using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.AccessControl;

public sealed class OfflineAccessDeviceConfiguration : IEntityTypeConfiguration<OfflineAccessDeviceDTO>
{
    public void Configure(EntityTypeBuilder<OfflineAccessDeviceDTO> builder)
    {
        builder.ToTable("OfflineAccessDevices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.InstallationId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DeviceName).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Platform).HasMaxLength(60);
        builder.Property(x => x.AppVersion).HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.DeviceSecret).IsRequired().HasConversion(new EquipmentSecretConverter()).HasColumnType("text");
        builder.Property(x => x.OfflineWindowMinutes).IsRequired().HasDefaultValue(480);
        builder.Property(x => x.ApprovedBy).HasMaxLength(160);
        builder.Property(x => x.RevokedBy).HasMaxLength(160);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LicenseId, x.InstallationId }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.LastSyncedAt });
    }
}

public sealed class OfflineAccessOperationConfiguration : IEntityTypeConfiguration<OfflineAccessOperationDTO>
{
    public void Configure(EntityTypeBuilder<OfflineAccessOperationDTO> builder)
    {
        builder.ToTable("OfflineAccessOperations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.CodeHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.BeforeStatus).HasMaxLength(50);
        builder.Property(x => x.AfterStatus).HasMaxLength(50);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(500);
        builder.Property(x => x.UserName).HasMaxLength(160);
        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.ReceivedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Device).WithMany(x => x.Operations).HasForeignKey(x => x.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Visit).WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.DeviceId, x.ClientOperationId }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.ReceivedAt });
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.ReceivedAt });
    }
}
