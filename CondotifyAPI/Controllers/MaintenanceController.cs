using System.Security.Claims;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using CondotifyAPI.Services.Operations;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/maintenance")]
public sealed class MaintenanceController(
    DatabaseContext context,
    ILicenseModuleService modules,
    IMaintenanceService maintenance,
    IPrivateMediaStore media,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewIncidents)]
    public async Task<IActionResult> Dashboard(Guid licenseId, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var now = DateTime.UtcNow;
        var incidents = await context.Incidents.AsNoTracking()
            .Include(x => x.Timeline.OrderByDescending(a => a.CreatedAt))
            .Include(x => x.Attachments)
            .Include(x => x.WorkOrders).ThenInclude(x => x.Provider)
            .Include(x => x.WorkOrders).ThenInclude(x => x.PreventivePlan)
            .Include(x => x.WorkOrders).ThenInclude(x => x.Checklist)
            .Include(x => x.WorkOrders).ThenInclude(x => x.Activities)
            .Include(x => x.WorkOrders).ThenInclude(x => x.Attachments)
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Status == IncidentStatusEnum.Open ? 0 : x.Status == IncidentStatusEnum.InProgress ? 1 : 2)
            .ThenByDescending(x => x.Severity).ThenByDescending(x => x.CreatedAt).Take(120).ToListAsync(cancellationToken);
        var orders = await WorkOrderQuery(licenseId).AsNoTracking()
            .OrderBy(x => x.Status == WorkOrderStatusEnum.Completed || x.Status == WorkOrderStatusEnum.Cancelled ? 1 : 0)
            .ThenByDescending(x => x.Priority).ThenBy(x => x.DueAt).Take(120).ToListAsync(cancellationToken);
        var activeIncidents = incidents.Where(x => x.Status is IncidentStatusEnum.Open or IncidentStatusEnum.InProgress).ToList();
        var completed = incidents.Where(x => x.Status is IncidentStatusEnum.Resolved or IncidentStatusEnum.Closed).ToList();
        var compliant = completed.Count(x => !x.SlaResolutionDueAt.HasValue || x.ResolvedAt <= x.SlaResolutionDueAt);
        return Ok(new MaintenanceDashboardViewModel
        {
            OpenIncidents = activeIncidents.Count,
            CriticalIncidents = activeIncidents.Count(x => x.Severity == IncidentSeverityEnum.Critical),
            SlaOverdue = activeIncidents.Count(x => x.SlaResolutionDueAt < now),
            SlaAtRisk = activeIncidents.Count(x => x.SlaResolutionDueAt >= now && x.SlaResolutionDueAt <= now.AddHours(6)),
            OpenWorkOrders = orders.Count(x => x.Status is not WorkOrderStatusEnum.Completed and not WorkOrderStatusEnum.Cancelled),
            PreventiveDueSoon = await context.PreventiveMaintenancePlans.CountAsync(x => x.LicenseId == licenseId && x.IsActive && x.NextDueAt <= now.AddDays(15), cancellationToken),
            SlaCompliancePercent = completed.Count == 0 ? 100 : Math.Round(compliant * 100m / completed.Count, 1),
            Incidents = incidents.Select(x => IncidentsController.ToOut(x, true)).ToList(),
            WorkOrders = orders.Select(x => ToWorkOrder(x)).ToList()
        });
    }

    [HttpGet("work-orders/{workOrderId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ViewIncidents)]
    public async Task<IActionResult> GetWorkOrder(Guid licenseId, Guid workOrderId, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var order = await WorkOrderQuery(licenseId).AsNoTracking().FirstOrDefaultAsync(x => x.Id == workOrderId, cancellationToken);
        return order is null ? NotFound() : Ok(ToWorkOrder(order));
    }

    [HttpPost("work-orders")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> CreateWorkOrder(Guid licenseId, WorkOrderCreateViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        if (string.IsNullOrWhiteSpace(input.Title) || !Enum.IsDefined(typeof(IncidentSeverityEnum), input.Priority))
            return BadRequest(new { Errors = "Informe título e prioridade válidos." });
        try
        {
            var order = await maintenance.CreateWorkOrderAsync(licenseId, input, UserId(), Actor(), cancellationToken);
            return CreatedAtAction(nameof(GetWorkOrder), new { licenseId, workOrderId = order.Id }, ToWorkOrder(order));
        }
        catch (InvalidOperationException exception) { return BadRequest(new { Errors = exception.Message }); }
    }

    [HttpPatch("work-orders/{workOrderId:guid}/status")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdateWorkOrderStatus(Guid licenseId, Guid workOrderId, WorkOrderStatusUpdateViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        if (!Enum.IsDefined(typeof(WorkOrderStatusEnum), input.Status)) return BadRequest(new { Errors = "Situação inválida." });
        var order = await context.WorkOrders.Include(x => x.Checklist).FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.Id == workOrderId, cancellationToken);
        if (order is null) return NotFound();
        var target = (WorkOrderStatusEnum)input.Status;
        if (target == WorkOrderStatusEnum.Completed && order.Checklist.Any(x => x.IsRequired && !x.IsCompleted))
            return Conflict(new { Errors = "Conclua os itens obrigatórios do checklist antes de finalizar." });
        if (target == WorkOrderStatusEnum.Completed && string.IsNullOrWhiteSpace(input.Note))
            return BadRequest(new { Errors = "Informe a conclusão do serviço." });
        var now = DateTime.UtcNow;
        var previous = order.Status;
        order.Status = target;
        order.UpdatedAt = now;
        if (target == WorkOrderStatusEnum.InProgress) order.StartedAt ??= now;
        if (target == WorkOrderStatusEnum.Completed) { order.CompletedAt = now; order.CompletionNotes = Short(input.Note.Trim(), 2000); }
        else if (target != WorkOrderStatusEnum.Cancelled) order.CompletedAt = null;
        context.WorkOrderActivities.Add(MaintenanceService.Activity(order,
            target == WorkOrderStatusEnum.Completed ? WorkOrderActivityTypeEnum.Completed : WorkOrderActivityTypeEnum.StatusChanged,
            string.IsNullOrWhiteSpace(input.Note) ? $"Situação alterada para {target}." : input.Note.Trim(), UserId(), Actor(), input.VisibleToResident, now));
        AddAudit(order, "StatusChanged", new { Previous = previous, Current = target, input.Note });
        await context.SaveChangesAsync(cancellationToken);
        if (order.IncidentId.HasValue)
        {
            var residentId = await context.Incidents.AsNoTracking()
                .Where(x => x.LicenseId == licenseId && x.Id == order.IncidentId)
                .Select(x => x.ReportedByResidentId)
                .FirstOrDefaultAsync(cancellationToken);
            if (residentId.HasValue)
            {
                var message = target == WorkOrderStatusEnum.Completed
                    ? "O serviço relacionado à sua ocorrência foi concluído."
                    : $"O atendimento da sua ocorrência foi atualizado para {StatusLabel(target)}.";
                await notifier.NotifyResidentAsync(
                    residentId.Value,
                    CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory.Operational,
                    "Andamento do atendimento",
                    message,
                    $"/ocorrencias/{order.IncidentId:D}",
                    $"work-order-status:{order.Id:N}:{now.Ticks}",
                    cancellationToken);
            }
        }
        return await GetWorkOrder(licenseId, workOrderId, cancellationToken);
    }

    private static string StatusLabel(WorkOrderStatusEnum status) => status switch
    {
        WorkOrderStatusEnum.Planned => "planejado",
        WorkOrderStatusEnum.Assigned => "atribuído",
        WorkOrderStatusEnum.InProgress => "em execução",
        WorkOrderStatusEnum.WaitingProvider => "aguardando prestador",
        WorkOrderStatusEnum.WaitingMaterial => "aguardando material",
        WorkOrderStatusEnum.Completed => "concluído",
        WorkOrderStatusEnum.Cancelled => "cancelado",
        _ => "atualizado"
    };

    [HttpPatch("work-orders/{workOrderId:guid}/assignment")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> AssignWorkOrder(Guid licenseId, Guid workOrderId, WorkOrderAssignmentViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        if (input.ProviderId.HasValue && !await context.MaintenanceProviders.AnyAsync(x => x.LicenseId == licenseId && x.Id == input.ProviderId && x.IsActive, cancellationToken))
            return BadRequest(new { Errors = "Prestador indisponível." });
        var order = await context.WorkOrders.FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.Id == workOrderId, cancellationToken);
        if (order is null) return NotFound();
        order.AssignedToName = Short(input.AssignedToName.Trim(), 150); order.ProviderId = input.ProviderId;
        order.Status = string.IsNullOrWhiteSpace(order.AssignedToName) && !order.ProviderId.HasValue ? WorkOrderStatusEnum.Planned : WorkOrderStatusEnum.Assigned;
        order.UpdatedAt = DateTime.UtcNow;
        context.WorkOrderActivities.Add(MaintenanceService.Activity(order, WorkOrderActivityTypeEnum.Assignment,
            "Responsável e prestador atualizados.", UserId(), Actor(), true, order.UpdatedAt));
        await context.SaveChangesAsync(cancellationToken);
        return await GetWorkOrder(licenseId, workOrderId, cancellationToken);
    }

    [HttpPatch("work-orders/{workOrderId:guid}/costs")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdateCosts(Guid licenseId, Guid workOrderId, WorkOrderCostUpdateViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var order = await context.WorkOrders.FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.Id == workOrderId, cancellationToken);
        if (order is null) return NotFound();
        order.EstimatedCost = input.EstimatedCost; order.ActualCost = input.ActualCost; order.UpdatedAt = DateTime.UtcNow;
        context.WorkOrderActivities.Add(MaintenanceService.Activity(order, WorkOrderActivityTypeEnum.Cost,
            string.IsNullOrWhiteSpace(input.Note) ? "Custos informativos atualizados." : input.Note.Trim(), UserId(), Actor(), false, order.UpdatedAt));
        await context.SaveChangesAsync(cancellationToken);
        return await GetWorkOrder(licenseId, workOrderId, cancellationToken);
    }

    [HttpPatch("work-orders/{workOrderId:guid}/checklist/{itemId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdateChecklist(Guid licenseId, Guid workOrderId, Guid itemId, WorkOrderChecklistUpdateViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var item = await context.WorkOrderChecklistItems.Include(x => x.WorkOrder)
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.WorkOrderId == workOrderId && x.Id == itemId, cancellationToken);
        if (item is null) return NotFound();
        item.IsCompleted = input.IsCompleted; item.CompletedAt = input.IsCompleted ? DateTime.UtcNow : null;
        item.CompletedByUserId = input.IsCompleted ? UserId() : null; item.CompletedByName = input.IsCompleted ? Actor() : string.Empty;
        item.WorkOrder.UpdatedAt = DateTime.UtcNow;
        context.WorkOrderActivities.Add(MaintenanceService.Activity(item.WorkOrder, WorkOrderActivityTypeEnum.Checklist,
            $"Checklist '{item.Title}' {(input.IsCompleted ? "concluído" : "reaberto")}.", UserId(), Actor(), true, item.WorkOrder.UpdatedAt));
        await context.SaveChangesAsync(cancellationToken);
        return await GetWorkOrder(licenseId, workOrderId, cancellationToken);
    }

    [HttpGet("providers")]
    [RequireLicensePermission(LicensePermissionEnum.ViewIncidents)]
    public async Task<IActionResult> Providers(Guid licenseId, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var rows = await context.MaintenanceProviders.AsNoTracking().Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .Select(x => new MaintenanceProviderViewModel
            {
                Id = x.Id, Name = x.Name, Specialty = x.Specialty, ContactName = x.ContactName, Phone = x.Phone,
                Email = x.Email, Notes = x.Notes, IsActive = x.IsActive,
                OpenWorkOrders = x.WorkOrders.Count(o => o.Status != WorkOrderStatusEnum.Completed && o.Status != WorkOrderStatusEnum.Cancelled)
            }).ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost("providers")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> CreateProvider(Guid licenseId, MaintenanceProviderFormViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        if (string.IsNullOrWhiteSpace(input.Name)) return BadRequest(new { Errors = "Informe o nome do prestador." });
        var row = new MaintenanceProviderDTO { Id = Guid.NewGuid(), LicenseId = licenseId, CreatedAt = DateTime.UtcNow };
        Apply(row, input); context.MaintenanceProviders.Add(row); await context.SaveChangesAsync(cancellationToken);
        return Ok(ToProvider(row));
    }

    [HttpPut("providers/{providerId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdateProvider(Guid licenseId, Guid providerId, MaintenanceProviderFormViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var row = await context.MaintenanceProviders.FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.Id == providerId, cancellationToken);
        if (row is null) return NotFound(); Apply(row, input); await context.SaveChangesAsync(cancellationToken); return Ok(ToProvider(row));
    }

    [HttpGet("preventive-plans")]
    [RequireLicensePermission(LicensePermissionEnum.ViewIncidents)]
    public async Task<IActionResult> PreventivePlans(Guid licenseId, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var rows = await context.PreventiveMaintenancePlans.AsNoTracking().Include(x => x.DefaultProvider)
            .Where(x => x.LicenseId == licenseId).OrderByDescending(x => x.IsActive).ThenBy(x => x.NextDueAt).ToListAsync(cancellationToken);
        return Ok(rows.Select(ToPlan).ToList());
    }

    [HttpPost("preventive-plans")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> CreatePlan(Guid licenseId, PreventiveMaintenancePlanFormViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var row = new PreventiveMaintenancePlanDTO { Id = Guid.NewGuid(), LicenseId = licenseId, CreatedAt = DateTime.UtcNow };
        var error = await ApplyPlan(row, input, cancellationToken); if (error is not null) return BadRequest(new { Errors = error });
        context.PreventiveMaintenancePlans.Add(row); await context.SaveChangesAsync(cancellationToken); return Ok(ToPlan(row));
    }

    [HttpPut("preventive-plans/{planId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdatePlan(Guid licenseId, Guid planId, PreventiveMaintenancePlanFormViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var row = await context.PreventiveMaintenancePlans.Include(x => x.DefaultProvider).FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.Id == planId, cancellationToken);
        if (row is null) return NotFound(); var error = await ApplyPlan(row, input, cancellationToken); if (error is not null) return BadRequest(new { Errors = error });
        await context.SaveChangesAsync(cancellationToken); return Ok(ToPlan(row));
    }

    [HttpGet("policy")]
    [RequireLicensePermission(LicensePermissionEnum.ViewIncidents)]
    public async Task<IActionResult> GetPolicy(Guid licenseId, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var row = await context.MaintenancePolicies.AsNoTracking().FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        return Ok(ToPolicy(row));
    }

    [HttpPut("policy")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UpdatePolicy(Guid licenseId, MaintenancePolicyViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        var row = await context.MaintenancePolicies.FirstOrDefaultAsync(x => x.LicenseId == licenseId, cancellationToken);
        if (row is null) { row = new MaintenancePolicyDTO { LicenseId = licenseId }; context.MaintenancePolicies.Add(row); }
        CopyPolicy(row, input); row.UpdatedAt = DateTime.UtcNow; row.UpdatedBy = Actor(); await context.SaveChangesAsync(cancellationToken); return Ok(ToPolicy(row));
    }

    [HttpPost("incidents/{incidentId:guid}/attachments")]
    [RequireLicensePermission(LicensePermissionEnum.ManageIncidents)]
    public async Task<IActionResult> UploadAttachment(Guid licenseId, Guid incidentId, IncidentAttachmentUploadViewModel input, CancellationToken cancellationToken)
    {
        if (!await Enabled(licenseId, cancellationToken)) return ModuleDisabled();
        if (!await context.Incidents.AnyAsync(x => x.LicenseId == licenseId && x.Id == incidentId, cancellationToken)) return NotFound();
        try
        {
            var reference = await media.StoreDataUriAsync(licenseId, input.DataUri, cancellationToken);
            var row = new IncidentAttachmentDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, IncidentId = incidentId, MediaReference = reference,
                FileName = Short(input.FileName.Trim(), 260), ContentType = ContentType(input.DataUri), Caption = Short(input.Caption.Trim(), 500),
                VisibleToResident = input.VisibleToResident, UploadedByUserId = UserId(), UploadedByName = Actor(), CreatedAt = DateTime.UtcNow
            };
            context.IncidentAttachments.Add(row); await context.SaveChangesAsync(cancellationToken); return Ok(ToAttachment(row));
        }
        catch (InvalidOperationException exception) { return BadRequest(new { Errors = exception.Message }); }
    }

    private IQueryable<WorkOrderDTO> WorkOrderQuery(Guid licenseId) => context.WorkOrders
        .Include(x => x.Incident).Include(x => x.PreventivePlan).Include(x => x.Provider)
        .Include(x => x.Checklist.OrderBy(i => i.SortOrder)).Include(x => x.Activities.OrderByDescending(a => a.CreatedAt))
        .Include(x => x.Attachments).Where(x => x.LicenseId == licenseId);

    private Task<bool> Enabled(Guid licenseId, CancellationToken token) => modules.IsEnabledAsync(licenseId, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents, token);
    private NotFoundObjectResult ModuleDisabled() => NotFound(new { Code = "ModuleDisabled", Errors = "O módulo de ocorrências e manutenção está desativado neste condomínio." });
    private Guid? UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var value) ? value : null;
    private string Actor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuário";

    private void AddAudit(WorkOrderDTO order, string action, object details) => context.AccessOperationAudits.Add(
        MaintenanceService.Audit(order.LicenseId, order.Id, action, $"{order.Code}: {order.Title}", UserId(), Actor(), details));
    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
    private static string ContentType(string dataUri) => dataUri.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase) ? "image/png" : dataUri.StartsWith("data:image/webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";

    private static void Apply(MaintenanceProviderDTO row, MaintenanceProviderFormViewModel input)
    {
        row.Name = Short(input.Name.Trim(), 180); row.Specialty = Short(input.Specialty.Trim(), 150);
        row.ContactName = Short(input.ContactName.Trim(), 150); row.Phone = Short(input.Phone.Trim(), 40);
        row.Email = Short(input.Email.Trim(), 254); row.Notes = Short(input.Notes.Trim(), 1200); row.IsActive = input.IsActive; row.UpdatedAt = DateTime.UtcNow;
    }
    private static MaintenanceProviderViewModel ToProvider(MaintenanceProviderDTO x) => new()
    { Id = x.Id, Name = x.Name, Specialty = x.Specialty, ContactName = x.ContactName, Phone = x.Phone, Email = x.Email, Notes = x.Notes, IsActive = x.IsActive };

    private async Task<string?> ApplyPlan(PreventiveMaintenancePlanDTO row, PreventiveMaintenancePlanFormViewModel input, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) return "Informe o nome do plano.";
        if (input.DefaultProviderId.HasValue && !await context.MaintenanceProviders.AnyAsync(x => x.LicenseId == row.LicenseId && x.Id == input.DefaultProviderId && x.IsActive, token)) return "Prestador indisponível.";
        row.Name = Short(input.Name.Trim(), 180); row.Description = Short(input.Description.Trim(), 3000); row.LocationLabel = Short(input.LocationLabel.Trim(), 240);
        row.IntervalDays = Math.Clamp(input.IntervalDays, 1, 3650); row.LeadDays = Math.Clamp(input.LeadDays, 0, 365);
        // Preventive execution is a calendar date. Converting its midnight to UTC
        // makes the portal render the previous day in UTC-03.
        row.NextDueAt = input.NextDueAt.AsCalendarDate();
        row.DefaultProviderId = input.DefaultProviderId; row.DefaultAssignedToName = Short(input.DefaultAssignedToName.Trim(), 150);
        row.EstimatedCost = input.EstimatedCost; row.ChecklistTemplateJson = MaintenanceService.ChecklistJson(input.Checklist); row.IsActive = input.IsActive; row.UpdatedAt = DateTime.UtcNow; return null;
    }

    internal static WorkOrderViewModel ToWorkOrder(WorkOrderDTO x, bool resident = false) => new()
    {
        Id = x.Id, LicenseId = x.LicenseId, IncidentId = x.IncidentId, IncidentCode = x.Incident?.Code ?? string.Empty,
        PreventivePlanId = x.PreventivePlanId, PreventivePlanName = x.PreventivePlan?.Name ?? string.Empty,
        Code = x.Code, Title = x.Title, Description = x.Description, Status = x.Status.ToString(), Priority = x.Priority.ToString(),
        LocationLabel = x.LocationLabel, AssignedToName = x.AssignedToName, ProviderId = resident ? null : x.ProviderId,
        ProviderName = resident ? string.Empty : x.Provider?.Name ?? string.Empty, ScheduledFor = x.ScheduledFor, DueAt = x.DueAt,
        StartedAt = x.StartedAt, CompletedAt = x.CompletedAt, EstimatedCost = resident ? 0 : x.EstimatedCost,
        ActualCost = resident ? 0 : x.ActualCost, CompletionNotes = x.CompletionNotes, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt,
        Checklist = x.Checklist.OrderBy(i => i.SortOrder).Select(i => new WorkOrderChecklistItemViewModel { Id = i.Id, Title = i.Title, SortOrder = i.SortOrder, IsRequired = i.IsRequired, IsCompleted = i.IsCompleted, CompletedByName = resident ? string.Empty : i.CompletedByName, CompletedAt = i.CompletedAt }).ToList(),
        Activities = x.Activities.Where(a => !resident || a.VisibleToResident).OrderByDescending(a => a.CreatedAt).Select(a => new WorkOrderActivityViewModel { Id = a.Id, Type = a.Type.ToString(), Message = a.Message, ActorName = resident ? string.Empty : a.ActorName, VisibleToResident = a.VisibleToResident, CreatedAt = a.CreatedAt }).ToList(),
        Attachments = x.Attachments.Where(a => !resident || a.VisibleToResident).Select(ToAttachment).ToList()
    };
    internal static IncidentAttachmentViewModel ToAttachment(IncidentAttachmentDTO x) => new() { Id = x.Id, FileName = x.FileName, ContentType = x.ContentType, Caption = x.Caption, Url = x.MediaReference, UploadedByName = x.UploadedByName, VisibleToResident = x.VisibleToResident, CreatedAt = x.CreatedAt };
    private static PreventiveMaintenancePlanViewModel ToPlan(PreventiveMaintenancePlanDTO x) => new()
    {
        Id = x.Id, Name = x.Name, Description = x.Description, LocationLabel = x.LocationLabel, IntervalDays = x.IntervalDays,
        LeadDays = x.LeadDays, NextDueAt = x.NextDueAt, LastGeneratedFor = x.LastGeneratedFor, DefaultProviderId = x.DefaultProviderId,
        DefaultProviderName = x.DefaultProvider?.Name ?? string.Empty, DefaultAssignedToName = x.DefaultAssignedToName,
        EstimatedCost = x.EstimatedCost, IsActive = x.IsActive,
        Checklist = MaintenanceService.ParseChecklist(x.ChecklistTemplateJson).Select(i => new WorkOrderChecklistInputViewModel { Title = i.Title, IsRequired = i.IsRequired }).ToList()
    };
    private static MaintenancePolicyViewModel ToPolicy(MaintenancePolicyDTO? x) => x is null ? new() : new()
    {
        LowResponseMinutes = x.LowResponseMinutes, LowResolutionMinutes = x.LowResolutionMinutes,
        MediumResponseMinutes = x.MediumResponseMinutes, MediumResolutionMinutes = x.MediumResolutionMinutes,
        HighResponseMinutes = x.HighResponseMinutes, HighResolutionMinutes = x.HighResolutionMinutes,
        CriticalResponseMinutes = x.CriticalResponseMinutes, CriticalResolutionMinutes = x.CriticalResolutionMinutes
    };
    private static void CopyPolicy(MaintenancePolicyDTO row, MaintenancePolicyViewModel x)
    {
        row.LowResponseMinutes = x.LowResponseMinutes; row.LowResolutionMinutes = x.LowResolutionMinutes;
        row.MediumResponseMinutes = x.MediumResponseMinutes; row.MediumResolutionMinutes = x.MediumResolutionMinutes;
        row.HighResponseMinutes = x.HighResponseMinutes; row.HighResolutionMinutes = x.HighResolutionMinutes;
        row.CriticalResponseMinutes = x.CriticalResponseMinutes; row.CriticalResolutionMinutes = x.CriticalResolutionMinutes;
    }
}
