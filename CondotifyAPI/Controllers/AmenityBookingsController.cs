using System.Security.Claims;
using CondotifyAPI.Data.Amenities;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Amenities;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/amenities/{amenityId:guid}/bookings")]
[RequireLicensePermission(LicensePermissionEnum.ViewBookings)]
public sealed class AmenityBookingsController : ControllerBase
{
    private readonly DatabaseContext _context;

    public AmenityBookingsController(DatabaseContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetBookings(Guid licenseId, Guid amenityId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var rangeStart = (from ?? DateTime.UtcNow.Date).Date;
        var rangeEnd = (to ?? rangeStart.AddDays(30)).Date;

        var bookings = await BookingQuery(licenseId, amenityId)
            .AsNoTracking()
            .Where(x => x.Date >= rangeStart && x.Date <= rangeEnd)
            .OrderBy(x => x.Date).ThenBy(x => x.Slot.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToOut));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(Guid licenseId, Guid amenityId, [FromQuery] DateTime date)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .AsNoTracking()
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        var day = date.Date;

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, day))
            return Ok(Array.Empty<AmenitySlotAvailabilityOut>());

        var slots = amenity.ScheduleSlots
            .Where(x => x.Active && x.DayOfWeek == day.DayOfWeek)
            .OrderBy(x => x.StartTime)
            .ToList();

        var activeBookings = await _context.AmenityBookings
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => x.AmenityId == amenityId && x.Date == day &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed))
            .ToListAsync();

        return Ok(slots.Select(slot =>
        {
            var occupiedBy = activeBookings.FirstOrDefault(x => x.SlotId == slot.Id);
            return new AmenitySlotAvailabilityOut
            {
                SlotId = slot.Id,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Available = occupiedBy is null,
                OccupiedByUnitNumber = occupiedBy?.Unit.Number,
                OccupiedStatus = occupiedBy?.Status,
                BookingId = occupiedBy?.Id
            };
        }));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking(Guid licenseId, Guid amenityId, [FromBody] CreateAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        if (!amenity.Active)
            return BadRequest(new { Result = "AmenityInactive", Errors = "Este local nao esta disponivel para agendamento." });

        var unitExists = await _context.Units.AsNoTracking().AnyAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId);
        if (!unitExists)
            return BadRequest(new { Result = "InvalidUnit", Errors = "A unidade informada nao pertence a esta licenca." });

        var slot = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.SlotId && x.Active);
        if (slot is null || slot.DayOfWeek != input.Date.DayOfWeek)
            return BadRequest(new { Result = "InvalidSlot", Errors = "O horario selecionado nao existe para este local nesta data." });

        var now = DateTime.UtcNow;

        var windowError = AmenityBookingValidator.ValidateWindow(amenity, input.Date, now);
        if (windowError is not null)
            return BadRequest(new { Result = "OutsideBookingWindow", Errors = windowError });

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, input.Date))
            return BadRequest(new { Result = "DateBlacked", Errors = "Esta data nao esta disponivel para este local." });

        var monthStart = new DateTime(input.Date.Year, input.Date.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var existingThisMonth = await _context.AmenityBookings
            .AsNoTracking()
            .CountAsync(x => x.AmenityId == amenityId && x.UnitId == input.UnitId &&
                x.Date >= monthStart && x.Date < monthEnd &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));

        if (AmenityBookingValidator.HasReachedMonthlyLimit(amenity.MonthlyLimitPerUnit, existingThisMonth))
            return BadRequest(new { Result = "MonthlyLimitReached", Errors = "Esta unidade ja atingiu o limite mensal de agendamentos para este local." });

        if (amenity.RequiresTermsAcceptance && !input.TermsAccepted)
            return BadRequest(new { Result = "TermsNotAccepted", Errors = "E necessario aceitar o termo de uso para agendar este local." });

        var slotTaken = await _context.AmenityBookings
            .AsNoTracking()
            .AnyAsync(x => x.AmenityId == amenityId && x.SlotId == input.SlotId && x.Date == input.Date.Date &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));

        if (slotTaken)
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });

        var currentUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty;

        var booking = new AmenityBookingDTO
        {
            Id = Guid.NewGuid(),
            AmenityId = amenityId,
            LicenseId = licenseId,
            UnitId = input.UnitId,
            ResidentId = input.ResidentId,
            Date = input.Date.Date,
            SlotId = input.SlotId,
            Status = amenity.RequiresApproval ? AmenityBookingStatusEnum.Pending : AmenityBookingStatusEnum.Confirmed,
            TermsAcceptedAt = amenity.RequiresTermsAcceptance ? now : null,
            Notes = input.Notes?.Trim() ?? string.Empty,
            CreatedByUserId = currentUserId,
            CreatedAt = now
        };

        _context.AmenityBookings.Add(booking);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });
        }

        var created = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == booking.Id);
        return Created(string.Empty, ToOut(created));
    }

    [HttpPut("{bookingId:guid}/approve")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> ApproveBooking(Guid licenseId, Guid amenityId, Guid bookingId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status != AmenityBookingStatusEnum.Pending)
            return Conflict(new { Result = "NotPending", Errors = "Apenas agendamentos pendentes podem ser aprovados." });

        booking.Status = AmenityBookingStatusEnum.Confirmed;
        await _context.SaveChangesAsync();

        var updated = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == bookingId);
        return Ok(ToOut(updated));
    }

    [HttpPut("{bookingId:guid}/reject")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> RejectBooking(Guid licenseId, Guid amenityId, Guid bookingId, [FromBody] RejectAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status != AmenityBookingStatusEnum.Pending)
            return Conflict(new { Result = "NotPending", Errors = "Apenas agendamentos pendentes podem ser recusados." });

        booking.Status = AmenityBookingStatusEnum.Rejected;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = input.Reason?.Trim() ?? string.Empty;
        await _context.SaveChangesAsync();

        var updated = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == bookingId);
        return Ok(ToOut(updated));
    }

    [HttpDelete("{bookingId:guid}")]
    public async Task<IActionResult> CancelBooking(Guid licenseId, Guid amenityId, Guid bookingId, [FromBody] CancelAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .Include(x => x.Amenity)
            .Include(x => x.Slot)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status is AmenityBookingStatusEnum.Cancelled or AmenityBookingStatusEnum.Rejected or AmenityBookingStatusEnum.Completed)
            return Conflict(new { Result = "AlreadyClosed", Errors = "Este agendamento ja nao esta mais ativo." });

        if (!AmenityBookingValidator.CanCancel(booking.Amenity, booking.Date, booking.Slot.StartTime, DateTime.UtcNow))
        {
            return Conflict(new
            {
                Result = "PastCancellationCutoff",
                Errors = $"Este agendamento so pode ser cancelado ate {booking.Amenity.CancellationCutoffHours} hora(s) antes do horario."
            });
        }

        booking.Status = AmenityBookingStatusEnum.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = input.Reason?.Trim() ?? string.Empty;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<AmenityBookingDTO> BookingQuery(Guid licenseId, Guid amenityId)
    {
        return _context.AmenityBookings
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Include(x => x.Slot)
            .Where(x => x.LicenseId == licenseId && x.AmenityId == amenityId);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static AmenityBookingOut ToOut(AmenityBookingDTO booking) => new()
    {
        Id = booking.Id,
        AmenityId = booking.AmenityId,
        AmenityName = booking.Amenity?.Name ?? string.Empty,
        UnitId = booking.UnitId,
        UnitNumber = booking.Unit.Number,
        BlockName = booking.Unit.Block.Name,
        ResidentId = booking.ResidentId,
        ResidentName = booking.Resident?.Name,
        Date = booking.Date,
        SlotId = booking.SlotId,
        SlotStartTime = booking.Slot.StartTime,
        SlotEndTime = booking.Slot.EndTime,
        Status = booking.Status.ToString(),
        TermsAcceptedAt = booking.TermsAcceptedAt,
        Notes = booking.Notes,
        CreatedAt = booking.CreatedAt,
        CancelledAt = booking.CancelledAt,
        CancelReason = booking.CancelReason
    };

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return false;

        return await _context.Licenses
            .AsNoTracking()
            .AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
