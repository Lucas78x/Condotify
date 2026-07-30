using System.Security.Claims;
using System.Text.Json;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/operations/alerts")]
public sealed class OperationalAlertsController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string status = "active",
        [FromQuery] string severity = "",
        [FromQuery] string type = "",
        [FromQuery] Guid? licenseId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var licenseIds = await authorization.GetLicenseIdsWithPermissionAsync(
            User,
            LicensePermissionEnum.ViewAlerts,
            HttpContext.RequestAborted);
        var manageableLicenseIds = await authorization.GetLicenseIdsWithPermissionAsync(
            User,
            LicensePermissionEnum.ManageAlerts,
            HttpContext.RequestAborted);
        if (licenseId.HasValue)
            licenseIds.IntersectWith([licenseId.Value]);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);
        var baseQuery = context.OperationalAlerts.AsNoTracking()
            .Include(x => x.License)
            .Where(x => x.LicenseId != null && licenseIds.Contains(x.LicenseId.Value));
        var filtered = ApplyStatus(baseQuery, status);
        if (Enum.TryParse<OperationalAlertSeverity>(severity, true, out var severityValue))
            filtered = filtered.Where(x => x.Severity == severityValue);
        if (!string.IsNullOrWhiteSpace(type))
            filtered = filtered.Where(x => x.Type == type.Trim());

        var total = await filtered.CountAsync(HttpContext.RequestAborted);
        var summary = await baseQuery
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Open = group.Count(x => x.Status == OperationalAlertStatus.Open),
                Acknowledged = group.Count(x => x.Status == OperationalAlertStatus.Acknowledged),
                Critical = group.Count(x =>
                    x.Status != OperationalAlertStatus.Resolved &&
                    x.Severity == OperationalAlertSeverity.Critical)
            })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        var alertEntities = await filtered
            .OrderBy(x => x.Status == OperationalAlertStatus.Resolved)
            .ThenByDescending(x => x.Severity)
            .ThenByDescending(x => x.LastOccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(HttpContext.RequestAborted);
        var items = alertEntities.Select(ToOut).ToList();
        foreach (var item in items)
            item.CanManage = item.LicenseId.HasValue && manageableLicenseIds.Contains(item.LicenseId.Value);

        return Ok(new OperationalAlertPageOut
        {
            Total = total,
            Open = summary?.Open ?? 0,
            Acknowledged = summary?.Acknowledged ?? 0,
            Critical = summary?.Critical ?? 0,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary()
    {
        var licenseIds = await authorization.GetLicenseIdsWithPermissionAsync(
            User,
            LicensePermissionEnum.ViewAlerts,
            HttpContext.RequestAborted);
        var summary = await context.OperationalAlerts.AsNoTracking()
            .Where(x => x.LicenseId != null &&
                        licenseIds.Contains(x.LicenseId.Value) &&
                        x.Status != OperationalAlertStatus.Resolved)
            .GroupBy(_ => 1)
            .Select(group => new OperationalAlertSummaryOut
            {
                Active = group.Count(x => x.SuppressedUntil == null || x.SuppressedUntil <= DateTime.UtcNow),
                Critical = group.Count(x =>
                    x.Severity == OperationalAlertSeverity.Critical &&
                    (x.SuppressedUntil == null || x.SuppressedUntil <= DateTime.UtcNow)),
                Warning = group.Count(x =>
                    x.Severity == OperationalAlertSeverity.Warning &&
                    (x.SuppressedUntil == null || x.SuppressedUntil <= DateTime.UtcNow)),
                Suppressed = group.Count(x => x.SuppressedUntil > DateTime.UtcNow)
            })
            .FirstOrDefaultAsync(HttpContext.RequestAborted);
        return Ok(summary ?? new OperationalAlertSummaryOut());
    }

    [HttpPost("{alertId:guid}/acknowledge")]
    public Task<IActionResult> Acknowledge(Guid alertId, [FromBody] OperationalAlertActionIn input) =>
        ChangeStatusAsync(alertId, input, OperationalAlertStatus.Acknowledged);

    [HttpPost("{alertId:guid}/resolve")]
    public Task<IActionResult> Resolve(Guid alertId, [FromBody] OperationalAlertActionIn input) =>
        ChangeStatusAsync(alertId, input, OperationalAlertStatus.Resolved);

    [HttpPost("{alertId:guid}/reopen")]
    public Task<IActionResult> Reopen(Guid alertId, [FromBody] OperationalAlertActionIn input) =>
        ChangeStatusAsync(alertId, input, OperationalAlertStatus.Open);

    [HttpPost("{alertId:guid}/snooze")]
    public async Task<IActionResult> Snooze(
        Guid alertId,
        [FromBody] SnoozeOperationalAlertIn input)
    {
        if (input.Minutes is < 15 or > 10080)
            return BadRequest(new { Result = "InvalidSnooze", Errors = "O silêncio deve ficar entre 15 minutos e 7 dias." });
        if (input.Note.Trim().Length > 500)
            return BadRequest(new { Result = "InvalidNote", Errors = "A observação deve ter no máximo 500 caracteres." });
        var alert = await FindManageableAsync(alertId);
        if (alert.Result is not null) return alert.Result;

        var now = DateTime.UtcNow;
        alert.Alert!.SuppressedUntil = now.AddMinutes(input.Minutes);
        alert.Alert.SuppressedById = CurrentUserId();
        alert.Alert.SuppressedBy = CurrentActor();
        alert.Alert.SuppressionReason = input.Note.Trim();
        alert.Alert.UpdatedAt = now;
        AddLifecycleAudit(alert.Alert, "Snoozed", $"Alerta silenciado até {alert.Alert.SuppressedUntil:O}.", input.Note);
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(alert.Alert));
    }

    [HttpPost("{alertId:guid}/unsnooze")]
    public async Task<IActionResult> Unsnooze(Guid alertId, [FromBody] OperationalAlertActionIn input)
    {
        var alert = await FindManageableAsync(alertId);
        if (alert.Result is not null) return alert.Result;
        alert.Alert!.SuppressedUntil = null;
        alert.Alert.SuppressedById = null;
        alert.Alert.SuppressedBy = string.Empty;
        alert.Alert.SuppressionReason = string.Empty;
        alert.Alert.UpdatedAt = DateTime.UtcNow;
        AddLifecycleAudit(alert.Alert, "Unsnoozed", "Silêncio do alerta removido.", input.Note);
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(alert.Alert));
    }

    private async Task<IActionResult> ChangeStatusAsync(
        Guid alertId,
        OperationalAlertActionIn input,
        OperationalAlertStatus targetStatus)
    {
        if (input.Note.Trim().Length > 500)
            return BadRequest(new { Result = "InvalidNote", Errors = "A observação deve ter no máximo 500 caracteres." });

        var alert = await context.OperationalAlerts
            .Include(x => x.License)
            .FirstOrDefaultAsync(x => x.Id == alertId, HttpContext.RequestAborted);
        if (alert?.LicenseId is not Guid licenseId)
            return NotFound();
        if (!await authorization.HasPermissionAsync(
                User,
                licenseId,
                LicensePermissionEnum.ManageAlerts,
                HttpContext.RequestAborted))
            return Forbid();
        if (targetStatus == OperationalAlertStatus.Acknowledged &&
            alert.Status == OperationalAlertStatus.Resolved)
            return Conflict(new
            {
                Result = "InvalidTransition",
                Errors = "Um alerta resolvido precisa ser reaberto antes de ser reconhecido."
            });
        if (targetStatus == OperationalAlertStatus.Open &&
            alert.Status != OperationalAlertStatus.Resolved)
            return Conflict(new
            {
                Result = "InvalidTransition",
                Errors = "Somente alertas resolvidos podem ser reabertos."
            });
        if (targetStatus == OperationalAlertStatus.Resolved &&
            alert.Status == OperationalAlertStatus.Resolved)
            return Ok(ToOut(alert));

        var now = DateTime.UtcNow;
        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : (Guid?)null;
        var actor = User.Identity?.Name ?? "Usuário";
        alert.Status = targetStatus;
        alert.UpdatedAt = now;

        if (targetStatus == OperationalAlertStatus.Acknowledged)
        {
            alert.AcknowledgedAt = now;
            alert.AcknowledgedById = userId;
            alert.AcknowledgedBy = actor;
            alert.AcknowledgementNote = input.Note.Trim();
            alert.ResolvedAt = null;
            alert.ResolvedById = null;
            alert.ResolvedBy = string.Empty;
            alert.ResolutionNote = string.Empty;
        }
        else if (targetStatus == OperationalAlertStatus.Resolved)
        {
            alert.ResolvedAt = now;
            alert.ResolvedById = userId;
            alert.ResolvedBy = actor;
            alert.ResolutionNote = input.Note.Trim();
            ClearSuppression(alert);
        }
        else
        {
            alert.AcknowledgedAt = null;
            alert.AcknowledgedById = null;
            alert.AcknowledgedBy = string.Empty;
            alert.AcknowledgementNote = string.Empty;
            alert.ResolvedAt = null;
            alert.ResolvedById = null;
            alert.ResolvedBy = string.Empty;
            alert.ResolutionNote = string.Empty;
            ClearSuppression(alert);
        }

        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "OperationalAlert",
            EntityId = alert.Id,
            Action = targetStatus.ToString(),
            Status = "Success",
            Summary = $"{alert.Title}: {StatusLabel(targetStatus)}",
            DetailsJson = JsonSerializer.Serialize(new { input.Note, AlertType = alert.Type }),
            UserId = userId,
            UserName = actor,
            CreatedAt = now
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOut(alert));
    }

    private async Task<(OperationalAlertDTO? Alert, IActionResult? Result)> FindManageableAsync(Guid alertId)
    {
        var alert = await context.OperationalAlerts
            .Include(x => x.License)
            .FirstOrDefaultAsync(x => x.Id == alertId, HttpContext.RequestAborted);
        if (alert?.LicenseId is not Guid licenseId)
            return (null, NotFound());
        if (!await authorization.HasPermissionAsync(
                User,
                licenseId,
                LicensePermissionEnum.ManageAlerts,
                HttpContext.RequestAborted))
            return (null, Forbid());
        return (alert, null);
    }

    private void AddLifecycleAudit(
        OperationalAlertDTO alert,
        string action,
        string summary,
        string note)
    {
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = alert.LicenseId!.Value,
            EntityType = "OperationalAlert",
            EntityId = alert.Id,
            Action = action,
            Status = "Success",
            Summary = summary,
            DetailsJson = JsonSerializer.Serialize(new { note, alert.Type }),
            UserId = CurrentUserId(),
            UserName = CurrentActor(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private string CurrentActor() => User.Identity?.Name ?? "Usuário";

    private static void ClearSuppression(OperationalAlertDTO alert)
    {
        alert.SuppressedUntil = null;
        alert.SuppressedById = null;
        alert.SuppressedBy = string.Empty;
        alert.SuppressionReason = string.Empty;
    }

    private static IQueryable<OperationalAlertDTO> ApplyStatus(
        IQueryable<OperationalAlertDTO> query,
        string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "open" => query.Where(x => x.Status == OperationalAlertStatus.Open),
            "acknowledged" => query.Where(x => x.Status == OperationalAlertStatus.Acknowledged),
            "resolved" => query.Where(x => x.Status == OperationalAlertStatus.Resolved),
            "all" => query,
            _ => query.Where(x => x.Status != OperationalAlertStatus.Resolved)
        };

    internal static OperationalAlertOut ToOut(OperationalAlertDTO alert) => new()
    {
        Id = alert.Id,
        Type = alert.Type,
        Severity = alert.Severity.ToString(),
        Status = alert.Status.ToString(),
        LicenseId = alert.LicenseId,
        LicenseName = alert.License?.Name ?? string.Empty,
        Title = alert.Title,
        Message = alert.Message,
        IsConditionActive = alert.IsConditionActive,
        OccurrenceCount = alert.OccurrenceCount,
        FirstOccurredAt = alert.FirstOccurredAt,
        OccurredAt = alert.LastOccurredAt,
        AcknowledgedAt = alert.AcknowledgedAt,
        AcknowledgedBy = alert.AcknowledgedBy,
        AcknowledgementNote = alert.AcknowledgementNote,
        ResolvedAt = alert.ResolvedAt,
        ResolvedBy = alert.ResolvedBy,
        ResolutionNote = alert.ResolutionNote,
        TargetUrl = alert.TargetUrl,
        SuppressedUntil = alert.SuppressedUntil,
        SuppressedBy = alert.SuppressedBy,
        SuppressionReason = alert.SuppressionReason
    };

    private static string StatusLabel(OperationalAlertStatus status) => status switch
    {
        OperationalAlertStatus.Acknowledged => "alerta reconhecido",
        OperationalAlertStatus.Resolved => "alerta resolvido",
        _ => "alerta reaberto"
    };
}
