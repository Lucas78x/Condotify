using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Observability;

public sealed class OperationalAlertConfiguration : IEntityTypeConfiguration<OperationalAlertDTO>
{
    public void Configure(EntityTypeBuilder<OperationalAlertDTO> builder)
    {
        builder.ToTable("OperationalAlerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Fingerprint).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Type).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Source).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.TargetUrl).HasMaxLength(300);
        builder.Property(x => x.ResourceType).HasMaxLength(80);
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(150);
        builder.Property(x => x.AcknowledgementNote).HasMaxLength(500);
        builder.Property(x => x.ResolvedBy).HasMaxLength(150);
        builder.Property(x => x.ResolutionNote).HasMaxLength(500);
        builder.Property(x => x.SuppressedBy).HasMaxLength(150);
        builder.Property(x => x.SuppressionReason).HasMaxLength(500);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Enterprise)
            .WithMany()
            .HasForeignKey(x => x.EnterpriseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.EnterpriseId, x.Fingerprint }).IsUnique();
        builder.HasIndex(x => new { x.EnterpriseId, x.Status, x.Severity, x.LastOccurredAt });
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.LastOccurredAt });
    }
}

public sealed class AlertNotificationPolicyConfiguration : IEntityTypeConfiguration<AlertNotificationPolicyDTO>
{
    public void Configure(EntityTypeBuilder<AlertNotificationPolicyDTO> builder)
    {
        builder.ToTable("AlertNotificationPolicies");
        builder.HasKey(x => x.LicenseId);
        builder.Property(x => x.WarningSlaMinutes).IsRequired().HasDefaultValue(60);
        builder.Property(x => x.CriticalSlaMinutes).IsRequired().HasDefaultValue(15);
        builder.Property(x => x.EscalationRepeatMinutes).IsRequired().HasDefaultValue(60);
        builder.Property(x => x.WebhookUrl).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.WebhookSecret).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.EmailRecipients).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.SmtpHost).HasMaxLength(200);
        builder.Property(x => x.SmtpPort).IsRequired().HasDefaultValue(587);
        builder.Property(x => x.SmtpUsername).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.SmtpPassword).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.SmtpFromEmail).HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.SmtpFromName).HasMaxLength(120);
        builder.Property(x => x.SmtpEnableSsl).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License)
            .WithOne()
            .HasForeignKey<AlertNotificationPolicyDTO>(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class AlertNotificationDeliveryConfiguration : IEntityTypeConfiguration<AlertNotificationDeliveryDTO>
{
    public void Configure(EntityTypeBuilder<AlertNotificationDeliveryDTO> builder)
    {
        builder.ToTable("AlertNotificationDeliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeliveryKey).IsRequired().HasMaxLength(180);
        builder.Property(x => x.DestinationLabel).IsRequired().HasMaxLength(240);
        builder.Property(x => x.LeaseOwner).HasMaxLength(180);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Alert)
            .WithMany()
            .HasForeignKey(x => x.AlertId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.DeliveryKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.LeaseExpiresAt });
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}
