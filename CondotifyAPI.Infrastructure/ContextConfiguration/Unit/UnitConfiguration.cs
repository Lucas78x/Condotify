using CondotifyAPI.Domain.DTO.Unit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Unit
{
    public class UnitConfiguration : IEntityTypeConfiguration<UnitDTO>
    {
        public void Configure(EntityTypeBuilder<UnitDTO> builder)
        {
            builder.ToTable("Units");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.Number)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(u => u.Floor)
                .HasMaxLength(20);

            builder.HasMany(u => u.Residents)
                .WithOne(r => r.Unit)
                .HasForeignKey(r => r.UnitId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(u => u.ResidentLinks)
                .WithOne(x => x.Unit)
                .HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(u => u.Vehicles)
                .WithOne(x => x.Unit)
                .HasForeignKey(x => x.UnitId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(u => new { u.BlockId, u.Number })
                .IsUnique();
        }
    }
}
