using System.Security.Cryptography;
using System.Text.Json;
using CondotifyAPI.Data.Login;
using CondotifyAPI.Data.Amenities;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Data.Deliveries;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Amenities;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident")]
public sealed class ResidentProfileController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IResidentAuthorizationService _authorization;

    public ResidentProfileController(DatabaseContext context, IResidentAuthorizationService authorization)
    {
        _context = context;
        _authorization = authorization;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null)
            return Forbid();

        var resident = await _context.Residents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == grant.ResidentId, cancellationToken);
        if (resident is null)
            return NotFound();

        var links = await _context.ResidentUnitLinks.AsNoTracking()
            .Include(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
            .Where(x => x.ResidentId == grant.ResidentId && grant.UnitIds.Contains(x.UnitId))
            .OrderByDescending(x => x.IsPrimary)
            .ThenBy(x => x.Unit.Block.Name)
            .ThenBy(x => x.Unit.Number)
            .ToListAsync(cancellationToken);

        var licenseName = links.Select(x => x.Unit.Block.License.Name).FirstOrDefault()
            ?? await _context.Licenses.AsNoTracking()
                .Where(x => x.Id == grant.LicenseId)
                .Select(x => x.Name)
                .FirstOrDefaultAsync(cancellationToken)
            ?? string.Empty;

        return Ok(new ResidentMeOut
        {
            ResidentId = resident.Id,
            LicenseId = grant.LicenseId,
            LicenseName = licenseName,
            Name = resident.Name,
            Email = resident.Email,
            PhoneNumber = resident.PhoneNumber,
            PhotoUrl = resident.ImgUrl,
            AccessType = grant.AccessType,
            IsResponsible = grant.IsResponsible,
            Units = links.Select(x => new ResidentUnitOut
            {
                UnitId = x.UnitId,
                Number = x.Unit.Number,
                Floor = x.Unit.Floor,
                BlockId = x.Unit.BlockId,
                BlockName = x.Unit.Block.Name,
                Relationship = x.Relationship,
                Description = x.Description,
                IsPrimary = x.IsPrimary
            }).ToList()
        });
    }

    [HttpGet("visits")]
    public async Task<IActionResult> Visits(CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var rows = await _context.AccessVisits.AsNoTracking()
            .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.GuestResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Credential)
            .Where(x => x.LicenseId == grant.LicenseId && x.HostResidentId == grant.ResidentId)
            .OrderByDescending(x => x.ValidFrom)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(x => new ConciergeVisitOut
        {
            Id = x.Id,
            HostResidentId = x.HostResidentId,
            HostName = x.HostResident.Name,
            BlockName = x.GuestResident.Unit.Block.Name,
            UnitNumber = x.GuestResident.Unit.Number,
            VisitorName = x.VisitorName,
            Document = x.Document,
            PhoneNumber = x.PhoneNumber,
            Company = x.Company,
            Purpose = x.Purpose,
            VehiclePlate = x.VehiclePlate,
            PhotoUrl = x.PhotoUrl,
            Status = x.Status.ToString(),
            CredentialType = x.Credential.CredentialType.ToString(),
            CredentialCode = x.Credential.Identifier,
            UseCount = x.Credential.UseCount,
            MaxUses = x.Credential.MaxUses,
            ValidFrom = x.ValidFrom,
            ValidTo = x.ValidTo,
            CheckedInAt = x.CheckedInAt,
            CheckedOutAt = x.CheckedOutAt,
            ApprovalRequired = x.ApprovalRequired,
            ApprovedBy = x.ApprovedBy,
            ApprovedAt = x.ApprovedAt,
            ApprovalNotes = x.ApprovalNotes,
            ExpectedCheckoutAt = x.ExpectedCheckoutAt,
            IsOverstayed = x.Status == Domain.Enums.Invitation.AccessVisitStatusEnum.CheckedIn && (x.ExpectedCheckoutAt ?? x.ValidTo) < DateTime.UtcNow,
            RecurrenceSequence = x.RecurrenceSequence,
            RecurrenceCount = x.RecurrenceCount
        }));
    }

    [HttpPost("visits")]
    public async Task<IActionResult> CreateVisit(
        [FromBody] CreateResidentVisitIn input,
        CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!grant.UnitIds.Contains(input.UnitId)) return NotFound();
        if (string.IsNullOrWhiteSpace(input.VisitorName))
            return BadRequest(new { Result = "VisitorNameRequired", Errors = "Informe o nome do visitante." });

        var validFrom = AsUtcInstant(input.ValidFrom);
        var validTo = AsUtcInstant(input.ValidTo);
        if (validTo <= validFrom || validTo <= DateTime.UtcNow || validTo > validFrom.AddDays(30))
            return BadRequest(new { Result = "InvalidWindow", Errors = "Informe uma janela futura de acesso com duracao maxima de 30 dias." });
        if (input.MaxUses is <= 0 or > 100)
            return BadRequest(new { Result = "InvalidMaxUses", Errors = "O limite de acessos deve estar entre 1 e 100." });

        var idempotencyKey = string.IsNullOrWhiteSpace(input.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : input.IdempotencyKey.Trim();
        if (idempotencyKey.Length > 100)
            return BadRequest(new { Result = "InvalidIdempotencyKey", Errors = "A chave da operacao e invalida." });

        var createdBy = $"resident:{grant.ResidentId:N}:key:{idempotencyKey}";
        var existing = await ResidentVisitQuery(grant)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.CreatedBy == createdBy, cancellationToken);
        if (existing is not null) return Ok(ToVisitOut(existing));

        var host = await _context.Residents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == grant.ResidentId, cancellationToken);
        var unit = await _context.Units.AsNoTracking()
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == input.UnitId && x.Block.LicenseId == grant.LicenseId, cancellationToken);
        if (host is null || unit is null) return NotFound();

        var visitorRoutesExist = await _context.AccessRoutes.AsNoTracking().AnyAsync(x =>
            x.LicenseId == grant.LicenseId && x.IsActive && x.AllowTemporary &&
            (x.Audience & AccessRouteAudienceEnum.Visitor) != 0, cancellationToken);
        if (!visitorRoutesExist)
            return BadRequest(new { Result = "VisitorRouteMissing", Errors = "Nao existe uma rota temporaria ativa para visitantes." });

        var document = Normalize(input.Document);
        var plate = Normalize(input.VehiclePlate);
        var watchlistMatch = await _context.AccessWatchlistEntries.AsNoTracking().AnyAsync(x =>
            x.LicenseId == grant.LicenseId && x.IsActive && (!x.ExpiresAt.HasValue || x.ExpiresAt > DateTime.UtcNow) &&
            ((!string.IsNullOrEmpty(document) && x.Document == document) ||
             (!string.IsNullOrEmpty(plate) && x.VehiclePlate == plate)), cancellationToken);
        if (watchlistMatch)
            return Conflict(new { Result = "WatchlistMatch", Errors = "Nao foi possivel autorizar esta visita. Procure a administracao." });

        var now = DateTime.UtcNow;
        var guestId = Guid.NewGuid();
        var credentialId = Guid.NewGuid();
        var guest = new ResidentAccessDTO
        {
            Id = guestId,
            UnitId = unit.Id,
            Name = input.VisitorName.Trim(),
            PhoneNumber = input.PhoneNumber.Trim(),
            CPF = input.Document.Trim(),
            ApartmentNumber = unit.Number,
            Description = input.Purpose.Trim(),
            AccessType = ResidentAccessTypeEnum.Guest,
            FirstAccess = true,
            IsActive = true,
            Temporary = true,
            Expire = validTo,
            LastAccess = now,
            CreatedAt = now
        };
        var credential = new ResidentAccessCredentialDTO
        {
            Id = credentialId,
            ResidentId = guestId,
            Resident = guest,
            CredentialType = AccessCredentialTypeEnum.QrCode,
            Identifier = $"VIS-{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}",
            IsActive = true,
            IsTemporary = true,
            MaxUses = input.MaxUses,
            ValidFrom = validFrom,
            ValidTo = validTo,
            CreatedAt = now
        };
        guest.AccessCredentials.Add(credential);

        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = grant.LicenseId,
            HostResidentId = grant.ResidentId,
            GuestResidentId = guestId,
            CredentialId = credentialId,
            VisitorName = guest.Name,
            Document = document,
            PhoneNumber = input.PhoneNumber.Trim(),
            Company = input.Company.Trim(),
            Purpose = input.Purpose.Trim(),
            VehiclePlate = plate,
            Status = AccessVisitStatusEnum.Scheduled,
            ValidFrom = validFrom,
            ValidTo = validTo,
            ExpectedCheckoutAt = validTo,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };

        _context.Residents.Add(guest);
        _context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(),
            ResidentId = guestId,
            UnitId = unit.Id,
            Relationship = ResidentUnitRelationshipEnum.Resident,
            Description = $"Visitante de {host.Name}",
            IsPrimary = true,
            IsActive = true,
            StartsAt = validFrom,
            EndsAt = validTo,
            CreatedAt = now,
            UpdatedAt = now
        });
        _context.AccessVisits.Add(visit);
        _context.AccessBatchOperations.Add(new AccessBatchOperationDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = grant.LicenseId,
            Operation = "ReconcileCredentials",
            IdempotencyKey = $"resident-visit:{grant.ResidentId:N}:{idempotencyKey}",
            Status = AccessBatchStatusEnum.Queued,
            RequestedBy = $"resident:{grant.ResidentId:N}",
            FilterJson = JsonSerializer.Serialize(new { credentialIds = new[] { credentialId } }),
            CreatedAt = now
        });
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = grant.LicenseId,
            EntityType = "Visit",
            EntityId = visit.Id,
            Action = "ResidentVisitCreated",
            Status = "Queued",
            Summary = $"Visita de {visit.VisitorName} autorizada pelo morador {host.Name}.",
            DetailsJson = JsonSerializer.Serialize(new { input.UnitId, visit.ValidFrom, visit.ValidTo, credential.MaxUses }),
            UserName = $"resident:{grant.ResidentId:N}",
            CreatedAt = now
        });

        await _context.SaveChangesAsync(cancellationToken);
        return Created(string.Empty, ToVisitOut(visit, host.Name, unit.Block.Name, unit.Number, credential));
    }

    [HttpGet("bookings")]
    public async Task<IActionResult> Bookings(CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var rows = await _context.AmenityBookings.AsNoTracking()
            .Include(x => x.Amenity)
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Include(x => x.Slot)
            .Where(x => x.LicenseId == grant.LicenseId && x.ResidentId == grant.ResidentId)
            .OrderByDescending(x => x.Date)
            .Take(100)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(x => new AmenityBookingOut
        {
            Id = x.Id,
            AmenityId = x.AmenityId,
            AmenityName = x.Amenity.Name,
            UnitId = x.UnitId,
            UnitNumber = x.Unit.Number,
            BlockName = x.Unit.Block.Name,
            ResidentId = x.ResidentId,
            ResidentName = x.Resident?.Name,
            Date = x.Date,
            SlotId = x.SlotId,
            SlotStartTime = x.Slot.StartTime,
            SlotEndTime = x.Slot.EndTime,
            Status = x.Status.ToString(),
            TermsAcceptedAt = x.TermsAcceptedAt,
            Notes = x.Notes,
            CreatedAt = x.CreatedAt,
            CancelledAt = x.CancelledAt,
            CancelReason = x.CancelReason
        }));
    }

    [HttpGet("amenities")]
    public async Task<IActionResult> Amenities(CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var rows = await _context.Amenities.AsNoTracking()
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .Where(x => x.LicenseId == grant.LicenseId && x.Active)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
        return Ok(rows.Select(x => new AmenityOut
        {
            Id = x.Id,
            Name = x.Name,
            Description = x.Description,
            Capacity = x.Capacity,
            Active = x.Active,
            FeeAmount = x.FeeAmount,
            FeeDescription = x.FeeDescription,
            RequiresApproval = x.RequiresApproval,
            RequiresTermsAcceptance = x.RequiresTermsAcceptance,
            TermsText = x.TermsText,
            MonthlyLimitPerUnit = x.MonthlyLimitPerUnit,
            MinAdvanceNoticeHours = x.MinAdvanceNoticeHours,
            MaxAdvanceDays = x.MaxAdvanceDays,
            CancellationCutoffHours = x.CancellationCutoffHours,
            ScheduleSlots = x.ScheduleSlots.Select(slot => new AmenityScheduleSlotOut
            {
                Id = slot.Id,
                DayOfWeek = slot.DayOfWeek,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Active = slot.Active
            }).ToList(),
            Blackouts = x.Blackouts.Select(blackout => new AmenityBlackoutOut
            {
                Id = blackout.Id,
                StartDate = blackout.StartDate,
                EndDate = blackout.EndDate,
                Reason = blackout.Reason
            }).ToList()
        }));
    }

    [HttpGet("amenities/{amenityId:guid}/availability")]
    public async Task<IActionResult> AmenityAvailability(
        Guid amenityId,
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var amenity = await _context.Amenities.AsNoTracking()
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == grant.LicenseId && x.Active, cancellationToken);
        if (amenity is null) return NotFound();

        var day = AsUtcDate(date);
        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, day))
            return Ok(Array.Empty<AmenitySlotAvailabilityOut>());

        var activeBookings = await _context.AmenityBookings.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => x.AmenityId == amenityId && x.Date == day &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed))
            .ToListAsync(cancellationToken);

        return Ok(amenity.ScheduleSlots
            .Where(x => x.Active && x.DayOfWeek == day.DayOfWeek)
            .OrderBy(x => x.StartTime)
            .Select(slot =>
            {
                var occupied = activeBookings.FirstOrDefault(x => x.SlotId == slot.Id);
                return new AmenitySlotAvailabilityOut
                {
                    SlotId = slot.Id,
                    StartTime = slot.StartTime,
                    EndTime = slot.EndTime,
                    Available = occupied is null,
                    OccupiedByUnitNumber = occupied?.Unit.Number,
                    OccupiedStatus = occupied?.Status,
                    BookingId = occupied?.Id
                };
            }));
    }

    [HttpPost("amenities/{amenityId:guid}/bookings")]
    public async Task<IActionResult> CreateBooking(
        Guid amenityId,
        [FromBody] CreateAmenityBookingIn input,
        CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();
        if (!grant.UnitIds.Contains(input.UnitId)) return NotFound();

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == grant.LicenseId, cancellationToken);
        if (amenity is null) return NotFound();
        if (!amenity.Active)
            return BadRequest(new { Result = "AmenityInactive", Errors = "Este local nao esta disponivel para agendamento." });

        var bookingDate = AsUtcDate(input.Date);
        var slot = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.SlotId && x.Active);
        if (slot is null || slot.DayOfWeek != bookingDate.DayOfWeek)
            return BadRequest(new { Result = "InvalidSlot", Errors = "O horario selecionado nao existe para esta data." });

        var now = DateTime.UtcNow;
        var windowError = AmenityBookingValidator.ValidateWindow(amenity, bookingDate, slot.StartTime, now);
        if (windowError is not null)
            return BadRequest(new { Result = "OutsideBookingWindow", Errors = windowError });
        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, bookingDate))
            return BadRequest(new { Result = "DateBlacked", Errors = "Esta data nao esta disponivel para este local." });
        if (amenity.RequiresTermsAcceptance && !input.TermsAccepted)
            return BadRequest(new { Result = "TermsNotAccepted", Errors = "E necessario aceitar o termo de uso." });

        var monthStart = new DateTime(bookingDate.Year, bookingDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var existingThisMonth = await _context.AmenityBookings.AsNoTracking().CountAsync(x =>
            x.AmenityId == amenityId && x.UnitId == input.UnitId && x.Date >= monthStart && x.Date < monthStart.AddMonths(1) &&
            (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed), cancellationToken);
        if (AmenityBookingValidator.HasReachedMonthlyLimit(amenity.MonthlyLimitPerUnit, existingThisMonth))
            return BadRequest(new { Result = "MonthlyLimitReached", Errors = "Esta unidade atingiu o limite mensal de reservas." });

        var slotTaken = await _context.AmenityBookings.AsNoTracking().AnyAsync(x =>
            x.AmenityId == amenityId && x.SlotId == input.SlotId && x.Date == bookingDate &&
            (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed), cancellationToken);
        if (slotTaken)
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario acabou de ser reservado." });

        var booking = new AmenityBookingDTO
        {
            Id = Guid.NewGuid(),
            AmenityId = amenityId,
            LicenseId = grant.LicenseId,
            UnitId = input.UnitId,
            ResidentId = grant.ResidentId,
            Date = bookingDate,
            SlotId = input.SlotId,
            Status = amenity.RequiresApproval ? AmenityBookingStatusEnum.Pending : AmenityBookingStatusEnum.Confirmed,
            TermsAcceptedAt = amenity.RequiresTermsAcceptance ? now : null,
            Notes = input.Notes?.Trim() ?? string.Empty,
            CreatedByUserId = grant.ResidentId,
            CreatedAt = now
        };
        _context.AmenityBookings.Add(booking);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario acabou de ser reservado." });
        }

        var created = await ResidentBookingQuery(grant).AsNoTracking()
            .FirstAsync(x => x.Id == booking.Id, cancellationToken);
        return Created(string.Empty, ToBookingOut(created));
    }

    [HttpDelete("bookings/{bookingId:guid}")]
    public async Task<IActionResult> CancelBooking(
        Guid bookingId,
        [FromBody] CancelAmenityBookingIn input,
        CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var booking = await _context.AmenityBookings
            .Include(x => x.Amenity)
            .Include(x => x.Slot)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.LicenseId == grant.LicenseId &&
                x.ResidentId == grant.ResidentId && grant.UnitIds.Contains(x.UnitId), cancellationToken);
        if (booking is null) return NotFound();
        if (booking.Status is AmenityBookingStatusEnum.Cancelled or AmenityBookingStatusEnum.Rejected or AmenityBookingStatusEnum.Completed)
            return Conflict(new { Result = "AlreadyClosed", Errors = "Esta reserva ja nao esta ativa." });
        if (!AmenityBookingValidator.CanCancel(booking.Amenity, booking.Date, booking.Slot.StartTime, DateTime.UtcNow))
            return Conflict(new { Result = "PastCancellationCutoff", Errors = $"O cancelamento encerrou {booking.Amenity.CancellationCutoffHours} hora(s) antes do horario." });

        booking.Status = AmenityBookingStatusEnum.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = input.Reason?.Trim() ?? string.Empty;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries(CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var deliveries = await _context.Deliveries.AsNoTracking()
            .Where(x => x.LicenseId == grant.LicenseId &&
                        x.RecipientResidentId == grant.ResidentId &&
                        x.UnitId.HasValue && grant.UnitIds.Contains(x.UnitId.Value))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new DeliveryOut
            {
                Id = x.Id,
                LicenseId = x.LicenseId,
                Type = x.Type.ToString(),
                Status = x.Status.ToString(),
                StatusValue = (int)x.Status,
                Name = x.Name,
                Description = x.Description,
                TrackingCode = x.TrackingCode,
                PhotoUrl = x.PhotoUrl,
                DeliveryProofUrl = x.DeliveryProofUrl,
                ReceivedBy = x.ReceivedBy,
                ReceivedAt = x.ReceivedAt,
                DeliveredTo = x.DeliveredTo,
                DeliveredAt = x.DeliveredAt,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                RecipientResidentId = x.RecipientResidentId,
                UnitId = x.UnitId
            })
            .ToListAsync(cancellationToken);

        return Ok(deliveries);
    }

    private IQueryable<AccessVisitDTO> ResidentVisitQuery(ResidentAccessGrant grant) =>
        _context.AccessVisits
            .Include(x => x.HostResident)
            .Include(x => x.GuestResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Credential)
            .Where(x => x.LicenseId == grant.LicenseId && x.HostResidentId == grant.ResidentId);

    private IQueryable<AmenityBookingDTO> ResidentBookingQuery(ResidentAccessGrant grant) =>
        _context.AmenityBookings
            .Include(x => x.Amenity)
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Include(x => x.Slot)
            .Where(x => x.LicenseId == grant.LicenseId && x.ResidentId == grant.ResidentId && grant.UnitIds.Contains(x.UnitId));

    private static ConciergeVisitOut ToVisitOut(AccessVisitDTO visit) =>
        ToVisitOut(visit, visit.HostResident.Name, visit.GuestResident.Unit.Block.Name, visit.GuestResident.Unit.Number, visit.Credential);

    private static ConciergeVisitOut ToVisitOut(
        AccessVisitDTO visit,
        string hostName,
        string blockName,
        string unitNumber,
        ResidentAccessCredentialDTO credential) => new()
    {
        Id = visit.Id,
        HostResidentId = visit.HostResidentId,
        HostName = hostName,
        BlockName = blockName,
        UnitNumber = unitNumber,
        VisitorName = visit.VisitorName,
        Document = visit.Document,
        PhoneNumber = visit.PhoneNumber,
        Company = visit.Company,
        Purpose = visit.Purpose,
        VehiclePlate = visit.VehiclePlate,
        PhotoUrl = visit.PhotoUrl,
        Status = visit.Status.ToString(),
        CredentialType = credential.CredentialType.ToString(),
        CredentialCode = credential.Identifier,
        UseCount = credential.UseCount,
        MaxUses = credential.MaxUses,
        ValidFrom = visit.ValidFrom,
        ValidTo = visit.ValidTo,
        CheckedInAt = visit.CheckedInAt,
        CheckedOutAt = visit.CheckedOutAt,
        ApprovalRequired = visit.ApprovalRequired,
        ApprovedBy = visit.ApprovedBy,
        ApprovedAt = visit.ApprovedAt,
        ApprovalNotes = visit.ApprovalNotes,
        ExpectedCheckoutAt = visit.ExpectedCheckoutAt,
        IsOverstayed = visit.Status == AccessVisitStatusEnum.CheckedIn && (visit.ExpectedCheckoutAt ?? visit.ValidTo) < DateTime.UtcNow,
        RecurrenceSequence = visit.RecurrenceSequence,
        RecurrenceCount = visit.RecurrenceCount
    };

    private static AmenityBookingOut ToBookingOut(AmenityBookingDTO booking) => new()
    {
        Id = booking.Id,
        AmenityId = booking.AmenityId,
        AmenityName = booking.Amenity.Name,
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

    private static DateTime AsUtcDate(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    private static DateTime AsUtcInstant(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
