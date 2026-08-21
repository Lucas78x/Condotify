using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Services.Amenities;
using Xunit;

namespace CondotifyAPI.Tests;

public class AmenityBookingValidatorTests
{
    private static AmenityDTO Amenity(int minAdvanceHours = 0, int maxAdvanceDays = 60, int cancellationCutoffHours = 0) => new()
    {
        MinAdvanceNoticeHours = minAdvanceHours,
        MaxAdvanceDays = maxAdvanceDays,
        CancellationCutoffHours = cancellationCutoffHours
    };

    [Fact]
    public void ValidateWindow_ShouldReject_WhenBelowMinimumAdvanceNotice()
    {
        var amenity = Amenity(minAdvanceHours: 24);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, new TimeSpan(8, 0, 0), now);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateWindow_ShouldAccept_WhenAtOrBeyondMinimumAdvanceNotice()
    {
        var amenity = Amenity(minAdvanceHours: 24);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, new TimeSpan(10, 0, 0), now);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateWindow_ShouldReject_WhenBeyondMaximumAdvanceDays()
    {
        var amenity = Amenity(maxAdvanceDays: 30);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, new TimeSpan(10, 0, 0), now);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateWindow_ShouldReject_WhenDateIsInThePast()
    {
        var amenity = Amenity();
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, new TimeSpan(10, 0, 0), now);

        Assert.NotNull(error);
    }

    [Fact]
    public void IsDateBlacked_ShouldReturnTrue_WhenDateFallsInsideARange()
    {
        var blackouts = new List<AmenityBlackoutDTO>
        {
            new() { StartDate = new DateTime(2026, 12, 24), EndDate = new DateTime(2026, 12, 26) }
        };

        Assert.True(AmenityBookingValidator.IsDateBlacked(blackouts, new DateTime(2026, 12, 25)));
        Assert.False(AmenityBookingValidator.IsDateBlacked(blackouts, new DateTime(2026, 12, 27)));
    }

    [Theory]
    [InlineData(null, 5, false)]
    [InlineData(0, 5, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)]
    public void HasReachedMonthlyLimit_ShouldCompareAgainstConfiguredLimit(int? limit, int existingCount, bool expected)
    {
        Assert.Equal(expected, AmenityBookingValidator.HasReachedMonthlyLimit(limit, existingCount));
    }

    [Fact]
    public void CanCancel_ShouldReturnFalse_WhenInsideCutoffWindow()
    {
        var amenity = Amenity(cancellationCutoffHours: 24);
        var now = new DateTime(2026, 7, 19, 13, 0, 0, DateTimeKind.Utc); // 10:00 in Bahia
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var slotStart = new TimeSpan(8, 0, 0);

        Assert.False(AmenityBookingValidator.CanCancel(amenity, date, slotStart, now));
    }

    [Fact]
    public void CanCancel_ShouldReturnTrue_WhenBeforeCutoffWindow()
    {
        var amenity = Amenity(cancellationCutoffHours: 24);
        var now = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var slotStart = new TimeSpan(8, 0, 0);

        Assert.True(AmenityBookingValidator.CanCancel(amenity, date, slotStart, now));
    }

    [Fact]
    public void ValidateWindow_ShouldUseSlotStartInsteadOfEndOfDay()
    {
        var amenity = Amenity(minAdvanceHours: 12);
        var now = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc); // 18/07 21:00 in Bahia
        var date = new DateTime(2026, 7, 19, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, new TimeSpan(8, 0, 0), now);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateWindow_ShouldUseBahiaWallClockNearUtcMidnight()
    {
        var amenity = Amenity();
        var now = new DateTime(2026, 8, 21, 1, 0, 0, DateTimeKind.Utc); // 20/08 22:00 in Bahia
        var localDate = new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, localDate, new TimeSpan(23, 0, 0), now);

        Assert.Null(error);
    }

    [Fact]
    public void HasOverlappingSlots_ShouldIgnoreAdjacentAndInactiveSlots()
    {
        var slots = new[]
        {
            new AmenityScheduleSlotDTO { DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0, 0), EndTime = new(10, 0, 0), Active = true },
            new AmenityScheduleSlotDTO { DayOfWeek = DayOfWeek.Monday, StartTime = new(10, 0, 0), EndTime = new(12, 0, 0), Active = true },
            new AmenityScheduleSlotDTO { DayOfWeek = DayOfWeek.Monday, StartTime = new(9, 0, 0), EndTime = new(11, 0, 0), Active = false }
        };

        Assert.False(AmenityBookingValidator.HasOverlappingSlots(slots));
    }

    [Fact]
    public void HasOverlappingSlots_ShouldRejectActiveOverlapOnSameDay()
    {
        var slots = new[]
        {
            new AmenityScheduleSlotDTO { DayOfWeek = DayOfWeek.Monday, StartTime = new(8, 0, 0), EndTime = new(10, 0, 0), Active = true },
            new AmenityScheduleSlotDTO { DayOfWeek = DayOfWeek.Monday, StartTime = new(9, 30, 0), EndTime = new(11, 0, 0), Active = true }
        };

        Assert.True(AmenityBookingValidator.HasOverlappingSlots(slots));
    }
}
