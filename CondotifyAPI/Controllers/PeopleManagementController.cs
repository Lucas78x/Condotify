using System.Security.Claims;
using System.Security.Cryptography;
using System.Net.Mail;
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
using Microsoft.AspNetCore.RateLimiting;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Auditing;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Services.RecycleBin;
using CondotifyAPI.Services.Invitation;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}")]
public class PeopleManagementController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IPrivateMediaStore _media;
    private readonly IRecycleBinService _recycleBin;
    private readonly IRegistrationInviteEmailSender _inviteEmail;
    private readonly IRegistrationInviteSmsSender _inviteSms;
    private readonly IConfiguration _configuration;

    public PeopleManagementController(
        DatabaseContext context,
        IPrivateMediaStore media,
        IRecycleBinService recycleBin,
        IRegistrationInviteEmailSender inviteEmail,
        IRegistrationInviteSmsSender inviteSms,
        IConfiguration configuration)
    {
        _context = context;
        _media = media;
        _recycleBin = recycleBin;
        _inviteEmail = inviteEmail;
        _inviteSms = inviteSms;
        _configuration = configuration;
    }

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

    [HttpGet("vehicles/search")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> SearchVehiclesByPlate(Guid licenseId, [FromQuery] string plate)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (string.IsNullOrWhiteSpace(plate) || plate.Trim().Length < 2) return Ok(new List<VehiclePlateSearchOut>());
        var results = await SearchVehiclesByPlateCore(_context, licenseId, plate);
        return Ok(results);
    }

    internal static async Task<List<VehiclePlateSearchOut>> SearchVehiclesByPlateCore(DatabaseContext context, Guid licenseId, string plate)
    {
        var normalized = NormalizePlate(plate);
        if (string.IsNullOrWhiteSpace(normalized)) return [];
        return await context.Vehicles.AsNoTracking()
            .Where(x => x.IsActive && x.Unit.Block.LicenseId == licenseId && x.Plate.StartsWith(normalized))
            .OrderBy(x => x.Plate)
            .Take(10)
            .Select(x => new VehiclePlateSearchOut
            {
                VehicleId = x.Id,
                Plate = x.Plate,
                ResidentName = x.Resident != null ? x.Resident.Name : string.Empty,
                UnitLabel = x.Unit.Block.Name + " / " + x.Unit.Number
            })
            .ToListAsync();
    }

    [HttpGet("residents/{residentId:guid}/profile")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> GetProfile(Guid licenseId, Guid residentId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await ResidentQuery(licenseId).AsNoTracking().AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == residentId);
        return resident == null ? NotFound() : Ok(ToProfile(resident, licenseId));
    }

    [HttpPatch("residents/{residentId:guid}/profile")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> UpdateProfile(Guid licenseId, Guid residentId, [FromBody] UpdatePersonProfileIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await ResidentQuery(licenseId).FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident == null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Nome completo e obrigatorio." });

        var before = new
        {
            resident.Name, resident.Email, resident.PhoneNumber, resident.CommercialPhone,
            resident.CPF, resident.RG, resident.BirthDate, resident.Description,
            resident.NotifyAccess, resident.IsActive, HasPhoto = !string.IsNullOrWhiteSpace(resident.ImgUrl),
            Relationship = ResidentLicenseScope.ResolveLinkForLicense(resident, licenseId)?.Relationship
        };
        resident.Name = input.Name.Trim();
        resident.Email = input.Email?.Trim() ?? string.Empty;
        resident.PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty;
        resident.CommercialPhone = input.CommercialPhone?.Trim() ?? string.Empty;
        resident.CPF = input.CPF?.Trim() ?? string.Empty;
        resident.RG = input.RG?.Trim() ?? string.Empty;
        resident.BirthDate = input.BirthDate?.Trim() ?? string.Empty;
        resident.Description = input.Description?.Trim() ?? string.Empty;
        var previousPhoto = resident.ImgUrl;
        var requestedPhoto = input.ImageUrl?.Trim();
        if (requestedPhoto is not null && !string.Equals(requestedPhoto, previousPhoto, StringComparison.Ordinal))
        {
            if (requestedPhoto.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var validation = FaceImageValidator.Validate(requestedPhoto, 1_000_000);
                if (!validation.Success) return BadRequest(new { Errors = validation.Error });
                resident.ImgUrl = await _media.StoreDataUriAsync(licenseId, requestedPhoto, HttpContext.RequestAborted);
            }
            else if (string.IsNullOrWhiteSpace(requestedPhoto)) resident.ImgUrl = string.Empty;
            else if (requestedPhoto.StartsWith($"/private-media/{licenseId:D}/", StringComparison.OrdinalIgnoreCase)) resident.ImgUrl = requestedPhoto;
            else return BadRequest(new { Errors = "A referencia da foto nao e valida." });
        }
        resident.NotifyAccess = input.NotifyAccess;
        resident.IsActive = input.IsActive;
        var link = ResidentLicenseScope.ResolveLinkForLicense(resident, licenseId);
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
        }
        AddManagementAudit(
            licenseId,
            "Resident",
            resident.Id,
            "Updated",
            credentialIds.Count > 0
                ? $"Cadastro de {resident.Name} atualizado; {credentialIds.Count} credencial(is) enviada(s) para reconciliacao."
                : $"Cadastro de {resident.Name} atualizado.",
            new { ChangedFields = AuditChangeTracker.GetChangedFieldNames(before, new
            {
                resident.Name, resident.Email, resident.PhoneNumber, resident.CommercialPhone,
                resident.CPF, resident.RG, resident.BirthDate, resident.Description,
                resident.NotifyAccess, resident.IsActive,
                HasPhoto = !string.IsNullOrWhiteSpace(resident.ImgUrl),
                Relationship = input.Relationship
            }), CredentialReconciliationCount = credentialIds.Count },
            credentialIds.Count > 0 ? "Queued" : "Success");
        await _context.SaveChangesAsync();
        if (!string.Equals(previousPhoto, resident.ImgUrl, StringComparison.Ordinal))
            await _media.DeleteAsync(licenseId, previousPhoto, HttpContext.RequestAborted);
        return Ok(ToProfile(resident, licenseId));
    }

    [HttpPost("residents/{residentId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateVehicle(Guid licenseId, Guid residentId, [FromBody] CreateVehicleIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await _context.Residents
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .ForLicense(licenseId)
            .FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident == null) return NotFound();
        var allowedUnitIds = ResidentLicenseScope.ResolveUnitsForLicense(resident, licenseId).Select(unit => unit.Id).ToHashSet();
        var unitId = input.UnitId == Guid.Empty ? allowedUnitIds.FirstOrDefault() : input.UnitId;
        if (unitId == Guid.Empty || !allowedUnitIds.Contains(unitId)) return NotFound();
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
        AddManagementAudit(licenseId, "Vehicle", vehicle.Id, "Created", $"Veiculo {vehicle.Plate} cadastrado para {resident.Name}.", new { vehicle.Plate, vehicle.Brand, vehicle.Model, vehicle.Color, vehicle.Type, vehicle.TagIdentifier, vehicle.UnitId });
        await _context.SaveChangesAsync();
        return Created(string.Empty, ToVehicle(vehicle, resident.Name));
    }

    [HttpPatch("residents/{residentId:guid}/vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> UpdateVehicle(Guid licenseId, Guid residentId, Guid vehicleId, [FromBody] UpdateVehicleIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var vehicle = await _context.Vehicles.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == vehicleId && x.ResidentId == residentId && x.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        var resident = await _context.Residents.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .ForLicense(licenseId)
            .FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident is null) return NotFound();
        var allowedUnitIds = ResidentLicenseScope.ResolveUnitsForLicense(resident, licenseId).Select(unit => unit.Id).ToHashSet();
        var unitId = input.UnitId == Guid.Empty ? vehicle.UnitId : input.UnitId;
        if (unitId == Guid.Empty || !allowedUnitIds.Contains(unitId)) return NotFound();
        var plate = NormalizePlate(input.Plate);
        if (string.IsNullOrWhiteSpace(plate)) return BadRequest(new { Errors = "Placa do veiculo e obrigatoria." });
        if (await _context.Vehicles.AnyAsync(x => x.Id != vehicleId && x.UnitId == unitId && x.Plate == plate))
            return Conflict(new { Errors = "Este veiculo ja esta cadastrado na unidade." });

        var previous = new { vehicle.UnitId, vehicle.Plate, vehicle.Brand, vehicle.Model, vehicle.Color, vehicle.Type, vehicle.TagIdentifier, vehicle.IsActive };
        var oldTag = vehicle.TagIdentifier;
        var newTag = input.TagIdentifier?.Trim() ?? string.Empty;
        if (!string.Equals(oldTag, newTag, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(oldTag))
        {
            var oldCredential = await _context.ResidentAccessCredentials.Include(x => x.Devices).FirstOrDefaultAsync(x =>
                x.ResidentId == residentId && x.CredentialType == AccessCredentialTypeEnum.VehicleTag && x.Identifier == oldTag);
            if (oldCredential?.Devices.Count > 0)
                return Conflict(new { Result = "CredentialLinked", Errors = "Remova a TAG dos equipamentos antes de alterar o identificador do veiculo." });
            if (oldCredential != null) _context.ResidentAccessCredentials.Remove(oldCredential);
        }

        var now = DateTime.UtcNow;
        vehicle.UnitId = unitId;
        vehicle.Plate = plate;
        vehicle.Brand = input.Brand?.Trim() ?? string.Empty;
        vehicle.Model = input.Model?.Trim() ?? string.Empty;
        vehicle.Color = input.Color?.Trim() ?? string.Empty;
        vehicle.Type = input.Type?.Trim() ?? "Carro";
        vehicle.TagIdentifier = newTag;
        vehicle.IsActive = input.IsActive;
        vehicle.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(newTag) && !string.Equals(oldTag, newTag, StringComparison.OrdinalIgnoreCase))
        {
            var duplicateTag = await _context.ResidentAccessCredentials.AnyAsync(x =>
                x.ResidentId == residentId && x.CredentialType == AccessCredentialTypeEnum.VehicleTag && x.Identifier == newTag);
            if (duplicateTag) return Conflict(new { Result = "DuplicateTag", Errors = "Esta TAG ja esta vinculada a outra credencial da pessoa." });
            _context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
            {
                Id = Guid.NewGuid(), ResidentId = residentId, CredentialType = AccessCredentialTypeEnum.VehicleTag,
                Identifier = newTag, IsActive = input.IsActive, ValidFrom = now, ValidTo = now.AddYears(10),
                CreatedAt = now, Devices = new List<ResidentAccessDeviceDTO>()
            });
        }

        AddManagementAudit(licenseId, "Vehicle", vehicle.Id, "Updated", $"Veiculo {vehicle.Plate} atualizado.", new { Before = previous, After = new { vehicle.UnitId, vehicle.Plate, vehicle.Brand, vehicle.Model, vehicle.Color, vehicle.Type, vehicle.TagIdentifier, vehicle.IsActive } });
        await _context.SaveChangesAsync();
        return Ok(ToVehicle(vehicle));
    }

    [HttpDelete("residents/{residentId:guid}/vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> DeleteVehicle(Guid licenseId, Guid residentId, Guid vehicleId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var vehicle = await _context.Vehicles.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == vehicleId && x.ResidentId == residentId && x.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(vehicle.TagIdentifier))
        {
            var credential = await _context.ResidentAccessCredentials.Include(x => x.Devices).FirstOrDefaultAsync(x =>
                x.ResidentId == residentId && x.CredentialType == AccessCredentialTypeEnum.VehicleTag && x.Identifier == vehicle.TagIdentifier);
            if (credential?.Devices.Count > 0)
                return Conflict(new { Result = "CredentialLinked", Errors = "Remova a TAG dos equipamentos antes de excluir o veiculo." });
            if (credential != null) _context.ResidentAccessCredentials.Remove(credential);
        }

        _recycleBin.CaptureVehicle(licenseId, vehicle, CurrentActor());
        AddManagementAudit(licenseId, "Vehicle", vehicle.Id, "Trashed", $"Veiculo {vehicle.Plate} movido para a lixeira.", new { vehicle.Plate, vehicle.Brand, vehicle.Model, vehicle.Color, vehicle.Type, vehicle.TagIdentifier, vehicle.UnitId });
        _context.Vehicles.Remove(vehicle);
        await _context.SaveChangesAsync();
        return NoContent();
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
        AddManagementAudit(licenseId, "Vehicle", vehicle.Id, input.IsActive ? "Activated" : "Deactivated", $"Veiculo {vehicle.Plate} {(input.IsActive ? "ativado" : "desativado")}.", new { vehicle.Plate, vehicle.TagIdentifier, vehicle.IsActive });
        await _context.SaveChangesAsync();
        return Ok(ToVehicle(vehicle));
    }

    [HttpPost("residents/{residentId:guid}/registration-invites")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    [EnableRateLimiting("staff-registration-invite")]
    public async Task<IActionResult> CreateInvite(
        Guid licenseId,
        Guid residentId,
        [FromBody] CreateRegistrationInviteIn input,
        CancellationToken cancellationToken)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (!Enum.IsDefined(input.Channel))
            return BadRequest(new { Errors = "Selecione um canal de convite válido." });
        if (input.Channel == RegistrationInviteChannelEnum.WhatsApp)
            return BadRequest(new { Errors = "O envio automático por WhatsApp ainda não está disponível." });

        var resident = await _context.Residents
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .ForLicense(licenseId)
            .FirstOrDefaultAsync(x => x.Id == residentId, cancellationToken);
        if (resident == null) return NotFound();
        var contact = input.Contact?.Trim();
        if (string.IsNullOrWhiteSpace(contact))
            contact = input.Channel == RegistrationInviteChannelEnum.Email
                ? resident.Email
                : !string.IsNullOrWhiteSpace(resident.PhoneNumber) ? resident.PhoneNumber : resident.Email;
        if (string.IsNullOrWhiteSpace(contact)) return BadRequest(new { Errors = "Informe um celular ou e-mail para o convite." });
        if (contact.Length > 160) return BadRequest(new { Errors = "O contato deve ter no máximo 160 caracteres." });
        if (input.Channel == RegistrationInviteChannelEnum.Email && !IsValidEmail(contact))
            return BadRequest(new { Errors = "Informe um e-mail válido para enviar o convite." });
        if (input.Channel == RegistrationInviteChannelEnum.Sms && !_inviteSms.IsValidRecipient(contact))
            return BadRequest(new { Errors = "Informe um celular válido com DDD para enviar o convite." });

        var licenseName = await _context.Licenses.AsNoTracking()
            .Where(x => x.Id == licenseId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "seu condomínio";
        var smtpPolicy = input.Channel == RegistrationInviteChannelEnum.Email
            ? await _context.AlertNotificationPolicies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken)
            : null;
        if (input.Channel == RegistrationInviteChannelEnum.Email && !_inviteEmail.IsReady(smtpPolicy))
            return Conflict(new { Errors = "Configure o SMTP do condomínio antes de enviar convites por e-mail." });
        if (input.Channel == RegistrationInviteChannelEnum.Sms && !_inviteSms.IsReady())
            return Conflict(new { Errors = "Configure o provedor de SMS antes de enviar convites por celular." });

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        var inviteUrl = BuildPublicInviteUrl(token);
        if (input.Channel is RegistrationInviteChannelEnum.Email or RegistrationInviteChannelEnum.Sms &&
            !IsAbsoluteHttps(inviteUrl))
            return Conflict(new { Errors = "Configure o endereço público HTTPS do portal antes de enviar convites." });
        var invite = new RegistrationInviteDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, ResidentId = resident.Id, Contact = contact,
            Channel = input.Channel, Status = RegistrationInviteStatusEnum.Pending,
            TokenHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))),
            CreatedBy = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal",
            SendCount = 0, SentAt = now, ExpiresAt = now.AddDays(Math.Clamp(input.ValidDays, 1, 30)),
            CreatedAt = now, UpdatedAt = now
        };
        _context.RegistrationInvites.Add(invite);
        await _context.SaveChangesAsync(cancellationToken);

        if (input.Channel == RegistrationInviteChannelEnum.Email)
        {
            RegistrationInviteEmailResult delivery;
            try
            {
                delivery = await _inviteEmail.SendAsync(
                    smtpPolicy,
                    contact,
                    resident.Name,
                    licenseName,
                    inviteUrl,
                    invite.ExpiresAt,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _context.RegistrationInvites.Remove(invite);
                await _context.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            if (!delivery.Success)
            {
                _context.RegistrationInvites.Remove(invite);
                await _context.SaveChangesAsync(cancellationToken);
                return StatusCode(StatusCodes.Status502BadGateway, new { Errors = delivery.Error });
            }

            invite.SendCount = 1;
            invite.SentAt = DateTime.UtcNow;
            invite.UpdatedAt = invite.SentAt;
        }
        else if (input.Channel == RegistrationInviteChannelEnum.Sms)
        {
            RegistrationInviteSmsResult delivery;
            try
            {
                delivery = await _inviteSms.SendAsync(
                    contact,
                    resident.Name,
                    licenseName,
                    inviteUrl,
                    invite.ExpiresAt,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _context.RegistrationInvites.Remove(invite);
                await _context.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            if (!delivery.Success)
            {
                _context.RegistrationInvites.Remove(invite);
                await _context.SaveChangesAsync(cancellationToken);
                return StatusCode(StatusCodes.Status502BadGateway, new { Errors = delivery.Error });
            }

            invite.SendCount = 1;
            invite.SentAt = DateTime.UtcNow;
            invite.UpdatedAt = invite.SentAt;
        }

        var deliveryStatus = input.Channel switch
        {
            RegistrationInviteChannelEnum.Email => "EmailDelivered",
            RegistrationInviteChannelEnum.Sms => "SmsQueued",
            _ => "Created"
        };
        var auditSummary = input.Channel switch
        {
            RegistrationInviteChannelEnum.Email => $"Convite de cadastro enviado por e-mail para {resident.Name}.",
            RegistrationInviteChannelEnum.Sms => $"Convite de cadastro aceito para envio por SMS para {resident.Name}.",
            _ => $"Link de cadastro gerado para {resident.Name}."
        };
        AddManagementAudit(
            licenseId,
            "RegistrationInvite",
            invite.Id,
            deliveryStatus,
            auditSummary,
            new { ResidentId = resident.Id, input.Channel, invite.ExpiresAt });
        await _context.SaveChangesAsync(cancellationToken);
        return Created(string.Empty, ToInvite(invite, resident.Name, inviteUrl));
    }

    internal string BuildPublicInviteUrl(string token)
    {
        var path = $"/cadastro/convite/{token}";
        var baseUrl = Environment.GetEnvironmentVariable("CONDOTIFY_PUBLIC_PORTAL_URL")
            ?? _configuration["PublicPortal:BaseUrl"];
        return string.IsNullOrWhiteSpace(baseUrl) ? path : $"{baseUrl.TrimEnd('/')}{path}";
    }

    internal static bool IsValidEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 160) return false;
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static bool IsAbsoluteHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

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

    [HttpDelete("residents/{residentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> DeleteResident(Guid licenseId, Guid residentId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await ResidentQuery(licenseId).FirstOrDefaultAsync(x => x.Id == residentId);
        if (resident == null) return NotFound();
        if (resident.UnitLinks.Any(link => link.Unit.Block.LicenseId != licenseId))
            return Conflict(new
            {
                Result = "ResidentLinkedToAnotherLicense",
                Errors = "Esta pessoa tambem possui vinculo com outro condominio e nao pode ser excluida globalmente por esta tela."
            });
        if (resident.AccessCredentials.Count > 0 || resident.Vehicles.Count > 0)
            return Conflict(new
            {
                Result = "ResidentInUse",
                Errors = $"A pessoa possui {resident.AccessCredentials.Count} credencial(is) e {resident.Vehicles.Count} veiculo(s). Exclua esses vinculos antes de remover o cadastro."
            });

        var overrides = await _context.AccessRouteResidentOverrides
            .Where(x => x.ResidentId == resident.Id)
            .ToListAsync();
        var link = ResidentLicenseScope.ResolveLinkForLicense(resident, licenseId);
        _recycleBin.CaptureResident(
            licenseId,
            resident,
            link,
            resident.RegistrationInvites.ToList(),
            overrides,
            CurrentActor());
        AddManagementAudit(licenseId, "Resident", resident.Id, "Trashed", $"Pessoa {resident.Name} movida para a lixeira.", new { resident.UnitId, resident.AccessType, HasPhoto = !string.IsNullOrWhiteSpace(resident.ImgUrl) });
        _context.Residents.Remove(resident);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<ResidentAccessDTO> ResidentQuery(Guid licenseId) => _context.Residents
        .Include(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.AccessCredentials).ThenInclude(x => x.Devices).ThenInclude(x => x.Device)
        .Include(x => x.Vehicles)
        .Include(x => x.RegistrationInvites)
        .ForLicense(licenseId);

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId) =>
        Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId) &&
        await _context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);

    private void AddManagementAudit(
        Guid licenseId,
        string entityType,
        Guid entityId,
        string action,
        string summary,
        object details,
        string status = "Success")
    {
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Status = status,
            Summary = summary,
            DetailsJson = JsonSerializer.Serialize(details),
            UserName = User.FindFirstValue("name") ?? User.Identity?.Name ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        });
    }

    private string CurrentActor() =>
        User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";

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

    private static PersonProfileOut ToProfile(ResidentAccessDTO resident, Guid licenseId)
    {
        var link = ResidentLicenseScope.ResolveLinkForLicense(resident, licenseId);
        var unit = ResidentLicenseScope.ResolveUnitForLicense(resident, licenseId);
        var summary = ToSummary(resident, link);
        return new PersonProfileOut
        {
            Id = summary.Id, Name = summary.Name, Document = summary.Document, PhoneNumber = summary.PhoneNumber,
            Email = summary.Email, ImageUrl = summary.ImageUrl, Relationship = summary.Relationship,
            Description = summary.Description, IsActive = summary.IsActive, CredentialCount = summary.CredentialCount,
            VehicleCount = summary.VehicleCount, HasFaceCredential = summary.HasFaceCredential,
            HasActiveFaceCredential = summary.HasActiveFaceCredential, UnitId = unit?.Id ?? Guid.Empty, UnitNumber = unit?.Number ?? string.Empty,
            BlockName = unit?.Block?.Name ?? string.Empty, CPF = resident.CPF, RG = resident.RG, BirthDate = resident.BirthDate,
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

public class VehiclePlateSearchOut
{
    public Guid VehicleId { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
}
