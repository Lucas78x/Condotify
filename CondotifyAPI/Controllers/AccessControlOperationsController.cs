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
    private readonly IMapper _mapper;

    public AccessControlOperationsController(
        DatabaseContext context,
        IAccessControlService accessControl,
        IAccessRouteResolver routeResolver,
        IMapper mapper)
    {
        _context = context;
        _accessControl = accessControl;
        _routeResolver = routeResolver;
        _mapper = mapper;
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
                Status = x.Status, Summary = x.Summary, UserName = x.UserName, CreatedAt = x.CreatedAt
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
            UserName = x.UserName, CreatedAt = x.Timestamp
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

    [HttpPost("reconciliation/batches")]
    [RequireLicensePermission(LicensePermissionEnum.ManageCredentials)]
    public async Task<IActionResult> QueueReconciliation(Guid licenseId, [FromBody] CreateReconciliationBatchIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var batch = QueueBatch(licenseId, input.CredentialIds, CurrentUserName());
        await _context.SaveChangesAsync();
        return Accepted(ToBatchOut(batch));
    }

    [HttpGet("reconciliation/batches")]
    [RequireLicensePermission(LicensePermissionEnum.ViewCredentials)]
    public async Task<IActionResult> GetBatches(Guid licenseId, [FromQuery] int take = 20)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        var batches = await _context.AccessBatchOperations.AsNoTracking()
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

    private AccessBatchOperationDTO QueueBatch(Guid licenseId, IReadOnlyCollection<Guid> credentialIds, string actor)
    {
        var batch = new AccessBatchOperationDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials",
            Status = AccessBatchStatusEnum.Queued, RequestedBy = actor,
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
        RequestedBy = x.RequestedBy, Error = x.Error, CreatedAt = x.CreatedAt, StartedAt = x.StartedAt, FinishedAt = x.FinishedAt
    };
    private static DeviceInspectionOut ToInspectionOut(Guid deviceId, DateTime checkedAt, DeviceInspectionResult x) => new()
    {
        DeviceId = deviceId, Online = x.Online, ResponseTimeMs = x.ResponseTimeMs, Message = x.Message,
        FirmwareVersion = x.FirmwareVersion ?? string.Empty, CheckedAt = checkedAt,
        Portals = x.Portals.Select(p => new DevicePortalCapabilityOut { Number = p.Number, Name = p.Name, Direction = p.Direction.ToString(), Discovered = p.Discovered }).ToList()
    };
}
