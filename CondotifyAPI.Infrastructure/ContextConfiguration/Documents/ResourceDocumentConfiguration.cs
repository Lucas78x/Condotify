using CondotifyAPI.Domain.DTO.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Documents;

public sealed class ResourceDocumentConfiguration : IEntityTypeConfiguration<ResourceDocumentDTO>
{
    public void Configure(EntityTypeBuilder<ResourceDocumentDTO> builder)
    {
        builder.ToTable("ResourceDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.StorageReference).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UploadedByName).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Category, x.PublishedAt });
    }
}
