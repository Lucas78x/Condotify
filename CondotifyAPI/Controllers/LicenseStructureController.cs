using AutoMapper;
using CondotifyAPI.Data.Structure;
using CondotifyAPI.Data.Deliveries;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.AccessControl;
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
using System.Text.Json;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.RecycleBin;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/licenses/{licenseId:guid}")]
[Authorize]
public class LicenseStructureController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControlService;
    private readonly IMapper _mapper;
    private readonly IRecycleBinService _recycleBin;

    public LicenseStructureController(
        DatabaseContext context,
        IAccessControlService accessControlService,
        IMapper mapper,
        IRecycleBinService recycleBin)
    {
        _context = context;
        _accessControlService = accessControlService;
        _mapper = mapper;
        _recycleBin = recycleBin;
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
        var before = new { license.GroupLabelSingular, license.GroupLabelPlural, license.UnitLabelSingular, license.UnitLabelPlural };
        license.GroupLabelSingular = RequiredLabel(input.GroupLabelSingular, "Bloco");
        license.GroupLabelPlural = RequiredLabel(input.GroupLabelPlural, "Blocos");
        license.UnitLabelSingular = RequiredLabel(input.UnitLabelSingular, "Unidade");
        license.UnitLabelPlural = RequiredLabel(input.UnitLabelPlural, "Unidades");
        AddManagementAudit(licenseId, "StructureSettings", licenseId, "Updated", "Nomenclatura da estrutura atualizada.", new { Before = before, After = new { license.GroupLabelSingular, license.GroupLabelPlural, license.UnitLabelSingular, license.UnitLabelPlural } });
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
        AddManagementAudit(licenseId, "Block", block.Id, "Created", $"Agrupamento {block.Name} criado.", new { block.Name });
        await _context.SaveChangesAsync();

        return Created("", ToBlockOut(block));
    }

    [HttpPatch("blocks/{blockId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> UpdateBlock(Guid licenseId, Guid blockId, [FromBody] UpdateBlockIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome do bloco e obrigatorio." });

        var block = await _context.Blocks.FirstOrDefaultAsync(x => x.Id == blockId && x.LicenseId == licenseId);
        if (block == null) return NotFound();
        if (await _context.Blocks.AnyAsync(x => x.Id != blockId && x.LicenseId == licenseId && EF.Functions.ILike(x.Name, name)))
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe um bloco com este nome." });

        var previousName = block.Name;
        block.Name = name;
        block.LastUpdatedAt = DateTime.UtcNow;
        AddManagementAudit(licenseId, "Block", block.Id, "Updated", $"Agrupamento {previousName} atualizado.", new { Before = new { Name = previousName }, After = new { block.Name } });
        await _context.SaveChangesAsync();
        return Ok(ToBlockOut(block));
    }

    [HttpDelete("blocks/{blockId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> DeleteBlock(Guid licenseId, Guid blockId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var block = await _context.Blocks.FirstOrDefaultAsync(x => x.Id == blockId && x.LicenseId == licenseId);
        if (block == null) return NotFound();
        if (await _context.Units.AnyAsync(x => x.BlockId == blockId))
            return Conflict(new { Result = "BlockNotEmpty", Errors = "Remova ou transfira as unidades antes de excluir este bloco." });

        _recycleBin.CaptureBlock(licenseId, block, CurrentActor());
        AddManagementAudit(licenseId, "Block", block.Id, "Trashed", $"Agrupamento {block.Name} movido para a lixeira.", new { block.Name });
        _context.Blocks.Remove(block);
        await _context.SaveChangesAsync();
        return NoContent();
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
        AddManagementAudit(licenseId, "Unit", unit.Id, "Created", $"Unidade {unit.Number} criada em {block.Name}.", new { unit.Number, unit.Floor, Block = block.Name });
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

    [HttpPatch("units/{unitId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> UpdateUnit(Guid licenseId, Guid unitId, [FromBody] UpdateUnitIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var number = input.Number?.Trim();
        if (input.BlockId == Guid.Empty || string.IsNullOrWhiteSpace(number))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Bloco e numero da unidade sao obrigatorios." });

        var unit = await _context.Units.Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == unitId && x.Block.LicenseId == licenseId);
        if (unit == null) return NotFound();
        var targetBlock = await _context.Blocks.FirstOrDefaultAsync(x => x.Id == input.BlockId && x.LicenseId == licenseId);
        if (targetBlock == null) return NotFound();
        if (await _context.Units.AnyAsync(x => x.Id != unitId && x.BlockId == input.BlockId && EF.Functions.ILike(x.Number, number)))
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe uma unidade com este numero neste bloco." });

        var before = new { unit.BlockId, Block = unit.Block.Name, unit.Number, unit.Floor };
        unit.BlockId = input.BlockId;
        unit.Number = number;
        unit.Floor = input.Floor?.Trim() ?? string.Empty;
        AddManagementAudit(licenseId, "Unit", unit.Id, "Updated", $"Unidade {unit.Number} atualizada.", new { Before = before, After = new { unit.BlockId, Block = targetBlock.Name, unit.Number, unit.Floor } });
        await _context.SaveChangesAsync();
        return Ok(new UnitOut
        {
            Id = unit.Id, BlockId = unit.BlockId, BlockName = targetBlock.Name,
            Number = unit.Number, Floor = unit.Floor,
            TotalResidents = await _context.Residents.CountAsync(x => x.UnitId == unit.Id)
        });
    }

    [HttpDelete("units/{unitId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageStructure)]
    public async Task<IActionResult> DeleteUnit(Guid licenseId, Guid unitId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var unit = await _context.Units.Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == unitId && x.Block.LicenseId == licenseId);
        if (unit == null) return NotFound();

        var people = await _context.Residents.CountAsync(x => x.UnitId == unitId);
        var vehicles = await _context.Vehicles.CountAsync(x => x.UnitId == unitId);
        if (people > 0 || vehicles > 0)
            return Conflict(new
            {
                Result = "UnitNotEmpty",
                Errors = $"A unidade possui {people} pessoa(s) e {vehicles} veiculo(s). Remova ou transfira esses cadastros antes de excluir."
            });

        _recycleBin.CaptureUnit(licenseId, unit, CurrentActor());
        AddManagementAudit(licenseId, "Unit", unit.Id, "Trashed", $"Unidade {unit.Number} movida para a lixeira.", new { unit.Number, unit.Floor, Block = unit.Block.Name });
        _context.Units.Remove(unit);
        await _context.SaveChangesAsync();
        return NoContent();
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
        AddManagementAudit(licenseId, "Resident", resident.Id, "Created", $"Pessoa {resident.Name} cadastrada na unidade {unit.Number}.", new { Unit = unit.Number, Block = unit.Block.Name, input.Relationship, input.AccessType, HasPhoto = false });
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
                Username = x.Username,
                Model = x.Model,
                SerialNumber = x.SerialNumber ?? string.Empty,
                MACAddress = x.MACAddress ?? string.Empty,
                Type = x.Type.ToString(),
                IsActive = x.IsActive,
                FirmwareVersion = x.FirmwareVersion ?? string.Empty,
                LastHealthCheckAt = x.LastHealthCheckAt,
                LastSeenAt = x.LastSeenAt,
                LastResponseTimeMs = x.LastResponseTimeMs,
                HealthMessage = x.HealthMessage,
                DiscoveredPortalsJson = x.DiscoveredPortalsJson
            })
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPatch("devices/{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> UpdateAccessDevice(Guid licenseId, Guid deviceId, [FromBody] UpdateAccessDeviceIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var name = input.Name?.Trim();
        var ipAddress = input.IPAddress?.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ipAddress) || input.Port is < 1 or > 65535)
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome, endereco IP e uma porta valida sao obrigatorios." });

        var device = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (device == null) return NotFound();
        if (await _context.Devices.AnyAsync(x => x.Id != deviceId && x.LicenseId == licenseId && x.IPAddress == ipAddress && x.Port == input.Port))
            return Conflict(new { Result = "DuplicateDevice", Errors = "Ja existe um equipamento com o mesmo IP e porta nesta licenca." });

        var before = new { device.Name, device.IPAddress, device.Port, device.Username, device.IsActive };
        device.Name = name;
        device.IPAddress = ipAddress;
        device.Port = input.Port;
        device.Username = input.Username?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(input.Password)) device.Password = input.Password;
        device.IsActive = input.IsActive;
        device.LastUpdatedAt = DateTime.UtcNow;
        AddDeviceAudit(device.Id, ActionTypeEnum.Update, "Configuracao do equipamento atualizada");
        AddManagementAudit(licenseId, "Device", device.Id, "Updated", $"Equipamento {device.Name} atualizado.", new { Before = before, After = new { device.Name, device.IPAddress, device.Port, device.Username, device.IsActive }, PasswordChanged = !string.IsNullOrWhiteSpace(input.Password) });
        await _context.SaveChangesAsync();
        return Ok(new { device.Id, device.Name, device.IPAddress, device.Port, device.IsActive });
    }

    [HttpDelete("devices/{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> DeleteAccessDevice(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var device = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (device == null) return NotFound();

        var routeCount = await _context.AccessRouteDevices.CountAsync(x => x.DeviceId == deviceId);
        var bindingCount = await _context.ResidentAccessDevices.CountAsync(x => x.DeviceId == deviceId);
        var eventCount = await _context.AccessEventRecords.CountAsync(x => x.DeviceId == deviceId);
        if (routeCount > 0 || bindingCount > 0 || eventCount > 0)
            return Conflict(new
            {
                Result = "DeviceInUse",
                Errors = $"O equipamento possui {routeCount} rota(s), {bindingCount} credencial(is) e {eventCount} evento(s). Desative-o para preservar o historico ou remova os vinculos antes da exclusao."
            });

        _recycleBin.CaptureDevice(licenseId, device, CurrentActor());
        AddManagementAudit(licenseId, "Device", device.Id, "Trashed", $"Equipamento {device.Name} movido para a lixeira.", new { device.Name, device.IPAddress, device.Port, device.Model, device.SerialNumber, device.MACAddress });
        _context.Devices.Remove(device);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("devices/{deviceId:guid}/test-connection")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> TestDeviceConnection(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await _context.Devices
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (device == null) return NotFound();

        DeviceInspectionResult inspection;
        try
        {
            inspection = await _accessControlService.InspectAsync(_mapper.Map<AccessControlDevice>(device));
        }
        catch (Exception exception)
        {
            inspection = DeviceInspectionResult.Unavailable(exception.Message);
        }

        var now = DateTime.UtcNow;
        var connected = inspection.Online;
        device.IsActive = connected;
        device.LastHealthCheckAt = now;
        device.LastSeenAt = connected ? now : device.LastSeenAt;
        device.LastResponseTimeMs = inspection.ResponseTimeMs;
        device.HealthMessage = inspection.Message;
        device.FirmwareVersion = inspection.FirmwareVersion ?? device.FirmwareVersion;
        device.SerialNumber = inspection.SerialNumber ?? device.SerialNumber;
        device.MACAddress = inspection.MacAddress?.ToUpperInvariant() ?? device.MACAddress;
        device.CapacityJson = ValidJsonOrDefault(inspection.CapacityJson, "{}");
        device.DiscoveredPortalsJson = System.Text.Json.JsonSerializer.Serialize(inspection.Portals);
        device.LastUpdatedAt = now;
        AddDeviceAudit(device.Id, ActionTypeEnum.Update, connected ? "Inspecao do equipamento: sucesso" : "Inspecao do equipamento: falha");
        await _context.SaveChangesAsync();

        return connected
            ? Ok(new
            {
                Result = "Success",
                Message = "Equipamento identificado e inventario atualizado.",
                device.SerialNumber,
                device.MACAddress,
                device.FirmwareVersion
            })
            : StatusCode(StatusCodes.Status502BadGateway, new { Result = "ConnectionFailed", Errors = inspection.Message });
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

    private void AddManagementAudit(
        Guid licenseId,
        string entityType,
        Guid entityId,
        string action,
        string summary,
        object details)
    {
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Status = "Success",
            Summary = summary,
            DetailsJson = JsonSerializer.Serialize(details),
            UserName = User.FindFirstValue("name") ?? User.Identity?.Name ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        });
    }

    private string CurrentActor() =>
        User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";

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

    private static string ValidJsonOrDefault(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(value);
            return value;
        }
        catch (System.Text.Json.JsonException)
        {
            return fallback;
        }
    }
}
