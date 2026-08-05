using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/emergency")]
public sealed class EmergencyController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IIncidentService incidents) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ViewEmergency, HttpContext.RequestAborted)) return Forbid();
        var sessions = await context.EmergencySessions.AsNoTracking().Include(x => x.License)
            .Where(x => x.LicenseId == licenseId).OrderByDescending(x => x.ActivatedAt).Take(50)
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(sessions.Select(ToOut));
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(Guid licenseId, [FromBody] EmergencyActivationViewModel input)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageEmergency, HttpContext.RequestAborted)) return Forbid();
        var license = await context.Licenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == licenseId, HttpContext.RequestAborted);
        if (license is null) return NotFound();
        if (!Enum.IsDefined(typeof(EmergencyTypeEnum), input.Type)) return BadRequest(new { Errors = "Tipo de emergência inválido." });
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Instructions)) return BadRequest(new { Errors = "Informe o título e as orientações do protocolo." });
        if (!string.Equals(input.ConfirmationText.Trim(), $"ATIVAR {license.Code}", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Errors = $"Digite ATIVAR {license.Code} para confirmar." });
        if (await context.EmergencySessions.AnyAsync(x => x.LicenseId == licenseId && x.Status == EmergencyStatusEnum.Active, HttpContext.RequestAborted))
            return Conflict(new { Errors = "Já existe um modo de emergência ativo neste condomínio." });

        await using var transaction = await context.Database.BeginTransactionAsync(HttpContext.RequestAborted);
        var now = DateTime.UtcNow;
        var type = (EmergencyTypeEnum)input.Type;
        var incident = await incidents.CreateAsync(new IncidentCreationRequest(
            licenseId, input.Title, input.Instructions, IncidentCategoryEnum.Safety,
            IncidentSeverityEnum.Critical, IncidentSourceEnum.Emergency, CurrentActor(), CurrentUserId(),
            "Emergency", null, null, IncidentTimelineTypeEnum.Emergency,
            JsonSerializer.Serialize(new { Type = type.ToString() })), HttpContext.RequestAborted);
        var session = new EmergencySessionDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, Type = type, Status = EmergencyStatusEnum.Active,
            Title = input.Title.Trim(), Instructions = input.Instructions.Trim(), ActivatedByUserId = CurrentUserId(),
            ActivatedByName = CurrentActor(), ActivatedAt = now, IncidentId = incident.Id, CreatedAt = now, UpdatedAt = now
        };
        context.EmergencySessions.Add(session);
        context.OperationalAlerts.Add(new OperationalAlertDTO
        {
            Id = Guid.NewGuid(), EnterpriseId = license.EnterpriseId, LicenseId = licenseId,
            Fingerprint = $"emergency:{session.Id:N}", Type = "EmergencyMode", Source = "Emergency",
            Severity = OperationalAlertSeverity.Critical, Status = OperationalAlertStatus.Open,
            Title = Short(input.Title.Trim(), 180), Message = Short(input.Instructions.Trim(), 1000),
            TargetUrl = $"/licencas/{licenseId}/emergencia", ResourceType = "EmergencySession",
            ResourceId = session.Id, IsConditionActive = true, OccurrenceCount = 1,
            FirstOccurredAt = now, LastOccurredAt = now, CreatedAt = now, UpdatedAt = now
        });
        AddAudit(session, "Activated", "Modo de emergência ativado.", new { Type = type.ToString(), incident.Id });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        await transaction.CommitAsync(HttpContext.RequestAborted);
        session.License = license;
        return Ok(ToOut(session));
    }

    [HttpPost("{sessionId:guid}/resolve")]
    public async Task<IActionResult> Resolve(Guid licenseId, Guid sessionId, [FromBody] EmergencyResolveViewModel input)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageEmergency, HttpContext.RequestAborted)) return Forbid();
        var license = await context.Licenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == licenseId, HttpContext.RequestAborted);
        if (license is null) return NotFound();
        if (!string.Equals(input.ConfirmationText.Trim(), $"ENCERRAR {license.Code}", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Errors = $"Digite ENCERRAR {license.Code} para confirmar." });
        if (string.IsNullOrWhiteSpace(input.Resolution)) return BadRequest(new { Errors = "Informe como a emergência foi encerrada." });
        var session = await context.EmergencySessions.Include(x => x.Incident)
            .FirstOrDefaultAsync(x => x.Id == sessionId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (session is null) return NotFound();
        if (session.Status != EmergencyStatusEnum.Active) return Conflict(new { Errors = "Esta emergência já foi encerrada." });

        var now = DateTime.UtcNow;
        session.Status = EmergencyStatusEnum.Resolved; session.ResolvedByUserId = CurrentUserId();
        session.ResolvedByName = CurrentActor(); session.ResolvedAt = now;
        session.Resolution = Short(input.Resolution.Trim(), 2000); session.UpdatedAt = now;
        session.Incident.Status = IncidentStatusEnum.Resolved; session.Incident.ResolvedAt = now;
        session.Incident.Resolution = session.Resolution; session.Incident.UpdatedAt = now;
        context.IncidentTimelineEntries.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(), IncidentId = session.IncidentId, Type = IncidentTimelineTypeEnum.Emergency,
            Message = session.Resolution, ActorUserId = CurrentUserId(), ActorName = CurrentActor(), CreatedAt = now
        });
        var alert = await context.OperationalAlerts.FirstOrDefaultAsync(
            x => x.LicenseId == licenseId && x.ResourceType == "EmergencySession" && x.ResourceId == sessionId,
            HttpContext.RequestAborted);
        if (alert is not null)
        {
            alert.Status = OperationalAlertStatus.Resolved; alert.IsConditionActive = false;
            alert.ResolvedAt = now; alert.ResolvedById = CurrentUserId(); alert.ResolvedBy = CurrentActor();
            alert.ResolutionNote = Short(input.Resolution.Trim(), 500); alert.UpdatedAt = now;
        }
        AddAudit(session, "Resolved", "Modo de emergência encerrado.", new { input.Resolution });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        session.License = license;
        return Ok(ToOut(session));
    }

    private void AddAudit(EmergencySessionDTO session, string action, string summary, object details) => context.AccessOperationAudits.Add(new AccessOperationAuditDTO
    {
        Id = Guid.NewGuid(), LicenseId = session.LicenseId, EntityType = "EmergencySession", EntityId = session.Id,
        Action = action, Status = "Success", Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
        UserId = CurrentUserId(), UserName = CurrentActor(), CreatedAt = DateTime.UtcNow
    });

    private static EmergencySessionViewModel ToOut(EmergencySessionDTO x) => new()
    {
        Id = x.Id, LicenseId = x.LicenseId, LicenseName = x.License?.Name ?? string.Empty,
        Type = x.Type.ToString(), Status = x.Status.ToString(), Title = x.Title, Instructions = x.Instructions,
        ActivatedByName = x.ActivatedByName, ActivatedAt = x.ActivatedAt, ResolvedByName = x.ResolvedByName,
        ResolvedAt = x.ResolvedAt, Resolution = x.Resolution, IncidentId = x.IncidentId
    };
    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentActor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuário";
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
