using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
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
[Route("api/access/operations/incidents")]
public sealed class IncidentsController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IIncidentService incidents) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? licenseId = null,
        [FromQuery] string status = "active",
        [FromQuery] string severity = "",
        [FromQuery] string search = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var visible = await authorization.GetLicenseIdsWithPermissionAsync(
            User, LicensePermissionEnum.ViewIncidents, HttpContext.RequestAborted);
        var manageable = await authorization.GetLicenseIdsWithPermissionAsync(
            User, LicensePermissionEnum.ManageIncidents, HttpContext.RequestAborted);
        if (licenseId.HasValue) visible.IntersectWith([licenseId.Value]);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = context.Incidents.AsNoTracking()
            .Include(x => x.License)
            .Include(x => x.Timeline.OrderByDescending(entry => entry.CreatedAt))
            .Where(x => visible.Contains(x.LicenseId));
        query = status.Trim().ToLowerInvariant() switch
        {
            "open" => query.Where(x => x.Status == IncidentStatusEnum.Open),
            "inprogress" => query.Where(x => x.Status == IncidentStatusEnum.InProgress),
            "resolved" => query.Where(x => x.Status == IncidentStatusEnum.Resolved),
            "closed" => query.Where(x => x.Status == IncidentStatusEnum.Closed),
            "all" => query,
            _ => query.Where(x => x.Status == IncidentStatusEnum.Open || x.Status == IncidentStatusEnum.InProgress)
        };
        if (Enum.TryParse<IncidentSeverityEnum>(severity, true, out var parsedSeverity))
            query = query.Where(x => x.Severity == parsedSeverity);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.Code.ToLower().Contains(term) || x.Title.ToLower().Contains(term) ||
                                     x.Description.ToLower().Contains(term) || x.AssignedToName.ToLower().Contains(term));
        }

        var total = await query.CountAsync(HttpContext.RequestAborted);
        var rows = await query.OrderBy(x => x.Status == IncidentStatusEnum.Closed)
            .ThenBy(x => x.Status == IncidentStatusEnum.Resolved)
            .ThenByDescending(x => x.Severity)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(HttpContext.RequestAborted);
        var allVisible = context.Incidents.AsNoTracking().Where(x => visible.Contains(x.LicenseId));
        return Ok(new IncidentPageViewModel
        {
            Total = total,
            Open = await allVisible.CountAsync(x => x.Status == IncidentStatusEnum.Open || x.Status == IncidentStatusEnum.InProgress, HttpContext.RequestAborted),
            Critical = await allVisible.CountAsync(x => (x.Status == IncidentStatusEnum.Open || x.Status == IncidentStatusEnum.InProgress) && x.Severity == IncidentSeverityEnum.Critical, HttpContext.RequestAborted),
            Items = rows.Select(x => ToOut(x, manageable.Contains(x.LicenseId))).ToList()
        });
    }

    [HttpGet("{incidentId:guid}")]
    public async Task<IActionResult> GetById(Guid incidentId)
    {
        var incident = await context.Incidents.AsNoTracking().Include(x => x.License)
            .Include(x => x.Timeline.OrderByDescending(entry => entry.CreatedAt))
            .FirstOrDefaultAsync(x => x.Id == incidentId, HttpContext.RequestAborted);
        if (incident is null) return NotFound();
        if (!await authorization.HasPermissionAsync(User, incident.LicenseId, LicensePermissionEnum.ViewIncidents, HttpContext.RequestAborted))
            return Forbid();
        var canManage = await authorization.HasPermissionAsync(User, incident.LicenseId, LicensePermissionEnum.ManageIncidents, HttpContext.RequestAborted);
        return Ok(ToOut(incident, canManage));
    }

    [HttpPost("{licenseId:guid}")]
    public async Task<IActionResult> Create(Guid licenseId, [FromBody] IncidentCreateViewModel input)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageIncidents, HttpContext.RequestAborted))
            return Forbid();
        if (string.IsNullOrWhiteSpace(input.Title)) return BadRequest(new { Errors = "Informe o título da ocorrência." });
        if (!Enum.IsDefined(typeof(IncidentCategoryEnum), input.Category) || !Enum.IsDefined(typeof(IncidentSeverityEnum), input.Severity))
            return BadRequest(new { Errors = "Categoria ou gravidade inválida." });
        var incident = await incidents.CreateAsync(new IncidentCreationRequest(
            licenseId, input.Title, input.Description, (IncidentCategoryEnum)input.Category,
            (IncidentSeverityEnum)input.Severity, IncidentSourceEnum.Manual,
            CurrentActor(), CurrentUserId(), input.RelatedResourceType, input.RelatedResourceId, input.DueAt),
            HttpContext.RequestAborted);
        incident.License = await context.Licenses.AsNoTracking().FirstAsync(x => x.Id == licenseId, HttpContext.RequestAborted);
        return CreatedAtAction(nameof(GetById), new { incidentId = incident.Id }, ToOut(incident, true));
    }

    [HttpPost("{incidentId:guid}/comments")]
    public async Task<IActionResult> Comment(Guid incidentId, [FromBody] IncidentCommentViewModel input)
    {
        if (string.IsNullOrWhiteSpace(input.Message)) return BadRequest(new { Errors = "Escreva um comentário ou descrição da evidência." });
        var incident = await FindManageableAsync(incidentId);
        if (incident.Result is not null) return incident.Result;
        var now = DateTime.UtcNow;
        context.IncidentTimelineEntries.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(), IncidentId = incident.Value!.Id,
            Type = string.IsNullOrWhiteSpace(input.ReferenceUrl) && string.IsNullOrWhiteSpace(input.ReferenceType)
                ? IncidentTimelineTypeEnum.Comment : IncidentTimelineTypeEnum.Evidence,
            Message = Short(input.Message.Trim(), 2000), ReferenceType = Short(input.ReferenceType.Trim(), 80),
            ReferenceId = input.ReferenceId, ReferenceUrl = Short(input.ReferenceUrl.Trim(), 1000),
            ActorUserId = CurrentUserId(), ActorName = CurrentActor(), CreatedAt = now
        });
        incident.Value.UpdatedAt = now;
        AddAudit(incident.Value, "Commented", "Comentário ou evidência adicionada.", new { input.ReferenceType, input.ReferenceId });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return await GetById(incidentId);
    }

    [HttpPatch("{incidentId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(Guid incidentId, [FromBody] IncidentStatusUpdateViewModel input)
    {
        if (!Enum.IsDefined(typeof(IncidentStatusEnum), input.Status)) return BadRequest(new { Errors = "Status inválido." });
        var target = (IncidentStatusEnum)input.Status;
        if (target is IncidentStatusEnum.Resolved or IncidentStatusEnum.Closed && string.IsNullOrWhiteSpace(input.Note))
            return BadRequest(new { Errors = "Informe a conclusão da ocorrência." });
        var incident = await FindManageableAsync(incidentId);
        if (incident.Result is not null) return incident.Result;
        var now = DateTime.UtcNow;
        incident.Value!.Status = target;
        incident.Value.UpdatedAt = now;
        if (target == IncidentStatusEnum.InProgress && !incident.Value.AcknowledgedAt.HasValue) incident.Value.AcknowledgedAt = now;
        if (target is IncidentStatusEnum.Resolved or IncidentStatusEnum.Closed)
        {
            incident.Value.ResolvedAt = now;
            incident.Value.Resolution = Short(input.Note.Trim(), 2000);
        }
        else
        {
            incident.Value.ResolvedAt = null;
            incident.Value.Resolution = string.Empty;
        }
        context.IncidentTimelineEntries.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(), IncidentId = incidentId, Type = IncidentTimelineTypeEnum.StatusChanged,
            Message = string.IsNullOrWhiteSpace(input.Note) ? $"Status alterado para {target}." : Short(input.Note.Trim(), 2000),
            ActorUserId = CurrentUserId(), ActorName = CurrentActor(), CreatedAt = now
        });
        AddAudit(incident.Value, target.ToString(), $"Status alterado para {target}.", new { input.Note });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return await GetById(incidentId);
    }

    [HttpPatch("{incidentId:guid}/assignment")]
    public async Task<IActionResult> Assign(Guid incidentId, [FromBody] IncidentAssignmentViewModel input)
    {
        if (input.UserId.HasValue && string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Informe o nome do responsável." });
        var incident = await FindManageableAsync(incidentId);
        if (incident.Result is not null) return incident.Result;
        incident.Value!.AssignedToUserId = input.UserId;
        incident.Value.AssignedToName = Short(input.Name.Trim(), 150);
        incident.Value.UpdatedAt = DateTime.UtcNow;
        context.IncidentTimelineEntries.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(), IncidentId = incidentId, Type = IncidentTimelineTypeEnum.Assignment,
            Message = string.IsNullOrWhiteSpace(input.Name) ? "Responsável removido." : $"Ocorrência atribuída a {input.Name.Trim()}.",
            ActorUserId = CurrentUserId(), ActorName = CurrentActor(), CreatedAt = DateTime.UtcNow
        });
        AddAudit(incident.Value, "Assigned", "Responsável da ocorrência atualizado.", new { input.UserId, input.Name });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return await GetById(incidentId);
    }

    private async Task<(IncidentDTO? Value, IActionResult? Result)> FindManageableAsync(Guid incidentId)
    {
        var incident = await context.Incidents.FirstOrDefaultAsync(x => x.Id == incidentId, HttpContext.RequestAborted);
        if (incident is null) return (null, NotFound());
        return await authorization.HasPermissionAsync(User, incident.LicenseId, LicensePermissionEnum.ManageIncidents, HttpContext.RequestAborted)
            ? (incident, null) : (null, Forbid());
    }

    private void AddAudit(IncidentDTO incident, string action, string summary, object details) => context.AccessOperationAudits.Add(new AccessOperationAuditDTO
    {
        Id = Guid.NewGuid(), LicenseId = incident.LicenseId, EntityType = "Incident", EntityId = incident.Id,
        Action = action, Status = "Success", Summary = summary, DetailsJson = JsonSerializer.Serialize(details),
        UserId = CurrentUserId(), UserName = CurrentActor(), CreatedAt = DateTime.UtcNow
    });

    internal static IncidentViewModel ToOut(IncidentDTO x, bool canManage) => new()
    {
        Id = x.Id, LicenseId = x.LicenseId, LicenseName = x.License?.Name ?? string.Empty, Code = x.Code,
        Title = x.Title, Description = x.Description, Category = x.Category.ToString(), Severity = x.Severity.ToString(),
        Status = x.Status.ToString(), Source = x.Source.ToString(), RelatedResourceType = x.RelatedResourceType,
        RelatedResourceId = x.RelatedResourceId, AssignedToName = x.AssignedToName, ReportedByName = x.ReportedByName,
        DueAt = x.DueAt, ResolvedAt = x.ResolvedAt, Resolution = x.Resolution, CreatedAt = x.CreatedAt,
        UpdatedAt = x.UpdatedAt, CanManage = canManage,
        Timeline = x.Timeline.OrderByDescending(entry => entry.CreatedAt).Select(entry => new IncidentTimelineEntryViewModel
        {
            Id = entry.Id, Type = entry.Type.ToString(), Message = entry.Message, ReferenceType = entry.ReferenceType,
            ReferenceId = entry.ReferenceId, ReferenceUrl = entry.ReferenceUrl, ActorName = entry.ActorName, CreatedAt = entry.CreatedAt
        }).ToList()
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentActor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuário";
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
