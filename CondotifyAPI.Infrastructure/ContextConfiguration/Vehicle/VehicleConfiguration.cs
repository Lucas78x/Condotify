using CondotifyAPI.Domain.DTO.Vehicle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Vehicle;

public class VehicleConfiguration : IEntityTypeConfiguration<VehicleDTO>
{
    public void Configure(EntityTypeBuilder<VehicleDTO> builder)
    {
        builder.ToTable("Vehicles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Plate).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Brand).HasMaxLength(60);
        builder.Property(x => x.Model).HasMaxLength(80);
        builder.Property(x => x.Color).HasMaxLength(40);
        builder.Property(x => x.Type).HasMaxLength(40);
        builder.Property(x => x.TagIdentifier).HasMaxLength(100);
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => new { x.UnitId, x.Plate }).IsUnique();
        builder.HasIndex(x => x.TagIdentifier);
        builder.HasIndex(x => x.ResidentId);
    }
}
