using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Infrastructure.ContextConfiguration.Mobile;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<PushInstallationDTO> PushInstallations { get; set; }
    public DbSet<PushPreferenceDTO> PushPreferences { get; set; }
    public DbSet<PushNotificationDTO> PushNotifications { get; set; }
    public DbSet<PushDeliveryDTO> PushDeliveries { get; set; }

    internal static void MobileEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new PushInstallationConfiguration());
        builder.ApplyConfiguration(new PushPreferenceConfiguration());
        builder.ApplyConfiguration(new PushNotificationConfiguration());
        builder.ApplyConfiguration(new PushDeliveryConfiguration());
    }
}
