using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Backup;

public sealed class ConfigurationBackupConfiguration : IEntityTypeConfiguration<ConfigurationBackupDTO>
{
    public void Configure(EntityTypeBuilder<ConfigurationBackupDTO> builder)
    {
        builder.ToTable("ConfigurationBackups");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.PayloadJson)
            .IsRequired()
            .HasConversion(new EquipmentSecretConverter())
            .HasColumnType("text");
        builder.Property(x => x.Checksum).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.Property(x => x.LastRestoredBy).HasMaxLength(150);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Version }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}

public sealed class BackupAutomationPolicyConfiguration : IEntityTypeConfiguration<BackupAutomationPolicyDTO>
{
    public void Configure(EntityTypeBuilder<BackupAutomationPolicyDTO> builder)
    {
        builder.ToTable("BackupAutomationPolicies");
        builder.HasKey(x => x.LicenseId);
        builder.Property(x => x.IntervalHours).IsRequired().HasDefaultValue(24);
        builder.Property(x => x.ExportEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.ExternalRetentionDays).IsRequired().HasDefaultValue(90);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.LeaseOwner).HasMaxLength(180);
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License)
            .WithOne()
            .HasForeignKey<BackupAutomationPolicyDTO>(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.Enabled, x.NextRunAt, x.LeaseExpiresAt });
    }
}
