using CondotifyAPI.Domain.DTO.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Amenities;

public sealed class AmenityConfiguration : IEntityTypeConfiguration<AmenityDTO>
{
    public void Configure(EntityTypeBuilder<AmenityDTO> builder)
    {
        builder.ToTable("Amenities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.FeeAmount).HasColumnType("numeric(10,2)");
        builder.Property(x => x.FeeDescription).HasMaxLength(300);
        builder.Property(x => x.RequiresApproval).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.RequiresTermsAcceptance).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.TermsText).HasMaxLength(4000);
        builder.Property(x => x.MinAdvanceNoticeHours).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxAdvanceDays).IsRequired().HasDefaultValue(60);
        builder.Property(x => x.CancellationCutoffHours).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.License)
            .WithMany(x => x.Amenities)
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
    }
}

public sealed class AmenityScheduleSlotConfiguration : IEntityTypeConfiguration<AmenityScheduleSlotDTO>
{
    public void Configure(EntityTypeBuilder<AmenityScheduleSlotDTO> builder)
    {
        builder.ToTable("AmenityScheduleSlots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DayOfWeek).IsRequired();
        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.EndTime).IsRequired();
        builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.ScheduleSlots)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AmenityId, x.DayOfWeek });
    }
}

public sealed class AmenityBlackoutConfiguration : IEntityTypeConfiguration<AmenityBlackoutDTO>
{
    public void Configure(EntityTypeBuilder<AmenityBlackoutDTO> builder)
    {
        builder.ToTable("AmenityBlackouts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(300);

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.Blackouts)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AmenityId, x.StartDate, x.EndDate });
    }
}

public sealed class AmenityBookingConfiguration : IEntityTypeConfiguration<AmenityBookingDTO>
{
    public void Configure(EntityTypeBuilder<AmenityBookingDTO> builder)
    {
        builder.ToTable("AmenityBookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(300);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.Bookings)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Resident)
            .WithMany()
            .HasForeignKey(x => x.ResidentId)
            .OnDelete(DeleteBehavior.SetNull);

        /*
         * Restrict (not Cascade): a schedule slot must never be hard-deleted
         * while a booking still points to it, or booking history would be
         * silently destroyed. The controller only physically deletes a slot
         * once it has verified zero bookings reference it (see Task 9);
         * this FK is the DB-level safety net for that invariant.
         */
        builder.HasOne(x => x.Slot)
            .WithMany()
            .HasForeignKey(x => x.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        /*
         * Only one active (Pending or Confirmed) booking may occupy a given
         * slot on a given date. This is the concurrency safety net: two
         * simultaneous create requests race the application-level
         * availability check, but only one can win this index.
         */
        builder.HasIndex(x => new { x.AmenityId, x.SlotId, x.Date })
            .HasFilter("\"Status\" IN (0, 1)")
            .IsUnique();

        builder.HasIndex(x => new { x.LicenseId, x.UnitId, x.Date });
    }
}
