using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Services.Authorization;
using Condotify.Models;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class ResidentHomeSummaryTests
{
    private static ResidentAccessGrant Grant(Guid unitId) => new(Guid.NewGuid(), Guid.NewGuid(), new[] { unitId }, default, true);

    [Fact]
    public void PickupCount_RequiresRecipientCurrentUnitLicenseAndReceivedStatus()
    {
        var unit = Guid.NewGuid(); var grant = Grant(unit);
        var allowed = ResidentHomeController.DeliveriesForPickup(grant).Compile();
        var row = new DeliveryDTO { LicenseId = grant.LicenseId, RecipientResidentId = grant.ResidentId, UnitId = unit, Status = DeliveryStatusEnum.Received };
        Assert.True(allowed(row));
        row.Status = DeliveryStatusEnum.Delivered; Assert.False(allowed(row));
        row.Status = DeliveryStatusEnum.Received; row.UnitId = Guid.NewGuid(); Assert.False(allowed(row));
        row.UnitId = unit; row.RecipientResidentId = Guid.NewGuid(); Assert.False(allowed(row));
        row.RecipientResidentId = grant.ResidentId; row.LicenseId = Guid.NewGuid(); Assert.False(allowed(row));
    }

    [Fact]
    public void BookingsCount_ExcludesOtherResidentsFormerUnitsAndCancelledBookings()
    {
        var unit = Guid.NewGuid(); var grant = Grant(unit); var now = DateTime.UtcNow;
        var allowed = ResidentHomeController.UpcomingFor(grant, now).Compile();
        var row = new AmenityBookingDTO { LicenseId = grant.LicenseId, ResidentId = grant.ResidentId, UnitId = unit, Date = now.Date.AddDays(1), Status = AmenityBookingStatusEnum.Confirmed };
        Assert.True(allowed(row));
        row.Status = AmenityBookingStatusEnum.Cancelled; Assert.False(allowed(row));
        row.Status = AmenityBookingStatusEnum.Pending; row.UnitId = Guid.NewGuid(); Assert.False(allowed(row));
        row.UnitId = unit; row.ResidentId = Guid.NewGuid(); Assert.False(allowed(row));
        row.ResidentId = grant.ResidentId; row.Date = now.Date.AddDays(-2); Assert.False(allowed(row));
    }

    [Fact]
    public void VisitsToday_UsesLocalDayOverlapAndHostScope()
    {
        var grant = Grant(Guid.NewGuid()); var now = new DateTime(2026, 9, 7, 2, 0, 0, DateTimeKind.Utc);
        var (start, end) = CondotifyTime.UtcDay(now);
        var allowed = ResidentHomeController.VisitsForToday(grant, now).Compile();
        var row = new AccessVisitDTO { LicenseId = grant.LicenseId, HostResidentId = grant.ResidentId, ValidFrom = start, ValidTo = end, Status = AccessVisitStatusEnum.Scheduled };
        Assert.True(allowed(row));
        row.ValidFrom = end; Assert.False(allowed(row));
        row.ValidFrom = start; row.Status = AccessVisitStatusEnum.Canceled; Assert.False(allowed(row));
        row.Status = AccessVisitStatusEnum.Scheduled; row.HostResidentId = Guid.NewGuid(); Assert.False(allowed(row));
    }

    [Fact]
    public void PostgreSql_TranslatesLocalDayGroupingAndSummaryFiltersWithoutConnecting()
    {
        using var context = new DatabaseContext(new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=127.0.0.1;Database=translation_only;Username=unused;Password=unused").Options);
        var zone = CondotifyTime.TimeZoneId;
        var sql = context.AccessEventRecords.GroupBy(x => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(x.OccurredAt, zone).Date)
            .Select(x => new { Day = x.Key, Count = x.Count() }).ToQueryString();
        Assert.Contains("AT TIME ZONE", sql);
        var grant = Grant(Guid.NewGuid());
        Assert.Contains("WHERE", context.Deliveries.Where(ResidentHomeController.DeliveriesForPickup(grant)).ToQueryString());
        Assert.Contains("WHERE", context.AmenityBookings.Where(ResidentHomeController.UpcomingFor(grant, DateTime.UtcNow)).ToQueryString());
    }
}
