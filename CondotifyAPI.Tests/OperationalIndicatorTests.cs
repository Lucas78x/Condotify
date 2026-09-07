using Condotify.Models;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Services;

namespace CondotifyAPI.Tests;

public sealed class OperationalIndicatorTests
{
    [Fact]
    public void DailyDeniedCount_DoesNotDependOnRecentFeedLimit()
    {
        var licenseId = Guid.NewGuid();
        var now = new DateTime(2026, 9, 7, 14, 0, 0, DateTimeKind.Utc);
        var events = Enumerable.Range(0, 120).Select(index => new AccessEventRecordDTO
        {
            LicenseId = licenseId, Authorized = false, OccurredAt = now.AddMinutes(-index)
        }).ToList();

        Assert.Equal(120, events.Count(ConciergeController.DeniedDuring(licenseId, now).Compile()));
        Assert.Equal(80, events.OrderByDescending(x => x.OccurredAt).Take(80).Count());
    }

    [Fact]
    public void DailyDeniedCount_UsesLocalDayAndExcludesOtherCondominiums()
    {
        var licenseId = Guid.NewGuid();
        var now = new DateTime(2026, 9, 7, 2, 30, 0, DateTimeKind.Utc);
        var (start, end) = CondotifyTime.UtcDay(now);
        var includes = ConciergeController.DeniedDuring(licenseId, now).Compile();

        Assert.Equal(new DateTime(2026, 9, 6, 3, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc), end);
        Assert.True(includes(new() { LicenseId = licenseId, OccurredAt = start }));
        Assert.True(includes(new() { LicenseId = licenseId, OccurredAt = end.AddTicks(-1) }));
        Assert.False(includes(new() { LicenseId = licenseId, OccurredAt = start.AddTicks(-1) }));
        Assert.False(includes(new() { LicenseId = licenseId, OccurredAt = end }));
        Assert.False(includes(new() { LicenseId = Guid.NewGuid(), OccurredAt = now }));
        Assert.False(includes(new() { LicenseId = licenseId, OccurredAt = now, Authorized = true }));
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 5, true)]
    [InlineData(true, 6, false)]
    [InlineData(false, 0, false)]
    [InlineData(true, null, false)]
    public void OnlineHealth_RequiresARecentSuccessfulObservation(bool active, int? minutesAgo, bool expected)
    {
        var now = new DateTime(2026, 9, 7, 14, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, DeviceHealthPolicy.IsOnline(active, minutesAgo.HasValue ? now.AddMinutes(-minutesAgo.Value) : null, now));
    }
}
