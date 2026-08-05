using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure.ContextConfiguration.Operations;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<IncidentDTO> Incidents { get; set; }
    public DbSet<IncidentTimelineEntryDTO> IncidentTimelineEntries { get; set; }
    public DbSet<AutomationRuleDTO> AutomationRules { get; set; }
    public DbSet<AutomationExecutionDTO> AutomationExecutions { get; set; }
    public DbSet<EmergencySessionDTO> EmergencySessions { get; set; }
    public DbSet<DigitalPassDTO> DigitalPasses { get; set; }

    internal static void SafetyOperationsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new IncidentConfiguration());
        builder.ApplyConfiguration(new IncidentTimelineEntryConfiguration());
        builder.ApplyConfiguration(new AutomationRuleConfiguration());
        builder.ApplyConfiguration(new AutomationExecutionConfiguration());
        builder.ApplyConfiguration(new EmergencySessionConfiguration());
        builder.ApplyConfiguration(new DigitalPassConfiguration());
    }
}
