using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public sealed record SlaWindow(DateTime ResponseDueAt, DateTime ResolutionDueAt);

public interface IMaintenanceService
{
    Task<SlaWindow> CalculateSlaAsync(Guid licenseId, IncidentSeverityEnum severity, DateTime createdAt, CancellationToken cancellationToken = default);
    Task<WorkOrderDTO> CreateWorkOrderAsync(Guid licenseId, WorkOrderCreateViewModel input, Guid? actorUserId, string actorName, CancellationToken cancellationToken = default);
    Task<int> GenerateDuePreventiveOrdersAsync(DateTime now, CancellationToken cancellationToken = default);
}

public sealed class MaintenanceService(DatabaseContext context) : IMaintenanceService
{
    public async Task<SlaWindow> CalculateSlaAsync(Guid licenseId, IncidentSeverityEnum severity, DateTime createdAt, CancellationToken cancellationToken = default)
    {
        var policy = await context.MaintenancePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        var (response, resolution) = ResolveSla(policy, severity);
        return new SlaWindow(createdAt.AddMinutes(response), createdAt.AddMinutes(resolution));
    }

    public async Task<WorkOrderDTO> CreateWorkOrderAsync(Guid licenseId, WorkOrderCreateViewModel input, Guid? actorUserId, string actorName, CancellationToken cancellationToken = default)
    {
        if (input.IncidentId.HasValue && !await context.Incidents.AnyAsync(x => x.Id == input.IncidentId && x.LicenseId == licenseId, cancellationToken))
            throw new InvalidOperationException("A ocorrência informada não pertence a este condomínio.");
        if (input.ProviderId.HasValue && !await context.MaintenanceProviders.AnyAsync(x => x.Id == input.ProviderId && x.LicenseId == licenseId && x.IsActive, cancellationToken))
            throw new InvalidOperationException("O prestador informado não está disponível neste condomínio.");

        var now = DateTime.UtcNow;
        var order = new WorkOrderDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, IncidentId = input.IncidentId,
            Code = GenerateCode("OS", now), Title = Short(input.Title.Trim(), 180),
            Description = Short(input.Description.Trim(), 4000), Status = WorkOrderStatusEnum.Planned,
            Priority = (IncidentSeverityEnum)input.Priority, LocationLabel = Short(input.LocationLabel.Trim(), 240),
            AssignedToName = Short(input.AssignedToName.Trim(), 150), ProviderId = input.ProviderId,
            // DueAt comes from a date-only picker. Keep the selected calendar day
            // instead of treating midnight as a UTC instant (which displays as the
            // previous day in Bahia).
            DueAt = input.DueAt?.AsCalendarDate(), EstimatedCost = input.EstimatedCost,
            CreatedByUserId = actorUserId, CreatedByName = Short(actorName, 150), CreatedAt = now, UpdatedAt = now
        };
        var index = 0;
        foreach (var item in input.Checklist.Where(x => !string.IsNullOrWhiteSpace(x.Title)).Take(50))
            order.Checklist.Add(new WorkOrderChecklistItemDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, WorkOrderId = order.Id,
                Title = Short(item.Title.Trim(), 300), SortOrder = index++, IsRequired = item.IsRequired
            });
        order.Activities.Add(Activity(order, WorkOrderActivityTypeEnum.Created, "Ordem de serviço criada.", actorUserId, actorName, true, now));
        context.WorkOrders.Add(order);
        context.AccessOperationAudits.Add(Audit(licenseId, order.Id, "Created", $"{order.Code}: {order.Title}", actorUserId, actorName, new { order.IncidentId, order.Priority, order.DueAt }));
        await context.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<int> GenerateDuePreventiveOrdersAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var plans = await context.PreventiveMaintenancePlans
            .Include(x => x.DefaultProvider)
            .Where(x => x.IsActive &&
                        (x.License.EnabledModules & CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents) != 0 &&
                        x.NextDueAt <= now.AddDays(x.LeadDays))
            .OrderBy(x => x.NextDueAt).Take(200).ToListAsync(cancellationToken);
        var generated = 0;
        foreach (var plan in plans)
        {
            var scheduledFor = plan.NextDueAt.Date;
            var exists = await context.WorkOrders.AnyAsync(x => x.PreventivePlanId == plan.Id && x.ScheduledFor == scheduledFor, cancellationToken);
            if (!exists)
            {
                var templates = ParseChecklist(plan.ChecklistTemplateJson);
                var order = new WorkOrderDTO
                {
                    Id = Guid.NewGuid(), LicenseId = plan.LicenseId, PreventivePlanId = plan.Id, ScheduledFor = scheduledFor,
                    Code = GenerateCode("OS", now), Title = plan.Name, Description = plan.Description,
                    Status = WorkOrderStatusEnum.Planned, Priority = IncidentSeverityEnum.Medium,
                    LocationLabel = plan.LocationLabel, AssignedToName = plan.DefaultAssignedToName,
                    ProviderId = plan.DefaultProviderId, DueAt = plan.NextDueAt, EstimatedCost = plan.EstimatedCost,
                    CreatedByName = "Manutenção preventiva", CreatedAt = now, UpdatedAt = now
                };
                for (var i = 0; i < templates.Count; i++) order.Checklist.Add(new WorkOrderChecklistItemDTO
                {
                    Id = Guid.NewGuid(), LicenseId = plan.LicenseId, WorkOrderId = order.Id,
                    Title = templates[i].Title, IsRequired = templates[i].IsRequired, SortOrder = i
                });
                order.Activities.Add(Activity(order, WorkOrderActivityTypeEnum.Created, $"OS gerada pelo plano preventivo {plan.Name}.", null, "Sistema", true, now));
                context.WorkOrders.Add(order);
                generated++;
            }
            plan.LastGeneratedFor = scheduledFor;
            plan.NextDueAt = Advance(plan.NextDueAt, plan.IntervalDays, now);
            plan.UpdatedAt = now;
        }
        if (plans.Count > 0) await context.SaveChangesAsync(cancellationToken);
        return generated;
    }

    internal static (int ResponseMinutes, int ResolutionMinutes) ResolveSla(MaintenancePolicyDTO? policy, IncidentSeverityEnum severity) => severity switch
    {
        IncidentSeverityEnum.Critical => (policy?.CriticalResponseMinutes ?? 30, policy?.CriticalResolutionMinutes ?? 240),
        IncidentSeverityEnum.High => (policy?.HighResponseMinutes ?? 120, policy?.HighResolutionMinutes ?? 1440),
        IncidentSeverityEnum.Medium => (policy?.MediumResponseMinutes ?? 480, policy?.MediumResolutionMinutes ?? 4320),
        _ => (policy?.LowResponseMinutes ?? 1440, policy?.LowResolutionMinutes ?? 10080)
    };

    internal static DateTime Advance(DateTime dueAt, int intervalDays, DateTime now)
    {
        var next = dueAt;
        do next = next.AddDays(Math.Max(1, intervalDays)); while (next <= now);
        return next;
    }

    internal static string GenerateCode(string prefix, DateTime now) => $"{prefix}-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..24].ToUpperInvariant();
    internal static WorkOrderActivityDTO Activity(WorkOrderDTO order, WorkOrderActivityTypeEnum type, string message, Guid? actorUserId, string actorName, bool visible, DateTime now) => new()
    {
        Id = Guid.NewGuid(), LicenseId = order.LicenseId, WorkOrderId = order.Id, Type = type,
        Message = Short(message, 2000), ActorUserId = actorUserId, ActorName = Short(actorName, 150), VisibleToResident = visible, CreatedAt = now
    };
    internal static AccessOperationAuditDTO Audit(Guid licenseId, Guid entityId, string action, string summary, Guid? actorUserId, string actorName, object details) => new()
    {
        Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "WorkOrder", EntityId = entityId,
        Action = action, Status = "Success", Summary = Short(summary, 1000), DetailsJson = JsonSerializer.Serialize(details),
        UserId = actorUserId, UserName = Short(actorName, 150), CreatedAt = DateTime.UtcNow
    };
    internal static string ChecklistJson(IEnumerable<WorkOrderChecklistInputViewModel> input) => JsonSerializer.Serialize(
        input.Where(x => !string.IsNullOrWhiteSpace(x.Title)).Take(50).Select(x => new ChecklistTemplate(Short(x.Title.Trim(), 300), x.IsRequired)));
    internal static List<ChecklistTemplate> ParseChecklist(string json)
    {
        try { return JsonSerializer.Deserialize<List<ChecklistTemplate>>(json) ?? []; }
        catch (JsonException) { return []; }
    }
    internal sealed record ChecklistTemplate(string Title, bool IsRequired);
    internal static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
