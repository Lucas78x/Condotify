using CondotifyAPI.Domain.DTO.Announcements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Announcements;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<AnnouncementDTO>
{
    public void Configure(EntityTypeBuilder<AnnouncementDTO> builder)
    {
        builder.ToTable("Announcements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}
