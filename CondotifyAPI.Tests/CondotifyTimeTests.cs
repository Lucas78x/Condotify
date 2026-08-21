using Condotify.Models;

namespace CondotifyAPI.Tests;

public sealed class CondotifyTimeTests
{
    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToCondotifyTime_ConvertsUtcInstantToBahiaWallClock(DateTimeKind kind)
    {
        var utc = DateTime.SpecifyKind(new DateTime(2026, 8, 19, 20, 44, 0), kind);

        var local = utc.ToCondotifyTime();

        Assert.Equal(new DateTime(2026, 8, 19, 17, 44, 0), local);
    }

    [Fact]
    public void ToCondotifyTime_MovesEarlyUtcInstantToPreviousCalendarDay()
    {
        var utc = new DateTime(2026, 8, 21, 2, 30, 0, DateTimeKind.Utc);

        var local = utc.ToCondotifyTime();

        Assert.Equal(new DateTime(2026, 8, 20, 23, 30, 0), local);
    }

    [Fact]
    public void ToCondotifyTime_KeepsCreationAfterLocalMidnightOnCurrentDay()
    {
        var utc = new DateTime(2026, 8, 21, 3, 30, 0, DateTimeKind.Utc);

        var local = utc.ToCondotifyTime();

        Assert.Equal(new DateTime(2026, 8, 21, 0, 30, 0), local);
    }

    [Fact]
    public void ToCondotifyUtc_ConvertsUnspecifiedBahiaWallClockToUtc()
    {
        var local = new DateTime(2026, 8, 21, 10, 15, 0, DateTimeKind.Unspecified);

        var utc = local.ToCondotifyUtc();

        Assert.Equal(new DateTime(2026, 8, 21, 13, 15, 0, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void CondotifyConversion_RoundTripsWallClockTime()
    {
        var local = new DateTime(2026, 8, 21, 0, 30, 0, DateTimeKind.Unspecified);

        var roundTrip = local.ToCondotifyUtc().ToCondotifyTime();

        Assert.Equal(local, roundTrip);
    }
}
