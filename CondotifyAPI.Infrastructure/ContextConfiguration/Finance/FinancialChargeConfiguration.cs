using CondotifyAPI.Domain.DTO.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Finance;

public sealed class FinancialChargeConfiguration : IEntityTypeConfiguration<FinancialChargeDTO>
{
    public void Configure(EntityTypeBuilder<FinancialChargeDTO> builder)
    {
        builder.ToTable("FinancialCharges");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestKey).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Competence).IsRequired().HasMaxLength(7);
        builder.Property(x => x.Reference).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BaseAmount).HasPrecision(18, 2);
        builder.Property(x => x.FineAmount).HasPrecision(18, 2);
        builder.Property(x => x.InterestAmount).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentReference).HasMaxLength(100);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(150);

        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BoletoDocument).WithMany().HasForeignKey(x => x.BoletoDocumentId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => new { x.LicenseId, x.RequestKey }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.DueDate });
        builder.HasIndex(x => new { x.LicenseId, x.UnitId, x.DueDate });
    }
}
public sealed class FinancialChargeEventConfiguration : IEntityTypeConfiguration<FinancialChargeEventDTO>
{
    public void Configure(EntityTypeBuilder<FinancialChargeEventDTO> builder)
    {
        builder.ToTable("FinancialChargeEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ActorType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.ActorName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Note).HasMaxLength(500);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Charge).WithMany(x => x.Events).HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
        builder.HasIndex(x => new { x.ChargeId, x.CreatedAt });
    }
}
