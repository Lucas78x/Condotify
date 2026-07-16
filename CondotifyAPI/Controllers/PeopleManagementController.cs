using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Data.People;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}")]
public class PeopleManagementController : ControllerBase
{
    private readonly DatabaseContext _context;

    public PeopleManagementController(DatabaseContext context) => _context = context;

    [HttpGet("units/{unitId:guid}/details")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> GetUnit(Guid licenseId, Guid unitId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var unit = await _context.Units.AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.Residents).ThenInclude(x => x.AccessCredentials)
            .Include(x => x.Residents).ThenInclude(x => x.Vehicles)
            .Include(x => x.ResidentLinks)
            .Include(x => x.Vehicles).ThenInclude(x => x.Resident)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == unitId && x.Block.LicenseId == licenseId);
        if (unit == null) return NotFound();

        return Ok(new UnitDetailOut
        {
            Id = unit.Id, BlockId = unit.BlockId, BlockName = unit.Block.Name,
            Number = unit.Number, Floor = unit.Floor,
            People = unit.Residents.OrderBy(x => x.Name).Select(resident => ToSummary(resident,
                unit.ResidentLinks.FirstOrDefault(link => link.ResidentId == resident.Id && link.IsActive))).ToList(),
            Vehicles = unit.Vehicles.OrderBy(x => x.Plate).Select(x => ToVehicle(x)).ToList()
        });
    }

    [HttpGet("residents/{residentId:guid}/profile")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> GetProfile(Guid licenseId, Guid residentId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await ResidentQuery(licenseId).AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == residentId);
        return resident == null ? NotFound() : Ok(ToProfile(resident));
    }

    [HttpPatch("residents/{residentId:guid}/profile")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> UpdateProfile(Guid licenseId, Guid residentId, [FromBody] UpdatePersonProfileIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await ResidentQuery(licenseId).FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident == null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Nome completo e obrigatorio." });

        resident.Name = input.Name.Trim();
        resident.Email = input.Email?.Trim() ?? string.Empty;
        resident.PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty;
        resident.CommercialPhone = input.CommercialPhone?.Trim() ?? string.Empty;
        resident.CPF = input.CPF?.Trim() ?? string.Empty;
        resident.RG = input.RG?.Trim() ?? string.Empty;
        resident.BirthDate = input.BirthDate?.Trim() ?? string.Empty;
        resident.Description = input.Description?.Trim() ?? string.Empty;
        resident.ImgUrl = input.ImageUrl?.Trim() ?? resident.ImgUrl;
        resident.NotifyAccess = input.NotifyAccess;
        resident.IsActive = input.IsActive;
        var link = resident.UnitLinks.FirstOrDefault(x => x.IsPrimary) ?? resident.UnitLinks.FirstOrDefault();
        if (link != null)
        {
            link.Relationship = input.Relationship;
            link.Description = resident.Description;
            link.UpdatedAt = DateTime.UtcNow;
        }
        var credentialIds = resident.AccessCredentials.Select(x => x.Id).ToList();
        if (credentialIds.Count > 0)
        {
            _context.AccessBatchOperations.Add(new AccessBatchOperationDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials",
                Status = AccessBatchStatusEnum.Queued,
                RequestedBy = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Atualizacao da pessoa",
                FilterJson = JsonSerializer.Serialize(new { credentialIds }), CreatedAt = DateTime.UtcNow
            });
            _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Resident", EntityId = resident.Id,
                Action = "ProfileUpdated", Status = "Queued",
                Summary = $"Cadastro de {resident.Name} atualizado; {credentialIds.Count} credencial(is) enviada(s) para reconciliacao.",
                DetailsJson = JsonSerializer.Serialize(new { input.Relationship, input.IsActive, HasPhoto = !string.IsNullOrWhiteSpace(resident.ImgUrl) }),
                UserName = User.FindFirstValue("name") ?? User.Identity?.Name ?? string.Empty, CreatedAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        return Ok(ToProfile(resident));
    }

    [HttpPost("residents/{residentId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateVehicle(Guid licenseId, Guid residentId, [FromBody] CreateVehicleIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await _context.Residents.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == residentId && x.Unit.Block.LicenseId == licenseId);
        if (resident == null) return NotFound();
        var unitId = input.UnitId == Guid.Empty ? resident.UnitId : input.UnitId;
        var unitExists = await _context.Units.AnyAsync(x => x.Id == unitId && x.Block.LicenseId == licenseId);
        if (!unitExists) return NotFound();
        var plate = NormalizePlate(input.Plate);
        if (string.IsNullOrWhiteSpace(plate)) return BadRequest(new { Errors = "Placa do veiculo e obrigatoria." });
        if (await _context.Vehicles.AnyAsync(x => x.UnitId == unitId && x.Plate == plate))
            return Conflict(new { Errors = "Este veiculo ja esta cadastrado na unidade." });

        var now = DateTime.UtcNow;
        var vehicle = new VehicleDTO
        {
            Id = Guid.NewGuid(), UnitId = unitId, ResidentId = resident.Id, Plate = plate,
            Brand = input.Brand?.Trim() ?? string.Empty, Model = input.Model?.Trim() ?? string.Empty,
            Color = input.Color?.Trim() ?? string.Empty, Type = input.Type?.Trim() ?? "Carro",
            TagIdentifier = input.TagIdentifier?.Trim() ?? string.Empty, IsActive = true,
            CreatedAt = now, UpdatedAt = now
        };
        _context.Vehicles.Add(vehicle);
        if (!string.IsNullOrWhiteSpace(vehicle.TagIdentifier))
        {
            var credentialExists = await _context.ResidentAccessCredentials.AnyAsync(x =>
                x.ResidentId == resident.Id && x.CredentialType == AccessCredentialTypeEnum.VehicleTag && x.Identifier == vehicle.TagIdentifier);
            if (!credentialExists)
            {
                _context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
                {
                    Id = Guid.NewGuid(), ResidentId = resident.Id, CredentialType = AccessCredentialTypeEnum.VehicleTag,
                    Identifier = vehicle.TagIdentifier, IsActive = true, ValidFrom = now, ValidTo = now.AddYears(10),
                    CreatedAt = now, Devices = new List<ResidentAccessDeviceDTO>()
                });
            }
        }
        await _context.SaveChangesAsync();
        return Created(string.Empty, ToVehicle(vehicle, resident.Name));
    }

    [HttpPatch("residents/{residentId:guid}/vehicles/{vehicleId:guid}/status")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> SetVehicleStatus(Guid licenseId, Guid residentId, Guid vehicleId, [FromBody] VehicleStatusIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var vehicle = await _context.Vehicles.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == vehicleId && x.ResidentId == residentId && x.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();
        vehicle.IsActive = input.IsActive;
        vehicle.UpdatedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(vehicle.TagIdentifier))
        {
            var credential = await _context.ResidentAccessCredentials.FirstOrDefaultAsync(x =>
                x.ResidentId == residentId && x.CredentialType == AccessCredentialTypeEnum.VehicleTag && x.Identifier == vehicle.TagIdentifier);
            if (credential != null) credential.IsActive = input.IsActive;
        }
        await _context.SaveChangesAsync();
        return Ok(ToVehicle(vehicle));
    }

    [HttpPost("residents/{residentId:guid}/registration-invites")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateInvite(Guid licenseId, Guid residentId, [FromBody] CreateRegistrationInviteIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await _context.Residents.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == residentId && x.Unit.Block.LicenseId == licenseId);
        if (resident == null) return NotFound();
        var contact = input.Contact?.Trim();
        if (string.IsNullOrWhiteSpace(contact)) contact = !string.IsNullOrWhiteSpace(resident.PhoneNumber) ? resident.PhoneNumber : resident.Email;
        if (string.IsNullOrWhiteSpace(contact)) return BadRequest(new { Errors = "Informe um celular ou e-mail para o convite." });

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var invite = new RegistrationInviteDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, ResidentId = resident.Id, Contact = contact,
            Channel = input.Channel, Status = RegistrationInviteStatusEnum.Pending,
            TokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))),
            CreatedBy = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal",
            SendCount = 1, SentAt = now, ExpiresAt = now.AddDays(Math.Clamp(input.ValidDays, 1, 30)),
            CreatedAt = now, UpdatedAt = now
        };
        _context.RegistrationInvites.Add(invite);
        await _context.SaveChangesAsync();
        return Created(string.Empty, ToInvite(invite, resident.Name, $"/cadastro/convite/{token}"));
    }

    [HttpGet("registration-invites")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> GetInvites(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var now = DateTime.UtcNow;
        var invites = await _context.RegistrationInvites.Include(x => x.Resident)
            .Where(x => x.LicenseId == licenseId).OrderByDescending(x => x.SentAt).ToListAsync();
        foreach (var invite in invites.Where(x => x.Status == RegistrationInviteStatusEnum.Pending && x.ExpiresAt < now))
            invite.Status = RegistrationInviteStatusEnum.Expired;
        await _context.SaveChangesAsync();
        return Ok(invites.Select(x => ToInvite(x, x.Resident.Name)));
    }

    private IQueryable<ResidentAccessDTO> ResidentQuery(Guid licenseId) => _context.Residents
        .Include(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.UnitLinks).ThenInclude(x => x.Unit)
        .Include(x => x.AccessCredentials).ThenInclude(x => x.Devices).ThenInclude(x => x.Device)
        .Include(x => x.Vehicles)
        .Include(x => x.RegistrationInvites)
        .Where(x => x.Unit.Block.LicenseId == licenseId);

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId) =>
        Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId) &&
        await _context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);

    private static PersonSummaryOut ToSummary(ResidentAccessDTO resident, ResidentUnitLinkDTO? link) => new()
    {
        Id = resident.Id, Name = resident.Name, Document = !string.IsNullOrWhiteSpace(resident.CPF) ? resident.CPF : resident.RG,
        PhoneNumber = resident.PhoneNumber, Email = resident.Email, ImageUrl = resident.ImgUrl,
        Relationship = (link?.Relationship ?? RelationshipFromLegacy(resident.AccessType)).ToString(),
        Category = resident.AccessType.ToString(),
        Description = link?.Description ?? resident.Description, IsActive = resident.IsActive,
        CredentialCount = resident.AccessCredentials.Count, VehicleCount = resident.Vehicles.Count,
        HasFaceCredential = resident.AccessCredentials.Any(x => x.CredentialType == AccessCredentialTypeEnum.Face),
        HasActiveFaceCredential = resident.AccessCredentials.Any(x =>
            x.CredentialType == AccessCredentialTypeEnum.Face && x.IsActive && x.ValidTo > DateTime.UtcNow)
    };

    private static PersonProfileOut ToProfile(ResidentAccessDTO resident)
    {
        var link = resident.UnitLinks.FirstOrDefault(x => x.IsPrimary) ?? resident.UnitLinks.FirstOrDefault();
        var summary = ToSummary(resident, link);
        return new PersonProfileOut
        {
            Id = summary.Id, Name = summary.Name, Document = summary.Document, PhoneNumber = summary.PhoneNumber,
            Email = summary.Email, ImageUrl = summary.ImageUrl, Relationship = summary.Relationship,
            Description = summary.Description, IsActive = summary.IsActive, CredentialCount = summary.CredentialCount,
            VehicleCount = summary.VehicleCount, HasFaceCredential = summary.HasFaceCredential,
            HasActiveFaceCredential = summary.HasActiveFaceCredential, UnitId = resident.UnitId, UnitNumber = resident.Unit.Number,
            BlockName = resident.Unit.Block.Name, CPF = resident.CPF, RG = resident.RG, BirthDate = resident.BirthDate,
            CommercialPhone = resident.CommercialPhone, NotifyAccess = resident.NotifyAccess,
            Credentials = resident.AccessCredentials.OrderBy(x => x.CredentialType).Select(x => new PersonCredentialOut
            {
                Id = x.Id, Type = x.CredentialType.ToString(), Identifier = x.CredentialType == AccessCredentialTypeEnum.Password ? "********" : x.Identifier,
                IsActive = x.IsActive, IsTemporary = x.IsTemporary, RenewalCount = x.RenewalCount, MaxRenewals = x.MaxRenewals,
                DeviceCount = x.Devices.Count, SyncedDeviceCount = x.Devices.Count(device => device.IsSynced),
                UseCount = x.UseCount, MaxUses = x.MaxUses, ValidFrom = x.ValidFrom, ValidTo = x.ValidTo,
                Devices = x.Devices.OrderBy(device => device.Device.Name).Select(device => new PersonCredentialDeviceOut
                {
                    DeviceId = device.DeviceId, DeviceName = device.Device.Name, DeviceType = device.DeviceType.ToString(),
                    Status = device.SyncStatus.ToString(), Message = BindingMessage(device.ExtraJson),
                    RouteNames = device.RouteNames, PortalNumbers = device.PortalNumbers, AttemptCount = device.AttemptCount,
                    LastSyncAt = device.LastSyncAt, NextAttemptAt = device.NextAttemptAt
                }).ToList()
            }).ToList(),
            Vehicles = resident.Vehicles.OrderBy(x => x.Plate).Select(x => ToVehicle(x, resident.Name)).ToList(),
            Invites = resident.RegistrationInvites.OrderByDescending(x => x.SentAt).Select(x => ToInvite(x, resident.Name)).ToList()
        };
    }

    private static VehicleOut ToVehicle(VehicleDTO vehicle, string? ownerName = null) => new()
    {
        Id = vehicle.Id, UnitId = vehicle.UnitId, ResidentId = vehicle.ResidentId,
        OwnerName = ownerName ?? vehicle.Resident?.Name ?? string.Empty, Plate = vehicle.Plate,
        Brand = vehicle.Brand, Model = vehicle.Model, Color = vehicle.Color, Type = vehicle.Type,
        TagIdentifier = vehicle.TagIdentifier, IsActive = vehicle.IsActive
    };

    private static RegistrationInviteOut ToInvite(RegistrationInviteDTO invite, string residentName, string? url = null) => new()
    {
        Id = invite.Id, ResidentId = invite.ResidentId, ResidentName = residentName, Contact = invite.Contact,
        Channel = invite.Channel.ToString(), Status = invite.Status.ToString(), CreatedBy = invite.CreatedBy,
        SendCount = invite.SendCount, SentAt = invite.SentAt, ExpiresAt = invite.ExpiresAt, InviteUrl = url
    };

    private static string BindingMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("Message", out var value) || document.RootElement.TryGetProperty("message", out value)
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
        catch { return json; }
    }

    private static ResidentUnitRelationshipEnum RelationshipFromLegacy(ResidentAccessTypeEnum type) => type switch
    {
        ResidentAccessTypeEnum.Responsible => ResidentUnitRelationshipEnum.Responsible,
        ResidentAccessTypeEnum.NonResponsible => ResidentUnitRelationshipEnum.Resident,
        _ => ResidentUnitRelationshipEnum.Resident
    };

    private static string NormalizePlate(string? plate) => new((plate ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

public class VehicleStatusIn
{
    public bool IsActive { get; set; }
}
