using CondotifyAPI.Domain.DTO.AccessControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.AccessControl;

public sealed class AccessRouteConfiguration : IEntityTypeConfiguration<AccessRouteDTO>
{
    public void Configure(EntityTypeBuilder<AccessRouteDTO> builder)
    {
        builder.ToTable("AccessRoutes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(300);
        builder.Property(x => x.Audience).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.AllowTemporary).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.DaysOfWeekMask).IsRequired().HasDefaultValue(127);
        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.EndTime).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.License)
            .WithMany(x => x.AccessRoutes)
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
    }
}

public sealed class AccessRouteDeviceConfiguration : IEntityTypeConfiguration<AccessRouteDeviceDTO>
{
    public void Configure(EntityTypeBuilder<AccessRouteDeviceDTO> builder)
    {
        builder.ToTable("AccessRouteDevices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PortalNumber).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.Direction).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.AccessRoute)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.AccessRouteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Device)
            .WithMany(x => x.AccessRoutes)
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AccessRouteId, x.DeviceId, x.PortalNumber }).IsUnique();
    }
}

public sealed class AccessRouteResidentOverrideConfiguration : IEntityTypeConfiguration<AccessRouteResidentOverrideDTO>
{
    public void Configure(EntityTypeBuilder<AccessRouteResidentOverrideDTO> builder)
    {
        builder.ToTable("AccessRouteResidentOverrides");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Mode).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(300);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.AccessRoute).WithMany(x => x.ResidentOverrides).HasForeignKey(x => x.AccessRouteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Resident).WithMany().HasForeignKey(x => x.ResidentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AccessRouteId, x.ResidentId }).IsUnique();
    }
}

public sealed class AccessOperationAuditConfiguration : IEntityTypeConfiguration<AccessOperationAuditDTO>
{
    public void Configure(EntityTypeBuilder<AccessOperationAuditDTO> builder)
    {
        builder.ToTable("AccessOperationAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(500);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(x => x.UserName).HasMaxLength(150);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
    }
}

public sealed class AccessBatchOperationConfiguration : IEntityTypeConfiguration<AccessBatchOperationDTO>
{
    public void Configure(EntityTypeBuilder<AccessBatchOperationDTO> builder)
    {
        builder.ToTable("AccessBatchOperations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Operation).IsRequired().HasMaxLength(80);
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.RequestedBy).HasMaxLength(150);
        builder.Property(x => x.FilterJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(x => x.Priority).IsRequired().HasDefaultValue(50);
        builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).IsRequired().HasDefaultValue(5);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.LeaseOwner).HasMaxLength(180);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.Priority, x.CreatedAt });
        builder.HasIndex(x => new { x.LicenseId, x.IdempotencyKey }).IsUnique();
    }
}

public sealed class AccessOperationItemConfiguration : IEntityTypeConfiguration<AccessOperationItemDTO>
{
    public void Configure(EntityTypeBuilder<AccessOperationItemDTO> builder)
    {
        builder.ToTable("AccessOperationItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Error).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Batch).WithMany(x => x.Items).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.BatchId, x.DeviceId });
    }
}

public sealed class AccessInventoryItemConfiguration : IEntityTypeConfiguration<AccessInventoryItemDTO>
{
    public void Configure(EntityTypeBuilder<AccessInventoryItemDTO> builder)
    {
        builder.ToTable("AccessInventoryItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RemoteKey).IsRequired().HasMaxLength(220);
        builder.Property(x => x.ExternalUserId).HasMaxLength(150);
        builder.Property(x => x.ExternalCredentialId).HasMaxLength(150);
        builder.Property(x => x.CredentialType).HasMaxLength(50);
        builder.Property(x => x.Identifier).HasMaxLength(200);
        builder.Property(x => x.PersonName).HasMaxLength(200);
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(x => x.ObservedAt).IsRequired();
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.DeviceId, x.RemoteKey }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status });
        builder.HasIndex(x => x.CredentialId);
    }
}

public sealed class AccessEventRecordConfiguration : IEntityTypeConfiguration<AccessEventRecordDTO>
{
    public void Configure(EntityTypeBuilder<AccessEventRecordDTO> builder)
    {
        builder.ToTable("AccessEventRecords");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExternalEventId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Event).IsRequired().HasMaxLength(120);
        builder.Property(x => x.ExternalUserId).HasMaxLength(150);
        builder.Property(x => x.PersonName).HasMaxLength(200);
        builder.Property(x => x.Credential).HasMaxLength(200);
        builder.Property(x => x.Portal).HasMaxLength(120);
        builder.Property(x => x.Details).HasMaxLength(1000);
        builder.Property(x => x.AttentionResolvedBy).HasMaxLength(150);
        builder.Property(x => x.AttentionResolutionNote).HasMaxLength(500);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Device).WithMany().HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AccessCredential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.DeviceId, x.ExternalEventId }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.OccurredAt });
        builder.HasIndex(x => new { x.LicenseId, x.Authorized, x.AttentionResolvedAt });
        builder.HasIndex(x => x.CredentialId);
    }
}
