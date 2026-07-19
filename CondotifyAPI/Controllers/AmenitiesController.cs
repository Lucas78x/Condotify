using System.Security.Claims;
using CondotifyAPI.Data.Amenities;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/amenities")]
[RequireLicensePermission(LicensePermissionEnum.ViewBookings)]
public sealed class AmenitiesController : ControllerBase
{
    private readonly DatabaseContext _context;

    public AmenitiesController(DatabaseContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAmenities(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenities = await AmenityQuery(licenseId)
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(amenities.Select(ToOut));
    }

    [HttpGet("unit-search")]
    public async Task<IActionResult> SearchUnits(Guid licenseId, [FromQuery] string query)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Ok(Array.Empty<AmenityUnitSearchOut>());

        var normalized = query.Trim();

        var units = await _context.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.Residents)
            .Where(x => x.Block.LicenseId == licenseId &&
                (x.Number.Contains(normalized) ||
                 x.Residents.Any(r => r.Name.Contains(normalized))))
            .OrderBy(x => x.Block.Name)
            .ThenBy(x => x.Number)
            .Take(15)
            .ToListAsync();

        return Ok(units.Select(x => new AmenityUnitSearchOut
        {
            UnitId = x.Id,
            UnitNumber = x.Number,
            BlockName = x.Block.Name,

            Residents = x.Residents
                .Where(r => r.IsActive)
                .Select(r => new AmenityUnitSearchResidentOut { ResidentId = r.Id, Name = r.Name })
                .ToList()
        }));
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> CreateAmenity(Guid licenseId, [FromBody] SaveAmenityIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = Validate(input);
        if (validation is not null)
            return BadRequest(new { Result = "InvalidAmenity", Errors = validation });

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.Amenities
            .AsNoTracking()
            .AnyAsync(x => x.LicenseId == licenseId && x.Name == normalizedName);

        if (duplicateExists)
            return Conflict(new { Result = "DuplicateAmenity", Errors = "Ja existe um local com este nome." });

        var now = DateTime.UtcNow;
        var amenity = new AmenityDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyAmenity(amenity, input);
        amenity.ScheduleSlots = input.ScheduleSlots.Select(x => NewSlot(amenity.Id, x)).ToList();
        amenity.Blackouts = input.Blackouts.Select(x => NewBlackout(amenity.Id, x)).ToList();

        _context.Amenities.Add(amenity);
        await _context.SaveChangesAsync();

        var created = await AmenityQuery(licenseId).AsNoTracking().FirstAsync(x => x.Id == amenity.Id);
        return Created(string.Empty, ToOut(created));
    }

    [HttpPut("{amenityId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> UpdateAmenity(Guid licenseId, Guid amenityId, [FromBody] SaveAmenityIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = Validate(input);
        if (validation is not null)
            return BadRequest(new { Result = "InvalidAmenity", Errors = validation });

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.Amenities
            .AsNoTracking()
            .AnyAsync(x => x.LicenseId == licenseId && x.Id != amenityId && x.Name == normalizedName);

        if (duplicateExists)
            return Conflict(new { Result = "DuplicateAmenity", Errors = "Ja existe um local com este nome." });

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        ApplyAmenity(amenity, input);
        amenity.UpdatedAt = DateTime.UtcNow;

        await ReplaceScheduleSlotsAsync(amenity, input.ScheduleSlots);
        ReplaceBlackouts(amenity, input.Blackouts);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new
            {
                Result = "SlotHasBookings",
                Errors = "Um dos horarios removidos possui agendamentos e nao pode ser excluido. Ele foi mantido como inativo."
            });
        }

        var updated = await AmenityQuery(licenseId).AsNoTracking().FirstAsync(x => x.Id == amenityId);
        return Ok(ToOut(updated));
    }

    [HttpDelete("{amenityId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> DeleteAmenity(Guid licenseId, Guid amenityId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        _context.Amenities.Remove(amenity);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<AmenityDTO> AmenityQuery(Guid licenseId)
    {
        return _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .Where(x => x.LicenseId == licenseId);
    }

    private static string? Validate(SaveAmenityIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Informe o nome do local.";

        if (input.Name.Trim().Length > 120)
            return "O nome do local deve ter ate 120 caracteres.";

        if (input.Capacity is < 1)
            return "A capacidade deve ser maior que zero.";

        if (input.MonthlyLimitPerUnit is < 0)
            return "O limite mensal por unidade nao pode ser negativo.";

        if (input.MinAdvanceNoticeHours < 0 || input.CancellationCutoffHours < 0)
            return "Os prazos de antecedencia e cancelamento nao podem ser negativos.";

        if (input.MaxAdvanceDays < 1)
            return "A janela maxima de agendamento deve ser de ao menos 1 dia.";

        if (input.RequiresTermsAcceptance && string.IsNullOrWhiteSpace(input.TermsText))
            return "Informe o texto do termo de uso.";

        foreach (var slot in input.ScheduleSlots)
        {
            if (slot.StartTime < TimeSpan.Zero || slot.EndTime > TimeSpan.FromDays(1) || slot.StartTime >= slot.EndTime)
                return "Existe um horario invalido na grade semanal.";
        }

        foreach (var blackout in input.Blackouts)
        {
            if (blackout.EndDate.Date < blackout.StartDate.Date)
                return "Existe um bloqueio de data invalido.";
        }

        return null;
    }

    private static void ApplyAmenity(AmenityDTO amenity, SaveAmenityIn input)
    {
        amenity.Name = input.Name.Trim();
        amenity.Description = input.Description?.Trim() ?? string.Empty;
        amenity.Capacity = input.Capacity;
        amenity.Active = input.Active;
        amenity.FeeAmount = input.FeeAmount;
        amenity.FeeDescription = input.FeeDescription?.Trim() ?? string.Empty;
        amenity.RequiresApproval = input.RequiresApproval;
        amenity.RequiresTermsAcceptance = input.RequiresTermsAcceptance;
        amenity.TermsText = input.TermsText?.Trim() ?? string.Empty;
        amenity.MonthlyLimitPerUnit = input.MonthlyLimitPerUnit;
        amenity.MinAdvanceNoticeHours = input.MinAdvanceNoticeHours;
        amenity.MaxAdvanceDays = input.MaxAdvanceDays;
        amenity.CancellationCutoffHours = input.CancellationCutoffHours;
    }

    private static AmenityScheduleSlotDTO NewSlot(Guid amenityId, SaveAmenityScheduleSlotIn input) => new()
    {
        Id = Guid.NewGuid(),
        AmenityId = amenityId,
        DayOfWeek = input.DayOfWeek,
        StartTime = input.StartTime,
        EndTime = input.EndTime,
        Active = input.Active
    };

    private static AmenityBlackoutDTO NewBlackout(Guid amenityId, SaveAmenityBlackoutIn input) => new()
    {
        Id = Guid.NewGuid(),
        AmenityId = amenityId,
        StartDate = input.StartDate.Date,
        EndDate = input.EndDate.Date,
        Reason = input.Reason?.Trim() ?? string.Empty
    };

    /// <summary>
    /// Upserts schedule slots by Id. A slot removed from the payload is
    /// hard-deleted only when it has zero bookings ever; otherwise it is
    /// kept but marked inactive, so booking history never loses its
    /// referenced slot (see the Restrict FK in AmenityBookingConfiguration).
    /// </summary>
    private async Task ReplaceScheduleSlotsAsync(AmenityDTO amenity, List<SaveAmenityScheduleSlotIn> incoming)
    {
        var incomingIds = incoming.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();

        foreach (var existing in amenity.ScheduleSlots.ToList())
        {
            if (incomingIds.Contains(existing.Id))
                continue;

            var hasBookings = await _context.AmenityBookings.AsNoTracking().AnyAsync(x => x.SlotId == existing.Id);
            if (hasBookings)
                existing.Active = false;
            else
                amenity.ScheduleSlots.Remove(existing);
        }

        foreach (var input in incoming)
        {
            if (input.Id.HasValue)
            {
                var existing = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.Id.Value);
                if (existing is null) continue;
                existing.DayOfWeek = input.DayOfWeek;
                existing.StartTime = input.StartTime;
                existing.EndTime = input.EndTime;
                existing.Active = input.Active;
            }
            else
            {
                amenity.ScheduleSlots.Add(NewSlot(amenity.Id, input));
            }
        }
    }

    private static void ReplaceBlackouts(AmenityDTO amenity, List<SaveAmenityBlackoutIn> incoming)
    {
        var incomingIds = incoming.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();

        foreach (var existing in amenity.Blackouts.Where(x => !incomingIds.Contains(x.Id)).ToList())
            amenity.Blackouts.Remove(existing);

        foreach (var input in incoming)
        {
            if (input.Id.HasValue)
            {
                var existing = amenity.Blackouts.FirstOrDefault(x => x.Id == input.Id.Value);
                if (existing is null) continue;
                existing.StartDate = input.StartDate.Date;
                existing.EndDate = input.EndDate.Date;
                existing.Reason = input.Reason?.Trim() ?? string.Empty;
            }
            else
            {
                amenity.Blackouts.Add(NewBlackout(amenity.Id, input));
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static AmenityOut ToOut(AmenityDTO amenity) => new()
    {
        Id = amenity.Id,
        Name = amenity.Name,
        Description = amenity.Description,
        Capacity = amenity.Capacity,
        Active = amenity.Active,
        FeeAmount = amenity.FeeAmount,
        FeeDescription = amenity.FeeDescription,
        RequiresApproval = amenity.RequiresApproval,
        RequiresTermsAcceptance = amenity.RequiresTermsAcceptance,
        TermsText = amenity.TermsText,
        MonthlyLimitPerUnit = amenity.MonthlyLimitPerUnit,
        MinAdvanceNoticeHours = amenity.MinAdvanceNoticeHours,
        MaxAdvanceDays = amenity.MaxAdvanceDays,
        CancellationCutoffHours = amenity.CancellationCutoffHours,

        ScheduleSlots = amenity.ScheduleSlots
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new AmenityScheduleSlotOut { Id = x.Id, DayOfWeek = x.DayOfWeek, StartTime = x.StartTime, EndTime = x.EndTime, Active = x.Active })
            .ToList(),

        Blackouts = amenity.Blackouts
            .OrderBy(x => x.StartDate)
            .Select(x => new AmenityBlackoutOut { Id = x.Id, StartDate = x.StartDate, EndDate = x.EndDate, Reason = x.Reason })
            .ToList()
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
