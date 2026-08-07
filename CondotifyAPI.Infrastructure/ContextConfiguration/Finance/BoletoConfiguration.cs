using CondotifyAPI.Domain.DTO.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Finance;

public sealed class BoletoBatchConfiguration : IEntityTypeConfiguration<BoletoBatchDTO>
{
    public void Configure(EntityTypeBuilder<BoletoBatchDTO> builder)
    {
        builder.ToTable("BoletoBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reference).IsRequired().HasMaxLength(80);
        builder.Property(x => x.UploadedByName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.SourceFileName).IsRequired().HasMaxLength(260);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.CreatedAt });
    }
}

public sealed class BoletoDocumentConfiguration : IEntityTypeConfiguration<BoletoDocumentDTO>
{
    public void Configure(EntityTypeBuilder<BoletoDocumentDTO> builder)
    {
        builder.ToTable("BoletoDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StorageReference).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ExtractedSnippet).HasMaxLength(500);
        builder.HasOne(x => x.Batch).WithMany(x => x.Documents).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.UnitId);
    }
}
