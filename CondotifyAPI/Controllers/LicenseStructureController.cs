using AutoMapper;
using CondotifyAPI.Data.Structure;
using CondotifyAPI.Data.Deliveries;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Models;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/licenses/{licenseId:guid}")]
[Authorize]
public class LicenseStructureController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControlService;
    private readonly IMapper _mapper;

    public LicenseStructureController(
        DatabaseContext context,
        IAccessControlService accessControlService,
        IMapper mapper)
    {
        _context = context;
        _accessControlService = accessControlService;
        _mapper = mapper;
    }

    [HttpGet("structure")]
    [RequireLicensePermission(LicensePermissionEnum.ViewStructure)]
    public async Task<IActionResult> GetStructure(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var license = await _context.Licenses.AsNoTracking().FirstAsync(x => x.Id == licenseId);

        var blocks = await _context.Blocks
            .AsNoTracking()
            .Include(x => x.Units)
            .ThenInclude(x => x.Residents)
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(new LicenseStructureOut
        {
            LicenseId = licenseId,
            GroupLabelSingular = license.GroupLabelSingular,
            GroupLabelPlural = license.GroupLabelPlural,
            UnitLabelSingular = license.UnitLabelSingular,
            UnitLabelPlural = license.UnitLabelPlural,
            Blocks = blocks.Select(ToBlockOut).ToList()
        });
    }

    [HttpPatch("structure/settings")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> UpdateStructureSettings(Guid licenseId, [FromBody] UpdateStructureSettingsIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var license = await _context.Licenses.FirstAsync(x => x.Id == licenseId);
        license.GroupLabelSingular = RequiredLabel(input.GroupLabelSingular, "Bloco");
        license.GroupLabelPlural = RequiredLabel(input.GroupLabelPlural, "Blocos");
        license.UnitLabelSingular = RequiredLabel(input.UnitLabelSingular, "Unidade");
        license.UnitLabelPlural = RequiredLabel(input.UnitLabelPlural, "Unidades");
        await _context.SaveChangesAsync();
        return Ok(new { license.GroupLabelSingular, license.GroupLabelPlural, license.UnitLabelSingular, license.UnitLabelPlural });
    }

    [HttpPost("blocks")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> CreateBlock(Guid licenseId, [FromBody] CreateBlockIn input)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome do bloco e obrigatorio." });

        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var duplicate = await _context.Blocks.AnyAsync(x => x.LicenseId == licenseId && EF.Functions.ILike(x.Name, name));
        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe um bloco com este nome." });

        var block = new BlockDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Units = new()
        };

        _context.Blocks.Add(block);
        await _context.SaveChangesAsync();

        return Created("", ToBlockOut(block));
    }

    [HttpPost("units")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> CreateUnit(Guid licenseId, [FromBody] CreateUnitIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var number = input.Number?.Trim();
        if (input.BlockId == Guid.Empty || string.IsNullOrWhiteSpace(number))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Bloco e numero da unidade sao obrigatorios." });

        var block = await _context.Blocks.FirstOrDefaultAsync(x => x.Id == input.BlockId && x.LicenseId == licenseId);
        if (block == null) return NotFound();

        var duplicate = await _context.Units.AnyAsync(x => x.BlockId == input.BlockId && EF.Functions.ILike(x.Number, number));
        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe uma unidade com este numero neste bloco." });

        var unit = new UnitDTO
        {
            Id = Guid.NewGuid(),
            BlockId = input.BlockId,
            Number = number,
            Floor = input.Floor?.Trim() ?? string.Empty,
            Residents = new()
        };

        _context.Units.Add(unit);
        await _context.SaveChangesAsync();

        return Created("", new UnitOut
        {
            Id = unit.Id,
            BlockId = unit.BlockId,
            BlockName = block.Name,
            Number = unit.Number,
            Floor = unit.Floor,
            TotalResidents = 0
        });
    }

    [HttpPost("residents")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateResident(Guid licenseId, [FromBody] CreateResidentIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        if (input.UnitId == Guid.Empty || string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Unidade e nome do morador sao obrigatorios." });

        var unit = await _context.Units
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId);

        if (unit == null) return NotFound();

        var duplicate = await _context.Residents.AnyAsync(x =>
            x.UnitId == input.UnitId &&
            !string.IsNullOrWhiteSpace(input.CPF) &&
            x.CPF == input.CPF);

        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe um morador com este CPF nesta unidade." });

        var now = DateTime.UtcNow;
        var resident = new ResidentAccessDTO
        {
            Id = Guid.NewGuid(),
            UnitId = input.UnitId,
            Name = input.Name.Trim(),
            Email = input.Email?.Trim() ?? string.Empty,
            Password = input.Password ?? string.Empty,
            PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty,
            CommercialPhone = input.CommercialPhone?.Trim() ?? string.Empty,
            CPF = input.CPF?.Trim() ?? string.Empty,
            RG = input.RG?.Trim() ?? string.Empty,
            BirthDate = input.BirthDate?.Trim() ?? string.Empty,
            ApartmentNumber = string.IsNullOrWhiteSpace(input.ApartmentNumber) ? unit.Number : input.ApartmentNumber.Trim(),
            ImgUrl = string.Empty,
            Description = input.Description?.Trim() ?? string.Empty,
            AccessType = input.AccessType,
            FirstAccess = true,
            NotifyAccess = input.NotifyAccess,
            IsActive = true,
            Temporary = input.Temporary,
            Expire = input.Expire ?? now.AddYears(10),
            LastAccess = now,
            CreatedAt = now,
            AccessCredentials = new List<ResidentAccessCredentialDTO>()
        };

        _context.Residents.Add(resident);
        _context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(), ResidentId = resident.Id, UnitId = unit.Id,
            Relationship = input.Relationship, Description = resident.Description,
            IsPrimary = true, IsActive = true, StartsAt = now, CreatedAt = now, UpdatedAt = now
        });
        await _context.SaveChangesAsync();

        return Created("", ToResidentOut(resident, unit.Number));
    }

    [HttpGet("devices")]
    [RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
    public async Task<IActionResult> GetAccessDevices(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var devices = await _context.Devices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new AccessDeviceOut
            {
                Id = x.Id,
                Name = x.Name,
                IPAddress = x.IPAddress,
                Port = x.Port,
                Model = x.Model,
                SerialNumber = x.SerialNumber,
                MACAddress = x.MACAddress,
                Type = x.Type.ToString(),
                IsActive = x.IsActive,
                FirmwareVersion = x.FirmwareVersion,
                LastHealthCheckAt = x.LastHealthCheckAt,
                LastSeenAt = x.LastSeenAt,
                LastResponseTimeMs = x.LastResponseTimeMs,
                HealthMessage = x.HealthMessage,
                DiscoveredPortalsJson = x.DiscoveredPortalsJson
            })
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost("devices/{deviceId:guid}/test-connection")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> TestDeviceConnection(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await _context.Devices
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (device == null) return NotFound();

        var input = new CreateAccessControlDeviceByLicenseIn
        {
            LicenseId = licenseId.ToString(),
            Name = device.Name,
            IPAddress = device.IPAddress,
            Port = device.Port,
            Username = device.Username,
            Password = device.Password,
            MACAddress = device.MACAddress,
            Model = device.Model,
            SerialNumber = device.SerialNumber,
            FirmwareVersion = device.FirmwareVersion,
            Type = device.Type,
            IsActive = device.IsActive,
            Location = _mapper.Map<Location>(device.Location)
        };

        var connected = await _accessControlService.TestConnectionAsync(input);
        device.IsActive = connected;
        device.LastUpdatedAt = DateTime.UtcNow;
        AddDeviceAudit(device.Id, ActionTypeEnum.Update, connected ? "Teste de conexao: sucesso" : "Teste de conexao: falha");
        await _context.SaveChangesAsync();

        return connected
            ? Ok(new { Result = "Success", Message = "Conexao realizada e equipamento ativado." })
            : StatusCode(StatusCodes.Status502BadGateway, new { Result = "ConnectionFailed", Errors = "O equipamento nao respondeu. Verifique IP, porta e credenciais." });
    }

    [HttpPost("devices/{deviceId:guid}/open-door")]
    [RequireLicensePermission(LicensePermissionEnum.OperateDevices)]
    public async Task<IActionResult> OpenDoor(Guid licenseId, Guid deviceId, [FromBody] OpenDoorIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var deviceDto = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (deviceDto == null) return NotFound();

        if (!deviceDto.IsActive)
            return Conflict(new { Result = "InactiveDevice", Errors = "Teste a conexao e ative o equipamento antes de abrir a porta." });

        var success = false;
        string? operationError = null;
        try
        {
            success = await _accessControlService.OpenDoorAsync(_mapper.Map<AccessControlDevice>(deviceDto), input.Channel);
        }
        catch (NotSupportedException)
        {
            operationError = "Este modelo ainda nao possui acionamento remoto configurado.";
        }
        catch
        {
            operationError = "Falha inesperada ao comunicar com o equipamento.";
        }

        var reason = string.IsNullOrWhiteSpace(input.Reason) ? "Acionamento manual pelo portal" : input.Reason.Trim();
        AddDeviceAudit(deviceDto.Id, ActionTypeEnum.OpenDoor, $"Canal {input.Channel} | {(success ? "Sucesso" : "Falha")} | {reason}");
        await _context.SaveChangesAsync();

        if (!success)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                Result = "OpenDoorFailed",
                Errors = operationError ?? "O equipamento recebeu a solicitacao, mas nao confirmou o acionamento."
            });
        }

        return Ok(new { Result = "Success", Message = $"Porta {input.Channel} acionada com sucesso.", ExecutedAt = DateTime.UtcNow });
    }

    [HttpGet("devices/actions")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> GetDeviceActions(Guid licenseId, [FromQuery] int take = 20)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var actions = await _context.DeviceAudits
            .AsNoTracking()
            .Where(x => x.Device.LicenseId == licenseId)
            .OrderByDescending(x => x.Timestamp)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new DeviceActionOut
            {
                Id = x.Id,
                DeviceId = x.DeviceId,
                DeviceName = x.Device.Name,
                Action = x.Action.ToString(),
                Details = x.ChangedFields,
                UserName = x.UserName,
                Timestamp = x.Timestamp
            })
            .ToListAsync();

        return Ok(actions);
    }

    [HttpGet("cftv")]
    [RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
    public async Task<IActionResult> GetCftvDevices(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var devices = await _context.CFTVDevices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new CftvDeviceOut
            {
                Id = x.Id,
                Name = x.Name,
                IpAddress = x.IpAddress,
                HTTPPort = x.HTTPPort,
                RTSPPort = x.RTSPPort,
                DeviceType = x.DeviceType.ToString(),
                MaxChannels = x.MaxChannels
            })
            .ToListAsync();

        return Ok(devices);
    }

    [HttpGet("deliveries")]
    [RequireLicensePermission(LicensePermissionEnum.ViewDeliveries)]
    public async Task<IActionResult> GetDeliveries(Guid licenseId, [FromQuery] DeliveryStatusEnum? status = null)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var query = _context.Deliveries
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId);

        if (status.HasValue)
            query = query.Where(x => x.Status == status.Value);

        var deliveries = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDeliveryOut(x))
            .ToListAsync();

        return Ok(deliveries);
    }

    [HttpPost("deliveries")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDeliveries)]
    public async Task<IActionResult> CreateDelivery(Guid licenseId, [FromBody] CreateDeliveryIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome da encomenda e obrigatorio." });

        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var now = DateTime.UtcNow;
        var delivery = new DeliveryDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Type = input.Type,
            Status = DeliveryStatusEnum.Received,
            Name = input.Name.Trim(),
            Description = input.Description?.Trim() ?? string.Empty,
            TrackingCode = input.TrackingCode?.Trim() ?? string.Empty,
            PhotoUrl = input.PhotoUrl?.Trim() ?? string.Empty,
            DeliveryProofUrl = string.Empty,
            ReceivedBy = input.ReceivedBy?.Trim() ?? string.Empty,
            ReceivedAt = now,
            DeliveredTo = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Deliveries.Add(delivery);
        await _context.SaveChangesAsync();

        return Created("", ToDeliveryOut(delivery));
    }

    [HttpPatch("deliveries/{deliveryId:guid}/status")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDeliveries)]
    public async Task<IActionResult> UpdateDeliveryStatus(Guid licenseId, Guid deliveryId, [FromBody] UpdateDeliveryStatusIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var delivery = await _context.Deliveries
            .FirstOrDefaultAsync(x => x.Id == deliveryId && x.LicenseId == licenseId);

        if (delivery == null) return NotFound();

        if (input.Status is not DeliveryStatusEnum.Received and not DeliveryStatusEnum.Delivered and not DeliveryStatusEnum.Canceled)
            return BadRequest(new { Result = "InvalidRequest", Errors = "Status da encomenda invalido." });

        var now = DateTime.UtcNow;
        delivery.Status = input.Status;
        delivery.UpdatedAt = now;

        if (input.Status == DeliveryStatusEnum.Received)
        {
            delivery.ReceivedId = input.PersonId;
            delivery.ReceivedBy = input.PersonName?.Trim() ?? delivery.ReceivedBy;
            delivery.ReceivedAt ??= now;
        }

        if (input.Status == DeliveryStatusEnum.Delivered)
        {
            delivery.DeliveredToId = input.PersonId;
            delivery.DeliveredTo = input.PersonName?.Trim() ?? string.Empty;
            delivery.DeliveredAt = now;
            delivery.DeliveryProofUrl = input.ProofUrl?.Trim() ?? delivery.DeliveryProofUrl;
        }

        await _context.SaveChangesAsync();
        return Ok(ToDeliveryOut(delivery));
    }

    private Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return Task.FromResult(false);

        return _context.Licenses
            .AsNoTracking()
            .AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }

    private void AddDeviceAudit(Guid deviceId, ActionTypeEnum action, string details)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        var userName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";

        _context.DeviceAudits.Add(new DeviceAuditDTO
        {
            Id = Guid.NewGuid(),
            DeviceId = deviceId,
            Action = action,
            ChangedFields = details.Length <= 500 ? details : details[..500],
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            UserName = userName
        });
    }

    private static BlockOut ToBlockOut(BlockDTO block)
    {
        var units = block.Units?.OrderBy(x => x.Number).Select(unit => new UnitOut
        {
            Id = unit.Id,
            BlockId = block.Id,
            BlockName = block.Name,
            Number = unit.Number,
            Floor = unit.Floor,
            TotalResidents = unit.Residents?.Count ?? 0,
            Residents = unit.Residents?.Select(resident => ToResidentOut(resident, unit.Number)).ToList() ?? new()
        }).ToList() ?? new();

        return new BlockOut
        {
            Id = block.Id,
            Name = block.Name,
            TotalUnits = units.Count,
            TotalResidents = units.Sum(x => x.TotalResidents),
            Units = units
        };
    }

    private static ResidentOut ToResidentOut(ResidentAccessDTO resident, string unitNumber)
    {
        return new ResidentOut
        {
            Id = resident.Id,
            UnitId = resident.UnitId,
            UnitNumber = unitNumber,
            Name = resident.Name,
            Email = resident.Email,
            PhoneNumber = resident.PhoneNumber,
            CPF = resident.CPF,
            RG = resident.RG,
            ApartmentNumber = resident.ApartmentNumber,
            AccessType = resident.AccessType.ToString(),
            Temporary = resident.Temporary,
            Expire = resident.Expire
        };
    }

    private static DeliveryOut ToDeliveryOut(DeliveryDTO delivery)
    {
        return new DeliveryOut
        {
            Id = delivery.Id,
            LicenseId = delivery.LicenseId,
            Type = delivery.Type.ToString(),
            Status = delivery.Status.ToString(),
            StatusValue = (int)delivery.Status,
            Name = delivery.Name,
            Description = delivery.Description,
            TrackingCode = delivery.TrackingCode,
            PhotoUrl = delivery.PhotoUrl,
            DeliveryProofUrl = delivery.DeliveryProofUrl,
            ReceivedBy = delivery.ReceivedBy,
            ReceivedAt = delivery.ReceivedAt,
            DeliveredTo = delivery.DeliveredTo,
            DeliveredAt = delivery.DeliveredAt,
            CreatedAt = delivery.CreatedAt,
            UpdatedAt = delivery.UpdatedAt
        };
    }

    private static string RequiredLabel(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim()[..Math.Min(value.Trim().Length, 40)];
}
