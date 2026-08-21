using CondotifyAPI.Services.AccessControl;

namespace CondotifyAPI.Tests;

public sealed class AccessControlDeviceTimeZoneTests
{
    private static readonly TimeZoneInfo Bahia = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Bahia",
        TimeSpan.FromHours(-3),
        "Bahia",
        "Bahia");

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ToLocal_ConvertsStoredUtcToDeviceWallClock(DateTimeKind kind)
    {
        var stored = DateTime.SpecifyKind(new DateTime(2026, 8, 19, 17, 46, 0), kind);

        var local = AccessControlDeviceTimeZone.ToLocal(stored, Bahia);

        Assert.Equal(new DateTime(2026, 8, 19, 14, 46, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void ControlIdNtpTimeZone_UsesConfiguredUtcOffset()
    {
        var value = AccessControlDeviceTimeZone.ControlIdNtpTimeZone(
            new DateTime(2026, 8, 19, 17, 46, 0, DateTimeKind.Utc),
            Bahia);

        Assert.Equal("UTC-3", value);
    }

    [Fact]
    public void ToControlIdUnix_ShiftsStoredUtcToDeviceWallClockEpoch()
    {
        var stored = new DateTime(2026, 8, 19, 18, 16, 6, DateTimeKind.Utc);

        var timestamp = AccessControlDeviceTimeZone.ToControlIdUnix(stored, Bahia);

        Assert.Equal(1787152566, timestamp);
        Assert.Equal(
            new DateTime(2026, 8, 19, 15, 16, 6, DateTimeKind.Utc),
            DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime);
    }

    [Fact]
    public void FromControlIdUnix_RestoresActualUtcFromDeviceWallClockEpoch()
    {
        const long timestamp = 1787152566;

        var utc = AccessControlDeviceTimeZone.FromControlIdUnix(timestamp, Bahia);

        Assert.Equal(new DateTime(2026, 8, 19, 18, 16, 6, DateTimeKind.Utc), utc);
    }

    [Fact]
    public void Resolve_AcceptsIanaBahiaTimeZone()
    {
        var zone = AccessControlDeviceTimeZone.Resolve("America/Bahia");

        Assert.Equal(TimeSpan.FromHours(-3), zone.GetUtcOffset(
            new DateTime(2026, 8, 19, 17, 46, 0, DateTimeKind.Utc)));
    }
}
