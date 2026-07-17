using CondotifyAPI.Domain.DTO.Invitation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Invitation;

public sealed class AccessVisitConfiguration : IEntityTypeConfiguration<AccessVisitDTO>
{
    public void Configure(EntityTypeBuilder<AccessVisitDTO> builder)
    {
        builder.ToTable("AccessVisits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.VisitorName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Document).HasMaxLength(40);
        builder.Property(x => x.PhoneNumber).HasMaxLength(40);
        builder.Property(x => x.Company).HasMaxLength(150);
        builder.Property(x => x.Purpose).HasMaxLength(300);
        builder.Property(x => x.VehiclePlate).HasMaxLength(20);
        builder.Property(x => x.PhotoUrl).HasColumnType("text");
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(150);
        builder.Property(x => x.ApprovedBy).HasMaxLength(150);
        builder.Property(x => x.ApprovalNotes).HasMaxLength(500);
        builder.Property(x => x.RecurrenceSequence).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.RecurrenceCount).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.HostResident).WithMany().HasForeignKey(x => x.HostResidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.GuestResident).WithMany().HasForeignKey(x => x.GuestResidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.ValidFrom });
        builder.HasIndex(x => x.CredentialId).IsUnique();
    }
}

public sealed class AccessWatchlistEntryConfiguration : IEntityTypeConfiguration<AccessWatchlistEntryDTO>
{
    public void Configure(EntityTypeBuilder<AccessWatchlistEntryDTO> builder)
    {
        builder.ToTable("AccessWatchlistEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Document).HasMaxLength(40);
        builder.Property(x => x.VehiclePlate).HasMaxLength(20);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(150);
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.Severity).IsRequired().HasDefaultValue(2);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.IsActive, x.Document });
        builder.HasIndex(x => new { x.LicenseId, x.IsActive, x.VehiclePlate });
    }
}
