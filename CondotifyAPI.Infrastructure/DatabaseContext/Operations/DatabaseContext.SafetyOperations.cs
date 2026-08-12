using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure.ContextConfiguration.Operations;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<IncidentDTO> Incidents { get; set; }
    public DbSet<IncidentTimelineEntryDTO> IncidentTimelineEntries { get; set; }
    public DbSet<MaintenancePolicyDTO> MaintenancePolicies { get; set; }
    public DbSet<MaintenanceProviderDTO> MaintenanceProviders { get; set; }
    public DbSet<PreventiveMaintenancePlanDTO> PreventiveMaintenancePlans { get; set; }
    public DbSet<WorkOrderDTO> WorkOrders { get; set; }
    public DbSet<WorkOrderChecklistItemDTO> WorkOrderChecklistItems { get; set; }
    public DbSet<WorkOrderActivityDTO> WorkOrderActivities { get; set; }
    public DbSet<IncidentAttachmentDTO> IncidentAttachments { get; set; }
    public DbSet<AutomationRuleDTO> AutomationRules { get; set; }
    public DbSet<AutomationExecutionDTO> AutomationExecutions { get; set; }
    public DbSet<EmergencySessionDTO> EmergencySessions { get; set; }
    public DbSet<DigitalPassDTO> DigitalPasses { get; set; }

    internal static void SafetyOperationsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new IncidentConfiguration());
        builder.ApplyConfiguration(new IncidentTimelineEntryConfiguration());
        builder.ApplyConfiguration(new MaintenancePolicyConfiguration());
        builder.ApplyConfiguration(new MaintenanceProviderConfiguration());
        builder.ApplyConfiguration(new PreventiveMaintenancePlanConfiguration());
        builder.ApplyConfiguration(new WorkOrderConfiguration());
        builder.ApplyConfiguration(new WorkOrderChecklistItemConfiguration());
        builder.ApplyConfiguration(new WorkOrderActivityConfiguration());
        builder.ApplyConfiguration(new IncidentAttachmentConfiguration());
        builder.ApplyConfiguration(new AutomationRuleConfiguration());
        builder.ApplyConfiguration(new AutomationExecutionConfiguration());
        builder.ApplyConfiguration(new EmergencySessionConfiguration());
        builder.ApplyConfiguration(new DigitalPassConfiguration());
    }
}
