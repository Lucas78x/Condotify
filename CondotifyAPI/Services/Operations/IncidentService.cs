using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public sealed record IncidentCreationRequest(
    Guid LicenseId,
    string Title,
    string Description,
    IncidentCategoryEnum Category,
    IncidentSeverityEnum Severity,
    IncidentSourceEnum Source,
    string ActorName,
    Guid? ActorUserId = null,
    string RelatedResourceType = "",
    Guid? RelatedResourceId = null,
    DateTime? DueAt = null,
    IncidentTimelineTypeEnum TimelineType = IncidentTimelineTypeEnum.Created,
    string MetadataJson = "{}");

public interface IIncidentService
{
    Task<IncidentDTO> CreateAsync(IncidentCreationRequest request, CancellationToken cancellationToken = default);
}

public sealed class IncidentService(DatabaseContext context) : IIncidentService
{
    public async Task<IncidentDTO> CreateAsync(
        IncidentCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var codeSuffix = Guid.NewGuid().ToString("N")[..12];
        var incident = new IncidentDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = request.LicenseId,
            Code = $"INC-{now:yyyyMMdd}-{codeSuffix}".ToUpperInvariant(),
            Title = Short(request.Title.Trim(), 180),
            Description = Short(request.Description.Trim(), 4000),
            Category = request.Category,
            Severity = request.Severity,
            Status = IncidentStatusEnum.Open,
            Source = request.Source,
            RelatedResourceType = Short(request.RelatedResourceType.Trim(), 80),
            RelatedResourceId = request.RelatedResourceId,
            ReportedByUserId = request.ActorUserId,
            ReportedByName = Short(request.ActorName.Trim(), 150),
            DueAt = request.DueAt?.ToUniversalTime(),
            CreatedAt = now,
            UpdatedAt = now
        };
        incident.Timeline.Add(new IncidentTimelineEntryDTO
        {
            Id = Guid.NewGuid(),
            IncidentId = incident.Id,
            Type = request.TimelineType,
            Message = Short(request.Description.Trim().Length == 0 ? request.Title.Trim() : request.Description.Trim(), 2000),
            ReferenceType = incident.RelatedResourceType,
            ReferenceId = incident.RelatedResourceId,
            MetadataJson = ValidJson(request.MetadataJson),
            ActorUserId = request.ActorUserId,
            ActorName = incident.ReportedByName,
            CreatedAt = now
        });
        context.Incidents.Add(incident);
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = request.LicenseId,
            EntityType = "Incident",
            EntityId = incident.Id,
            Action = "Created",
            Status = "Success",
            Summary = $"{incident.Code}: {incident.Title}",
            DetailsJson = JsonSerializer.Serialize(new { incident.Category, incident.Severity, incident.Source }),
            UserId = request.ActorUserId,
            UserName = incident.ReportedByName,
            CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        return incident;
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];

    private static string ValidJson(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "{}";
        try { using var _ = JsonDocument.Parse(value); return value; }
        catch (JsonException) { return "{}"; }
    }
}
