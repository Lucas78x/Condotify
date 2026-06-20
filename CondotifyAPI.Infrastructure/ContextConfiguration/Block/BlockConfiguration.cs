using CondotifyAPI.Domain.DTO.Block;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Block
{
    public class BlockConfiguration : IEntityTypeConfiguration<BlockDTO>
    {
        public void Configure(EntityTypeBuilder<BlockDTO> builder)
        {
            builder.ToTable("Blocks");

            builder.HasKey(b => b.Id);

            builder.Property(b => b.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(b => b.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(b => b.LastUpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.HasMany(b => b.Units)
                .WithOne(u => u.Block)
                .HasForeignKey(u => u.BlockId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(b => new { b.LicenseId, b.Name })
                .IsUnique();
        }
    }
}
