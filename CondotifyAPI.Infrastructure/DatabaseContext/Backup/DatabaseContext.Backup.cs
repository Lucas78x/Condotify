using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Infrastructure.ContextConfiguration.Backup;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<ConfigurationBackupDTO> ConfigurationBackups { get; set; }
    public DbSet<BackupAutomationPolicyDTO> BackupAutomationPolicies { get; set; }

    internal static void BackupsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ConfigurationBackupConfiguration());
        builder.ApplyConfiguration(new BackupAutomationPolicyConfiguration());
    }
}
