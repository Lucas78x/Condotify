using System.Security.Claims;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using CondotifyAPI.Services.Operations;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

using ApiNotificationCategory = CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/incidents")]
public sealed class ResidentIncidentsController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    ILicenseModuleService modules,
    IIncidentService incidents,
    IPrivateMediaStore media,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!await Enabled(grant.LicenseId, cancellationToken)) return ModuleDisabled();
        var rows = await Query(grant.ResidentId, grant.LicenseId).AsNoTracking()
            .OrderBy(x => x.Status == IncidentStatusEnum.Open ? 0 : x.Status == IncidentStatusEnum.InProgress ? 1 : 2)
            .ThenByDescending(x => x.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return Ok(new ResidentIncidentOverviewViewModel
        {
            Open = rows.Count(x => x.Status == IncidentStatusEnum.Open),
            InProgress = rows.Count(x => x.Status == IncidentStatusEnum.InProgress),
            Resolved = rows.Count(x => x.Status is IncidentStatusEnum.Resolved or IncidentStatusEnum.Closed),
            Items = rows.Select(ToResident).ToList()
        });
    }

    [HttpGet("{incidentId:guid}")]
    public async Task<IActionResult> Get(Guid incidentId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!await Enabled(grant.LicenseId, cancellationToken)) return ModuleDisabled();
        var row = await Query(grant.ResidentId, grant.LicenseId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == incidentId, cancellationToken);
        return row is null ? NotFound() : Ok(ToResident(row));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ResidentIncidentCreateViewModel input, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!await Enabled(grant.LicenseId, cancellationToken)) return ModuleDisabled();
        if (string.IsNullOrWhiteSpace(input.Title) || string.IsNullOrWhiteSpace(input.Description) || string.IsNullOrWhiteSpace(input.LocationLabel))
            return BadRequest(new { Errors = "Informe título, descrição e local da ocorrência." });
        if (!Enum.IsDefined(typeof(IncidentCategoryEnum), input.Category)) return BadRequest(new { Errors = "Categoria inválida." });
        if (input.Photos.Count > 4) return BadRequest(new { Errors = "Envie no máximo quatro fotos." });
        var severity = (IncidentSeverityEnum)Math.Clamp(input.Severity, 0, 2);
        var actor = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Morador";
        var incident = await incidents.CreateAsync(new IncidentCreationRequest(
            grant.LicenseId, input.Title, input.Description, (IncidentCategoryEnum)input.Category, severity,
            IncidentSourceEnum.Manual, actor, ActorResidentId: grant.ResidentId, LocationLabel: input.LocationLabel), cancellationToken);
        try
        {
            foreach (var photo in input.Photos)
            {
                var reference = await media.StoreDataUriAsync(grant.LicenseId, photo.DataUri, cancellationToken);
                incident.Attachments.Add(new IncidentAttachmentDTO
                {
                    Id = Guid.NewGuid(), LicenseId = grant.LicenseId, IncidentId = incident.Id, MediaReference = reference,
                    FileName = Short(photo.FileName.Trim(), 260), ContentType = ContentType(photo.DataUri), Caption = Short(photo.Caption.Trim(), 500),
                    VisibleToResident = true, UploadedByResidentId = grant.ResidentId, UploadedByName = actor, CreatedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) { return BadRequest(new { Errors = exception.Message }); }

        await notifier.NotifyLicenseUsersAsync(grant.LicenseId, ApiNotificationCategory.Operational,
            "Nova ocorrência de morador", $"{actor} abriu: {incident.Title}", "/ocorrencias",
            $"resident-incident:{incident.Id:N}", cancellationToken);
        return CreatedAtAction(nameof(Get), new { incidentId = incident.Id }, ToResident(incident));
    }

    [HttpPost("{incidentId:guid}/comments")]
    public async Task<IActionResult> Comment(Guid incidentId, IncidentCommentViewModel input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Message)) return BadRequest(new { Errors = "Escreva uma mensagem." });
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!await Enabled(grant.LicenseId, cancellationToken)) return ModuleDisabled();
        var incident = await context.Incidents.FirstOrDefaultAsync(x => x.Id == incidentId && x.LicenseId == grant.LicenseId && x.ReportedByResidentId == grant.ResidentId, cancellationToken);
        if (incident is null) return NotFound();
        var now = DateTime.UtcNow;
        context.IncidentTimelineEntries.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(), IncidentId = incident.Id, Type = IncidentTimelineTypeEnum.Comment,
            Message = Short(input.Message.Trim(), 2000), ActorName = User.FindFirstValue("name") ?? "Morador",
            VisibleToResident = true, CreatedAt = now
        });
        incident.UpdatedAt = now; await context.SaveChangesAsync(cancellationToken);
        await notifier.NotifyLicenseUsersAsync(grant.LicenseId, ApiNotificationCategory.Operational,
            "Atualização em ocorrência", $"O morador atualizou {incident.Code}.", "/ocorrencias",
            $"resident-incident-comment:{incident.Id:N}:{now.Ticks}", cancellationToken);
        return await Get(incidentId, cancellationToken);
    }

    [HttpGet("media/{mediaId:guid}")]
    public async Task<IActionResult> Media(Guid mediaId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!await Enabled(grant.LicenseId, cancellationToken)) return ModuleDisabled();
        var reference = PrivateMediaStore.Reference(grant.LicenseId, mediaId);
        var linked = await context.IncidentAttachments.AsNoTracking().AnyAsync(x => x.LicenseId == grant.LicenseId && x.MediaReference == reference && x.VisibleToResident && x.Incident!.ReportedByResidentId == grant.ResidentId, cancellationToken);
        if (!linked) return NotFound();
        var file = await media.ReadAsync(grant.LicenseId, mediaId, cancellationToken);
        return file is null ? NotFound() : File(file.Content, file.ContentType);
    }

    private IQueryable<IncidentDTO> Query(Guid residentId, Guid licenseId) => context.Incidents
        .Include(x => x.Timeline).Include(x => x.Attachments)
        .Include(x => x.WorkOrders).ThenInclude(x => x.Checklist)
        .Include(x => x.WorkOrders).ThenInclude(x => x.Activities)
        .Include(x => x.WorkOrders).ThenInclude(x => x.Attachments)
        .Where(x => x.LicenseId == licenseId && x.ReportedByResidentId == residentId);

    internal static IncidentViewModel ToResident(IncidentDTO row)
    {
        var result = IncidentsController.ToOut(row, false);
        result.ReportedByName = string.Empty;
        result.Timeline = row.Timeline.Where(x => x.VisibleToResident).OrderByDescending(x => x.CreatedAt).Select(x => new IncidentTimelineEntryViewModel
        {
            Id = x.Id, Type = x.Type.ToString(), Message = x.Message, ReferenceType = x.ReferenceType,
            ReferenceId = x.ReferenceId, ReferenceUrl = x.ReferenceUrl, VisibleToResident = true, CreatedAt = x.CreatedAt
        }).ToList();
        result.Attachments = row.Attachments.Where(x => x.VisibleToResident).Select(MaintenanceController.ToAttachment).ToList();
        result.WorkOrders = row.WorkOrders.Select(x => MaintenanceController.ToWorkOrder(x, true)).ToList();
        return result;
    }

    private Task<bool> Enabled(Guid licenseId, CancellationToken token) => modules.IsEnabledAsync(licenseId, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents, token);
    private NotFoundObjectResult ModuleDisabled() => NotFound(new { Code = "ModuleDisabled", Errors = "O módulo de ocorrências e manutenção está desativado neste condomínio." });
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
    private static string ContentType(string value) => value.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase) ? "image/png" : value.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";
}
