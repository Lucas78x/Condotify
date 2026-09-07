using System.Linq.Expressions;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/home")]
public sealed class ResidentHomeController(DatabaseContext context, IResidentAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        var modules = await context.Licenses.AsNoTracking().Where(x => x.Id == grant.LicenseId)
            .Select(x => (long?)x.EnabledModules).SingleOrDefaultAsync(cancellationToken);
        if (!modules.HasValue) return Forbid();
        var now = DateTime.UtcNow;
        var result = new ResidentHomeSummaryViewModel { GeneratedAt = now, EnabledModules = modules.Value };
        if (grant.UnitIds.Count == 0) return Ok(result);
        bool Enabled(LicenseModuleEnum module) => (modules.Value & (long)module) != 0;

        result.VisitsToday = await context.AccessVisits.AsNoTracking().CountAsync(VisitsForToday(grant, now), cancellationToken);
        if (Enabled(LicenseModuleEnum.Deliveries))
            result.DeliveriesAwaitingPickup = await context.Deliveries.AsNoTracking().CountAsync(DeliveriesForPickup(grant), cancellationToken);
        if (Enabled(LicenseModuleEnum.Bookings))
        {
            var bookings = context.AmenityBookings.AsNoTracking().Where(UpcomingFor(grant, now));
            result.UpcomingBookings = await bookings.CountAsync(cancellationToken);
            var next = await bookings.OrderBy(x => x.Date).ThenBy(x => x.Slot.StartTime)
                .Select(x => new { x.Date, x.Amenity.Name }).FirstOrDefaultAsync(cancellationToken);
            result.NextBookingDate = next?.Date;
            result.NextBookingName = next?.Name;
        }
        if (Enabled(LicenseModuleEnum.Finance))
        {
            var charges = context.FinancialCharges.AsNoTracking().Where(ResidentFinancialController.IsVisibleTo(grant))
                .Where(x => x.Status == FinancialChargeStatusEnum.Open);
            result.OpenCharges = await charges.CountAsync(cancellationToken);
            result.NextChargeDueDate = await charges.Select(x => (DateTime?)x.DueDate).MinAsync(cancellationToken);
        }
        return Ok(result);
    }

    internal static Expression<Func<DeliveryDTO, bool>> DeliveriesForPickup(ResidentAccessGrant grant) =>
        x => x.LicenseId == grant.LicenseId && x.RecipientResidentId == grant.ResidentId &&
             x.UnitId.HasValue && grant.UnitIds.Contains(x.UnitId.Value) && x.Status == DeliveryStatusEnum.Received;

    internal static Expression<Func<AccessVisitDTO, bool>> VisitsForToday(ResidentAccessGrant grant, DateTime now)
    {
        var (start, end) = CondotifyTime.UtcDay(now);
        return x => x.LicenseId == grant.LicenseId && x.HostResidentId == grant.ResidentId &&
            x.ValidFrom < end && x.ValidTo > start &&
            (x.Status == AccessVisitStatusEnum.Scheduled || x.Status == AccessVisitStatusEnum.CheckedIn ||
             x.Status == AccessVisitStatusEnum.PendingApproval || x.Status == AccessVisitStatusEnum.PendingEnrollment);
    }

    internal static Expression<Func<AmenityBookingDTO, bool>> UpcomingFor(ResidentAccessGrant grant, DateTime now)
    {
        // Bookings use a calendar date stored with UTC kind, rather than an instant.
        var today = DateTime.SpecifyKind(now.ToCondotifyTime().Date, DateTimeKind.Utc);
        return x => x.LicenseId == grant.LicenseId && x.ResidentId == grant.ResidentId && grant.UnitIds.Contains(x.UnitId) &&
            x.Date >= today && (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed);
    }
}
