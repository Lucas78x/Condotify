using CondotifyAPI.Domain.DTO.Vehicle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Vehicle;

public sealed class VehicleAccessAuditConfiguration : IEntityTypeConfiguration<VehicleAccessAuditDTO>
{
    public void Configure(EntityTypeBuilder<VehicleAccessAuditDTO> builder)
    {
        builder.ToTable("VehicleAccessAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlateRead).HasMaxLength(10);
        builder.Property(x => x.SnapshotReference).HasMaxLength(300);
        builder.Property(x => x.Timestamp).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Device)
            .WithMany()
            .HasForeignKey(x => x.AccessControlDeviceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AccessControlDeviceId, x.Timestamp });
        builder.HasIndex(x => x.MatchedVehicleId);
    }
}
