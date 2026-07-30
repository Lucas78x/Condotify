using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Infrastructure.ContextConfiguration.Observability;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<OperationalAlertDTO> OperationalAlerts { get; set; }
    public DbSet<AlertNotificationPolicyDTO> AlertNotificationPolicies { get; set; }
    public DbSet<AlertNotificationDeliveryDTO> AlertNotificationDeliveries { get; set; }

    internal static void ObservabilityEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new OperationalAlertConfiguration());
        builder.ApplyConfiguration(new AlertNotificationPolicyConfiguration());
        builder.ApplyConfiguration(new AlertNotificationDeliveryConfiguration());
    }
}
