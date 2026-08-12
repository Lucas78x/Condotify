using CondotifyAPI.Domain.DTO.Finance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Finance;

public sealed class FinancialRecurringRuleConfiguration : IEntityTypeConfiguration<FinancialRecurringRuleDTO>
{
    public void Configure(EntityTypeBuilder<FinancialRecurringRuleDTO> builder)
    {
        builder.ToTable("FinancialRecurringRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ReferenceTemplate).IsRequired().HasMaxLength(80);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.BaseAmount).HasPrecision(18, 2);
        builder.Property(x => x.FineAmount).HasPrecision(18, 2);
        builder.Property(x => x.InterestAmount).HasPrecision(18, 2);
        builder.Property(x => x.DiscountAmount).HasPrecision(18, 2);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.IsActive, x.NextRunAt });
        builder.HasIndex(x => new { x.LicenseId, x.Name });
    }
}
public sealed class FinancialRecurringRuleUnitConfiguration : IEntityTypeConfiguration<FinancialRecurringRuleUnitDTO>
{
    public void Configure(EntityTypeBuilder<FinancialRecurringRuleUnitDTO> builder)
    {
        builder.ToTable("FinancialRecurringRuleUnits");
        builder.HasKey(x => new { x.RuleId, x.UnitId });
        builder.HasOne(x => x.Rule).WithMany(x => x.Units).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.LicenseId, x.UnitId });
    }
}

public sealed class FinancialImportBatchConfiguration : IEntityTypeConfiguration<FinancialImportBatchDTO>
{
    public void Configure(EntityTypeBuilder<FinancialImportBatchDTO> builder)
    {
        builder.ToTable("FinancialImportBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(64);
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SourceHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.ErrorSummary).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}

public sealed class FinancialReminderPolicyConfiguration : IEntityTypeConfiguration<FinancialReminderPolicyDTO>
{
    public void Configure(EntityTypeBuilder<FinancialReminderPolicyDTO> builder)
    {
        builder.ToTable("FinancialReminderPolicies");
        builder.HasKey(x => x.LicenseId);
        builder.Property(x => x.BeforeDueDays).IsRequired().HasMaxLength(40);
        builder.Property(x => x.UpdatedBy).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class FinancialReminderDeliveryConfiguration : IEntityTypeConfiguration<FinancialReminderDeliveryDTO>
{
    public void Configure(EntityTypeBuilder<FinancialReminderDeliveryDTO> builder)
    {
        builder.ToTable("FinancialReminderDeliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StageKey).IsRequired().HasMaxLength(40);
        builder.Property(x => x.DeliveryKey).IsRequired().HasMaxLength(220);
        builder.Property(x => x.DestinationLabel).IsRequired().HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Charge).WithMany().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Resident).WithMany().HasForeignKey(x => x.ResidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LicenseId, x.DeliveryKey }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.CreatedAt });
        builder.HasIndex(x => new { x.ResidentId, x.FinishedAt });
        builder.HasIndex(x => new { x.ChargeId, x.CreatedAt });
    }
}
