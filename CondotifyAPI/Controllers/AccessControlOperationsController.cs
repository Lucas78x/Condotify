using System.Security.Claims;
using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Data.AccessControl;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/access-control")]
public sealed class AccessControlOperationsController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControl;
    private readonly IAccessRouteResolver _routeResolver;
    private readonly IDeviceInventoryService _inventory;
    private readonly IMapper _mapper;

    public AccessControlOperationsController(
        DatabaseContext context,
        IAccessControlService accessControl,
        IAccessRouteResolver routeResolver,
        IDeviceInventoryService inventory,
        IMapper mapper)
    {
        _context = context;
        _accessControl = accessControl;
        _routeResolver = routeResolver;
        _inventory = inventory;
        _mapper = mapper;
    }

    [HttpGet("devices/{deviceId:guid}/inventory")]
    [RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
    public async Task<IActionResult> GetInventory(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var items = await _inventory.GetAsync(licenseId, deviceId);
        return Ok(items.Select(ToInventoryOut));
    }

    [HttpPost("devices/{deviceId:guid}/inventory/refresh")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> RefreshInventory(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var result = await _inventory.RefreshAsync(licenseId, deviceId, CurrentUserName());
        return result.Success ? Ok(new DeviceInventorySummaryOut
        {
            Success = true, Message = result.Message, Synced = result.Synced, Divergent = result.Divergent,
            Missing = result.Missing, Orphan = result.Orphan
        }) : StatusCode(StatusCodes.Status502BadGateway, new { Errors = result.Message });
    }

    [HttpPost("devices/{deviceId:guid}/inventory/repair")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> RepairInventory(Guid licenseId, Guid deviceId, [FromBody] RepairInventoryIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credentialIds = await _context.AccessInventoryItems.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && x.DeviceId == deviceId && input.InventoryItemIds.Contains(x.Id) && x.CredentialId.HasValue)
            .Select(x => x.CredentialId!.Value).Distinct().ToListAsync();
        if (credentialIds.Count == 0) return BadRequest(new { Errors = "Selecione divergencias vinculadas a credenciais do Condotify." });
        var batch = QueueBatch(licenseId, credentialIds, CurrentUserName(), input.IdempotencyKey);
        await _context.SaveChangesAsync();
        return Accepted(ToBatchOut(batch));
    }

    [HttpPost("devices/{deviceId:guid}/inspect")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> InspectDevice(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var device = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId);
        if (device is null) return NotFound();
        var wasOnline = device.IsActive;

        DeviceInspectionResult inspection;
        try { inspection = await _accessControl.InspectAsync(_mapper.Map<AccessControlDevice>(device)); }
        catch (Exception exception) { inspection = DeviceInspectionResult.Unavailable(exception.Message); }

        var now = DateTime.UtcNow;
        device.IsActive = inspection.Online;
        device.LastHealthCheckAt = now;
        device.LastSeenAt = inspection.Online ? now : device.LastSeenAt;
        device.LastResponseTimeMs = inspection.ResponseTimeMs;
        device.HealthMessage = inspection.Message;
        device.FirmwareVersion = inspection.FirmwareVersion ?? device.FirmwareVersion;
        device.SerialNumber = inspection.SerialNumber ?? device.SerialNumber;
        device.MACAddress = inspection.MacAddress?.ToUpperInvariant() ?? device.MACAddress;
        device.CapacityJson = ValidJsonOrDefault(inspection.CapacityJson, "{}");
        device.DiscoveredPortalsJson = JsonSerializer.Serialize(inspection.Portals);
        device.LastUpdatedAt = now;
        AddAudit(licenseId, "Device", device.Id, "Inspect", inspection.Online ? "Success" : "Offline", inspection.Message, inspection);
        if (inspection.Online && !wasOnline)
        {
            var credentialIds = await _context.ResidentAccessCredentials
                .Where(x => x.Resident.Unit.Block.LicenseId == licenseId && x.IsActive)
                .Select(x => x.Id).ToListAsync();
            if (credentialIds.Count > 0)
                QueueBatch(licenseId, credentialIds, $"Equipamento {device.Name} voltou a ficar online");
        }
        await _context.SaveChangesAsync();

        return Ok(ToInspectionOut(device.Id, now, inspection));
    }

    [HttpGet("audits")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> GetAudits(Guid licenseId, [FromQuery] int take = 100)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var limit = Math.Clamp(take, 1, 500);
        var items = await _context.AccessOperationAudits.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new AccessAuditOut
            {
                Id = x.Id, EntityType = x.EntityType, EntityId = x.EntityId, Action = x.Action,
                Status = x.Status, Summary = x.Summary, DetailsJson = x.DetailsJson,
                UserName = x.UserName, CreatedAt = x.CreatedAt
            }).ToListAsync();
        var deviceAuditRows = await _context.DeviceAudits.AsNoTracking()
            .Where(x => x.Device.LicenseId == licenseId)
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(x => new { x.Id, x.DeviceId, x.Action, DeviceName = x.Device.Name, x.ChangedFields, x.UserName, x.Timestamp })
            .ToListAsync();
        var deviceItems = deviceAuditRows.Select(x => new AccessAuditOut
        {
            Id = x.Id, EntityType = "Device", EntityId = x.DeviceId, Action = x.Action.ToString(),
            Status = "Recorded", Summary = x.DeviceName + " | " + x.ChangedFields,
            DetailsJson = "{}", UserName = x.UserName, CreatedAt = x.Timestamp
        });
        return Ok(items.Concat(deviceItems).OrderByDescending(x => x.CreatedAt).Take(limit));
    }

    [HttpPost("reconciliation/preview")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> PreviewReconciliation(Guid licenseId, [FromBody] CreateReconciliationBatchIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var query = CredentialQuery(licenseId).AsNoTracking().Where(x => x.IsActive);
        if (input.CredentialIds.Count > 0) query = query.Where(x => input.CredentialIds.Contains(x.Id));
        var credentials = await query.ToListAsync();
        var preview = new ReconciliationPreviewOut
        {
            CredentialCount = credentials.Count,
            ResidentCount = credentials.Select(x => x.ResidentId).Distinct().Count(),
            PendingCount = credentials.SelectMany(x => x.Devices).Count(x => !x.IsSynced)
        };
        foreach (var credential in credentials)
        {
            var resolution = await _routeResolver.ResolveAsync(licenseId, credential.Resident, credential.CredentialType);
            preview.TargetCount += resolution.Targets.Count;
            if (resolution.Targets.Count == 0)
                preview.Warnings.Add($"{credential.Resident.Name}: nenhuma rota compativel para {credential.CredentialType}.");
            if (credential.CredentialType == AccessCredentialTypeEnum.Face && string.IsNullOrWhiteSpace(credential.Resident.ImgUrl))
                preview.Warnings.Add($"{credential.Resident.Name}: facial sem foto preparada.");
        }
        return Ok(preview);
    }

    [HttpPost("routes/simulate")]
    [RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
    public async Task<IActionResult> SimulateRoute(Guid licenseId, [FromBody] RouteSimulationIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var resident = await _context.Residents.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks)
            .FirstOrDefaultAsync(x => x.Id == input.ResidentId && x.Unit.Block.LicenseId == licenseId);
        if (resident is null) return NotFound();
        var resolution = await _routeResolver.ResolveAsync(licenseId, resident, input.CredentialType);
        var output = new RouteSimulationOut
        {
            Audience = resolution.Audience.ToString(), Routes = resolution.RouteNames.ToList(),
            Targets = resolution.Targets.Select(x => new RouteSimulationTargetOut
            {
                DeviceId = x.Device.Id, DeviceName = x.Device.Name, DeviceType = x.Device.Type.ToString(), Online = x.Device.IsActive,
                Portals = x.Portals.Select(p => $"{p.RouteName} - porta {p.PortalNumber} ({p.Direction})").ToList()
            }).ToList()
        };
        if (output.Routes.Count == 0) output.Warnings.Add("Nenhuma rota atende ao perfil desta pessoa.");
        if (output.Targets.Count == 0 && output.Routes.Count > 0) output.Warnings.Add("As rotas elegiveis nao possuem terminais compativeis e online.");
        if (input.CredentialType == AccessCredentialTypeEnum.Face && string.IsNullOrWhiteSpace(resident.ImgUrl)) output.Warnings.Add("A pessoa ainda nao possui foto facial preparada.");
        AddAudit(licenseId, "Resident", resident.Id, "RouteSimulation", "Success", $"Simulacao de {input.CredentialType} para {resident.Name}.", new { output.Audience, output.Routes, TargetCount = output.Targets.Count });
        await _context.SaveChangesAsync();
        return Ok(output);
    }

    [HttpPost("reconciliation/batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> QueueReconciliation(Guid licenseId, [FromBody] CreateReconciliationBatchIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var existing = !string.IsNullOrWhiteSpace(input.IdempotencyKey)
            ? await _context.AccessBatchOperations.Include(x => x.Items).ThenInclude(x => x.Device)
                .Include(x => x.Items).ThenInclude(x => x.Credential).ThenInclude(x => x!.Resident)
                .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.IdempotencyKey == input.IdempotencyKey)
            : null;
        if (existing is not null) return Accepted(ToBatchOut(existing));
        var batch = QueueBatch(licenseId, input.CredentialIds, CurrentUserName(), input.IdempotencyKey, input.Priority);
        await _context.SaveChangesAsync();
        return Accepted(ToBatchOut(batch));
    }

    [HttpGet("reconciliation/batches")]
    [RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
    public async Task<IActionResult> GetBatches(Guid licenseId, [FromQuery] int take = 20)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var batches = await _context.AccessBatchOperations.AsNoTracking()
            .Include(x => x.Items).ThenInclude(x => x.Device)
            .Include(x => x.Items).ThenInclude(x => x.Credential).ThenInclude(x => x!.Resident)
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();
        return Ok(batches.Select(ToBatchOut));
    }

    [HttpDelete("reconciliation/batches/{batchId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> CancelBatch(Guid licenseId, Guid batchId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var batch = await _context.AccessBatchOperations.FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId);
        if (batch is null) return NotFound();
        if (batch.Status != AccessBatchStatusEnum.Queued)
            return Conflict(new { Errors = "Somente operacoes que ainda estao na fila podem ser canceladas." });
        batch.Status = AccessBatchStatusEnum.Canceled;
        batch.FinishedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("reconciliation/batches/{batchId:guid}/retry")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> RetryBatch(Guid licenseId, Guid batchId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var batch = await _context.AccessBatchOperations.Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == batchId && x.LicenseId == licenseId);
        if (batch is null) return NotFound();
        if (batch.Status is not (AccessBatchStatusEnum.DeadLetter or AccessBatchStatusEnum.Failed or AccessBatchStatusEnum.CompletedWithErrors))
            return Conflict(new { Errors = "Somente operacoes com falha ou pendencias podem ser reprocessadas." });
        var now = DateTime.UtcNow;
        batch.Status = AccessBatchStatusEnum.Queued;
        batch.AttemptCount = 0;
        batch.NextAttemptAt = now;
        batch.FinishedAt = null;
        batch.LeaseOwner = string.Empty;
        batch.LeaseExpiresAt = null;
        batch.Error = string.Empty;
        foreach (var item in batch.Items.Where(x => x.Status != AccessOperationItemStatusEnum.Completed))
        {
            item.Status = AccessOperationItemStatusEnum.Queued;
            item.AttemptCount = 0;
            item.NextAttemptAt = now;
            item.FinishedAt = null;
            item.Error = string.Empty;
        }
        AddAudit(licenseId, "AccessBatch", batch.Id, "Retry", "Queued", "Operacao reenviada para processamento.", new { batch.Id });
        await _context.SaveChangesAsync();
        return Accepted(ToBatchOut(batch));
    }

    [HttpGet("credentials/backup")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> ExportBackup(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var credentials = await CredentialQuery(licenseId).AsNoTracking().OrderBy(x => x.Resident.Name).ToListAsync();
        return Ok(new CredentialBackupOut
        {
            LicenseId = licenseId,
            ExportedAt = DateTime.UtcNow,
            Credentials = credentials.Select(ToBackupItem).ToList()
        });
    }

    [HttpPost("credentials/backup/restore")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> RestoreBackup(Guid licenseId, [FromBody] CredentialBackupIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (input.SchemaVersion != 1 || input.Credentials.Count == 0)
            return BadRequest(new { Errors = "Arquivo de backup vazio ou com versao incompativel." });

        var residentIds = input.Credentials.Select(x => x.ResidentId).Distinct().ToList();
        var validResidents = await _context.Residents.Where(x => x.Unit.Block.LicenseId == licenseId && residentIds.Contains(x.Id))
            .Select(x => x.Id).ToHashSetAsync();
        if (validResidents.Count != residentIds.Count)
            return BadRequest(new { Errors = "O backup possui pessoas que nao pertencem a esta licenca." });

        var now = DateTime.UtcNow;
        var restoredIds = new List<Guid>();
        foreach (var item in input.Credentials)
        {
            if (string.IsNullOrWhiteSpace(item.Identifier) || item.ValidTo <= item.ValidFrom) continue;
            var credential = await _context.ResidentAccessCredentials.FirstOrDefaultAsync(x =>
                x.ResidentId == item.ResidentId && x.CredentialType == item.Type && x.Identifier == item.Identifier);
            if (credential is null)
            {
                credential = new ResidentAccessCredentialDTO { Id = Guid.NewGuid(), ResidentId = item.ResidentId, CreatedAt = now, Devices = [] };
                _context.ResidentAccessCredentials.Add(credential);
            }
            credential.CredentialType = item.Type;
            credential.Identifier = item.Identifier.Trim();
            credential.IsActive = item.IsActive;
            credential.IsTemporary = item.IsTemporary;
            credential.RenewalCount = item.RenewalCount;
            credential.MaxRenewals = item.MaxRenewals;
            credential.UseCount = item.UseCount;
            credential.MaxUses = item.MaxUses;
            credential.ValidFrom = Utc(item.ValidFrom);
            credential.ValidTo = Utc(item.ValidTo);
            credential.UpdatedAt = now;
            restoredIds.Add(credential.Id);
        }
        var batch = QueueBatch(licenseId, restoredIds, CurrentUserName());
        AddAudit(licenseId, "CredentialBackup", null, "Restore", "Queued", $"{restoredIds.Count} credencial(is) restaurada(s) para reconciliacao.", new { batch.Id });
        await _context.SaveChangesAsync();
        return Accepted(ToBatchOut(batch));
    }

    [HttpGet("residents/{residentId:guid}/route-overrides")]
    [RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
    public async Task<IActionResult> GetRouteOverrides(Guid licenseId, Guid residentId)
    {
        if (!await ResidentBelongsAsync(licenseId, residentId)) return NotFound();
        var items = await _context.AccessRouteResidentOverrides.AsNoTracking()
            .Where(x => x.ResidentId == residentId && x.AccessRoute.LicenseId == licenseId)
            .OrderBy(x => x.AccessRoute.Name)
            .Select(x => new ResidentRouteOverrideOut { Id = x.Id, RouteId = x.AccessRouteId, RouteName = x.AccessRoute.Name, Mode = x.Mode.ToString(), Reason = x.Reason })
            .ToListAsync();
        return Ok(items);
    }

    [HttpPut("residents/{residentId:guid}/route-overrides")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> SaveRouteOverride(Guid licenseId, Guid residentId, [FromBody] SaveResidentRouteOverrideIn input)
    {
        if (!await ResidentBelongsAsync(licenseId, residentId)) return NotFound();
        if (!await _context.AccessRoutes.AnyAsync(x => x.Id == input.RouteId && x.LicenseId == licenseId)) return NotFound();
        var item = await _context.AccessRouteResidentOverrides.FirstOrDefaultAsync(x => x.AccessRouteId == input.RouteId && x.ResidentId == residentId);
        var now = DateTime.UtcNow;
        if (item is null)
        {
            item = new AccessRouteResidentOverrideDTO { Id = Guid.NewGuid(), AccessRouteId = input.RouteId, ResidentId = residentId, CreatedAt = now };
            _context.AccessRouteResidentOverrides.Add(item);
        }
        item.Mode = input.Mode;
        item.Reason = input.Reason?.Trim() ?? string.Empty;
        item.UpdatedAt = now;
        var credentialIds = await _context.ResidentAccessCredentials.Where(x => x.ResidentId == residentId && x.IsActive).Select(x => x.Id).ToListAsync();
        QueueBatch(licenseId, credentialIds, CurrentUserName());
        AddAudit(licenseId, "Resident", residentId, "RouteOverride", "Queued", $"Excecao {input.Mode} configurada e enviada para reconciliacao.", input);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("residents/{residentId:guid}/route-overrides/{routeId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> DeleteRouteOverride(Guid licenseId, Guid residentId, Guid routeId)
    {
        if (!await ResidentBelongsAsync(licenseId, residentId)) return NotFound();
        var item = await _context.AccessRouteResidentOverrides.FirstOrDefaultAsync(x => x.AccessRouteId == routeId && x.ResidentId == residentId);
        if (item is null) return NotFound();
        _context.AccessRouteResidentOverrides.Remove(item);
        var credentialIds = await _context.ResidentAccessCredentials.Where(x => x.ResidentId == residentId && x.IsActive).Select(x => x.Id).ToListAsync();
        QueueBatch(licenseId, credentialIds, CurrentUserName());
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<ResidentAccessCredentialDTO> CredentialQuery(Guid licenseId) => _context.ResidentAccessCredentials
        .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.Resident).ThenInclude(x => x.UnitLinks)
        .Include(x => x.Devices)
        .Where(x => x.Resident.Unit.Block.LicenseId == licenseId);

    private AccessBatchOperationDTO QueueBatch(Guid licenseId, IReadOnlyCollection<Guid> credentialIds, string actor, string? idempotencyKey = null, int priority = 50)
    {
        var batch = new AccessBatchOperationDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials",
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? Guid.NewGuid().ToString("N") : idempotencyKey.Trim(),
            Status = AccessBatchStatusEnum.Queued, RequestedBy = actor, Priority = Math.Clamp(priority, 0, 100), MaxAttempts = 5,
            FilterJson = JsonSerializer.Serialize(new { credentialIds }), CreatedAt = DateTime.UtcNow
        };
        _context.AccessBatchOperations.Add(batch);
        return batch;
    }

    private void AddAudit(Guid licenseId, string entityType, Guid? entityId, string action, string status, string summary, object details)
    {
        _ = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = entityType, EntityId = entityId,
            Action = action, Status = status, Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
            UserId = userId == Guid.Empty ? null : userId, UserName = CurrentUserName(), CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await _context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }

    private async Task<bool> ResidentBelongsAsync(Guid licenseId, Guid residentId) =>
        await HasLicenseAccessAsync(licenseId) && await _context.Residents.AsNoTracking().AnyAsync(x => x.Id == residentId && x.Unit.Block.LicenseId == licenseId);

    private string CurrentUserName() => User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static string ValidJsonOrDefault(string value, string fallback) { try { JsonDocument.Parse(value).Dispose(); return value; } catch { return fallback; } }
    private static CredentialBackupItem ToBackupItem(ResidentAccessCredentialDTO x) => new()
    {
        ResidentId = x.ResidentId, Type = x.CredentialType, Identifier = x.Identifier, IsActive = x.IsActive,
        IsTemporary = x.IsTemporary, RenewalCount = x.RenewalCount, MaxRenewals = x.MaxRenewals,
        UseCount = x.UseCount, MaxUses = x.MaxUses, ValidFrom = x.ValidFrom, ValidTo = x.ValidTo
    };
    private static AccessBatchOperationOut ToBatchOut(AccessBatchOperationDTO x) => new()
    {
        Id = x.Id, Operation = x.Operation, Status = x.Status.ToString(), TotalItems = x.TotalItems,
        ProcessedItems = x.ProcessedItems, SuccessfulItems = x.SuccessfulItems, FailedItems = x.FailedItems,
        Priority = x.Priority, AttemptCount = x.AttemptCount, MaxAttempts = x.MaxAttempts,
        RequestedBy = x.RequestedBy, Error = x.Error, CreatedAt = x.CreatedAt, StartedAt = x.StartedAt, FinishedAt = x.FinishedAt,
        NextAttemptAt = x.NextAttemptAt,
        Items = x.Items.OrderBy(item => item.Device?.Name).Select(item => new AccessOperationItemOut
        {
            Id = item.Id, CredentialId = item.CredentialId, DeviceId = item.DeviceId,
            CredentialName = item.Credential?.Resident?.Name ?? string.Empty, DeviceName = item.Device?.Name ?? "Sem rota",
            Action = item.Action, Status = item.Status.ToString(), AttemptCount = item.AttemptCount,
            Error = item.Error, NextAttemptAt = item.NextAttemptAt
        }).ToList()
    };
    private static DeviceInventoryItemOut ToInventoryOut(AccessInventoryItemDTO x) => new()
    {
        Id = x.Id, CredentialId = x.CredentialId, RemoteKey = x.RemoteKey, ExternalUserId = x.ExternalUserId,
        CredentialType = x.CredentialType, Identifier = x.Identifier,
        PersonName = !string.IsNullOrWhiteSpace(x.PersonName) ? x.PersonName : x.Credential?.Resident?.Name ?? string.Empty,
        RemoteActive = x.RemoteActive, Status = x.Status.ToString(), ObservedAt = x.ObservedAt
    };
    private static DeviceInspectionOut ToInspectionOut(Guid deviceId, DateTime checkedAt, DeviceInspectionResult x) => new()
    {
        DeviceId = deviceId, Online = x.Online, ResponseTimeMs = x.ResponseTimeMs, Message = x.Message,
        FirmwareVersion = x.FirmwareVersion ?? string.Empty,
        SerialNumber = x.SerialNumber ?? string.Empty,
        MacAddress = x.MacAddress ?? string.Empty,
        CheckedAt = checkedAt,
        Portals = x.Portals.Select(p => new DevicePortalCapabilityOut { Number = p.Number, Name = p.Name, Direction = p.Direction.ToString(), Discovered = p.Discovered }).ToList()
    };
}
