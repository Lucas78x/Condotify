using CondotifyAPI.Domain.DTO.RecycleBin;
using CondotifyAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.RecycleBin;

public sealed class RecycleBinItemConfiguration : IEntityTypeConfiguration<RecycleBinItemDTO>
{
    public void Configure(EntityTypeBuilder<RecycleBinItemDTO> builder)
    {
        builder.ToTable("RecycleBinItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(40);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SnapshotJson)
            .IsRequired()
            .HasConversion(new EquipmentSecretConverter())
            .HasColumnType("text");
        builder.Property(x => x.DeletedBy).HasMaxLength(150);
        builder.Property(x => x.RestoredBy).HasMaxLength(150);
        builder.Property(x => x.DeletedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.RestoredAt, x.ExpiresAt });
        builder.HasIndex(x => new { x.LicenseId, x.EntityType, x.EntityId });
    }
}
