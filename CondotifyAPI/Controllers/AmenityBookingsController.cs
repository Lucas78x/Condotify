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

        var rangeStart = AsUtcDate(from ?? DateTime.UtcNow);
        var rangeEnd = AsUtcDate(to ?? rangeStart.AddDays(30));

        var bookings = await BookingQuery(licenseId, amenityId)
            .AsNoTracking()
            .Where(x => x.Date >= rangeStart && x.Date <= rangeEnd)
            .OrderBy(x => x.Date).ThenBy(x => x.Slot.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToOut));
    }

    [HttpGet("~/api/access/licenses/{licenseId:guid}/amenity-bookings")]
    public async Task<IActionResult> GetLicenseBookings(
        Guid licenseId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] Guid? amenityId,
        [FromQuery] string? status,
        [FromQuery] string? search)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var rangeStart = AsUtcDate(from ?? DateTime.UtcNow.AddMonths(-1));
        var rangeEnd = AsUtcDate(to ?? DateTime.UtcNow.AddMonths(2));
        if (rangeEnd < rangeStart || rangeEnd > rangeStart.AddYears(1))
            return BadRequest(new { Result = "InvalidPeriod", Errors = "Informe um periodo valido de ate 12 meses." });

        var query = BookingQuery(licenseId, amenityId)
            .AsNoTracking()
            .Where(x => x.Date >= rangeStart && x.Date <= rangeEnd);

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<AmenityBookingStatusEnum>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                EF.Functions.ILike(x.Amenity.Name, $"%{term}%") ||
                EF.Functions.ILike(x.Unit.Number, $"%{term}%") ||
                EF.Functions.ILike(x.Unit.Block.Name, $"%{term}%") ||
                (x.Resident != null && EF.Functions.ILike(x.Resident.Name, $"%{term}%")) ||
                EF.Functions.ILike(x.Notes, $"%{term}%"));
        }

        var bookings = await query
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Slot.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToOut));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(
        Guid licenseId,
        Guid amenityId,
        [FromQuery] DateTime date,
        [FromQuery] Guid? excludeBookingId)
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

        var day = AsUtcDate(date);

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
                (!excludeBookingId.HasValue || x.Id != excludeBookingId.Value) &&
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
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
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

        if (!await ResidentBelongsToUnitAsync(input.ResidentId, input.UnitId))
            return BadRequest(new { Result = "InvalidResident", Errors = "O morador informado nao pertence a unidade selecionada." });

        var bookingDate = AsUtcDate(input.Date);

        var slot = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.SlotId && x.Active);
        if (slot is null || slot.DayOfWeek != bookingDate.DayOfWeek)
            return BadRequest(new { Result = "InvalidSlot", Errors = "O horario selecionado nao existe para este local nesta data." });

        var now = DateTime.UtcNow;

        var windowError = AmenityBookingValidator.ValidateWindow(amenity, bookingDate, slot.StartTime, now);
        if (windowError is not null)
            return BadRequest(new { Result = "OutsideBookingWindow", Errors = windowError });

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, bookingDate))
            return BadRequest(new { Result = "DateBlacked", Errors = "Esta data nao esta disponivel para este local." });

        var monthStart = new DateTime(bookingDate.Year, bookingDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
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
            .AnyAsync(x => x.AmenityId == amenityId && x.SlotId == input.SlotId && x.Date == bookingDate &&
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
            Date = bookingDate,
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

    [HttpPut("{bookingId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> UpdateBooking(
        Guid licenseId,
        Guid amenityId,
        Guid bookingId,
        [FromBody] CreateAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);
        var booking = await _context.AmenityBookings
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (amenity is null || booking is null)
            return NotFound();

        if (booking.Status is AmenityBookingStatusEnum.Cancelled or AmenityBookingStatusEnum.Rejected or AmenityBookingStatusEnum.Completed)
            return Conflict(new { Result = "BookingClosed", Errors = "Agendamentos encerrados nao podem ser editados." });

        if (!amenity.Active)
            return BadRequest(new { Result = "AmenityInactive", Errors = "Este local nao esta disponivel para agendamento." });

        var unitExists = await _context.Units.AsNoTracking()
            .AnyAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId);
        if (!unitExists)
            return BadRequest(new { Result = "InvalidUnit", Errors = "A unidade informada nao pertence a esta licenca." });

        if (!await ResidentBelongsToUnitAsync(input.ResidentId, input.UnitId))
            return BadRequest(new { Result = "InvalidResident", Errors = "O morador informado nao pertence a unidade selecionada." });

        var bookingDate = AsUtcDate(input.Date);
        var slot = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.SlotId && x.Active);
        if (slot is null || slot.DayOfWeek != bookingDate.DayOfWeek)
            return BadRequest(new { Result = "InvalidSlot", Errors = "O horario selecionado nao existe para este local nesta data." });

        var now = DateTime.UtcNow;
        var windowError = AmenityBookingValidator.ValidateWindow(amenity, bookingDate, slot.StartTime, now);
        if (windowError is not null)
            return BadRequest(new { Result = "OutsideBookingWindow", Errors = windowError });

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, bookingDate))
            return BadRequest(new { Result = "DateBlacked", Errors = "Esta data nao esta disponivel para este local." });

        var monthStart = new DateTime(bookingDate.Year, bookingDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var existingThisMonth = await _context.AmenityBookings
            .AsNoTracking()
            .CountAsync(x => x.Id != bookingId && x.AmenityId == amenityId && x.UnitId == input.UnitId &&
                x.Date >= monthStart && x.Date < monthEnd &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));
        if (AmenityBookingValidator.HasReachedMonthlyLimit(amenity.MonthlyLimitPerUnit, existingThisMonth))
            return BadRequest(new { Result = "MonthlyLimitReached", Errors = "Esta unidade ja atingiu o limite mensal de agendamentos para este local." });

        if (amenity.RequiresTermsAcceptance && booking.TermsAcceptedAt is null && !input.TermsAccepted)
            return BadRequest(new { Result = "TermsNotAccepted", Errors = "E necessario aceitar o termo de uso para agendar este local." });

        var slotTaken = await _context.AmenityBookings
            .AsNoTracking()
            .AnyAsync(x => x.Id != bookingId && x.AmenityId == amenityId && x.SlotId == input.SlotId && x.Date == bookingDate &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));
        if (slotTaken)
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });

        var scheduleChanged = booking.Date != bookingDate ||
                              booking.SlotId != input.SlotId ||
                              booking.UnitId != input.UnitId;

        booking.UnitId = input.UnitId;
        booking.ResidentId = input.ResidentId;
        booking.Date = bookingDate;
        booking.SlotId = input.SlotId;
        booking.Notes = input.Notes?.Trim() ?? string.Empty;
        booking.TermsAcceptedAt ??= amenity.RequiresTermsAcceptance ? now : null;
        if (scheduleChanged && amenity.RequiresApproval)
            booking.Status = AmenityBookingStatusEnum.Pending;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });
        }

        var updated = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == bookingId);
        return Ok(ToOut(updated));
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
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
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

    private IQueryable<AmenityBookingDTO> BookingQuery(Guid licenseId, Guid? amenityId = null)
    {
        var query = _context.AmenityBookings
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Include(x => x.Slot)
            .Include(x => x.Amenity)
            .Where(x => x.LicenseId == licenseId);

        return amenityId.HasValue
            ? query.Where(x => x.AmenityId == amenityId.Value)
            : query;
    }

    private async Task<bool> ResidentBelongsToUnitAsync(Guid? residentId, Guid unitId) =>
        !residentId.HasValue ||
        await _context.Residents.AsNoTracking()
            .AnyAsync(x => x.Id == residentId.Value &&
                (x.UnitId == unitId || x.UnitLinks.Any(link => link.UnitId == unitId && link.IsActive)));

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    /// <summary>
    /// Client-supplied dates (from [FromQuery] binding or a JSON request body without
    /// an explicit UTC offset) deserialize with Kind=Unspecified. The Date column maps
    /// to Postgres 'timestamp with time zone', and Npgsql rejects Kind=Unspecified
    /// values for that type. These are date-only values with no meaningful timezone,
    /// so SpecifyKind (not ToUniversalTime) is correct: it reinterprets the same
    /// wall-clock date as UTC without shifting it.
    /// </summary>
    private static DateTime AsUtcDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

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
