using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/concierge")]
public sealed class ConciergeController(DatabaseContext context, IPrivateMediaStore media) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> Dashboard(Guid licenseId)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var now = DateTime.UtcNow;
        var expired = await context.AccessVisits.Include(x => x.Credential)
            .Where(x => x.LicenseId == licenseId && x.Status == AccessVisitStatusEnum.Scheduled && x.ValidTo <= now).ToListAsync();
        foreach (var visit in expired)
        {
            visit.Status = AccessVisitStatusEnum.Expired;
            visit.Credential.IsActive = false;
            visit.UpdatedAt = now;
            Queue(licenseId, visit.CredentialId, $"expire:{visit.Id:N}", "Expiracao automatica");
        }
        if (expired.Count > 0) await context.SaveChangesAsync();

        var visits = await VisitQuery(licenseId).Where(x => x.ValidTo >= now.AddDays(-1) && x.ValidFrom <= now.AddDays(7))
            .OrderBy(x => x.ValidFrom).ToListAsync();
        var events = await context.AccessEventRecords.AsNoTracking().Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.OccurredAt).Take(80)
            .Select(x => new ConciergeEventOut { Id = x.Id, DeviceName = x.Device.Name, PersonName = x.PersonName, Event = x.Event, Authorized = x.Authorized, Portal = x.Portal, OccurredAt = x.OccurredAt })
            .ToListAsync();
        var onlineThreshold = now.AddMinutes(-5);
        var devices = await context.Devices.AsNoTracking().Where(x => x.LicenseId == licenseId).OrderBy(x => x.Name)
            .Select(x => new ConciergeDeviceOut
            {
                Id = x.Id, Name = x.Name, Model = x.Model,
                Online = x.IsActive && x.LastSeenAt.HasValue && x.LastSeenAt >= onlineThreshold,
                HealthMessage = x.HealthMessage, DiscoveredPortalsJson = x.DiscoveredPortalsJson
            }).ToListAsync();
        var startToday = DateTime.UtcNow.Date;
        var watchlist = await context.AccessWatchlistEntries.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && x.IsActive && (!x.ExpiresAt.HasValue || x.ExpiresAt > now))
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Name).ToListAsync();
        return Ok(new ConciergeDashboardOut
        {
            Visits = visits.Select(ToOut).ToList(), Events = events, Devices = devices,
            ExpectedToday = visits.Count(x => x.ValidFrom < startToday.AddDays(1) && x.ValidTo >= startToday && x.Status == AccessVisitStatusEnum.Scheduled),
            InsideNow = visits.Count(x => x.Status == AccessVisitStatusEnum.CheckedIn),
            OfflineDevices = devices.Count(x => !x.Online),
            DeniedToday = events.Count(x => !x.Authorized && x.OccurredAt >= startToday),
            PendingApprovals = visits.Count(x => x.Status == AccessVisitStatusEnum.PendingApproval),
            Overstays = visits.Count(x => x.Status == AccessVisitStatusEnum.CheckedIn && (x.ExpectedCheckoutAt ?? x.ValidTo) < now),
            Watchlist = watchlist.Select(ToWatchlistOut).ToList()
        });
    }

    [HttpPost("visits")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateVisit(Guid licenseId, [FromBody] CreateConciergeVisitIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        if (string.IsNullOrWhiteSpace(input.VisitorName)) return BadRequest(new { Errors = "Informe o nome do visitante." });
        var validFrom = Utc(input.ValidFrom); var validTo = Utc(input.ValidTo);
        if (validTo <= validFrom || validTo <= DateTime.UtcNow) return BadRequest(new { Errors = "A janela de acesso informada e invalida." });
        if (input.CredentialType is not (AccessCredentialTypeEnum.QrCode or AccessCredentialTypeEnum.Face))
            return BadRequest(new { Errors = "Visitas aceitam QR Code ou reconhecimento facial temporario." });
        if (input.MaxUses is <= 0 or > 100)
            return BadRequest(new { Errors = "O limite de acessos deve estar entre 1 e 100." });
        if (input.RepeatCount is < 1 or > 30 || input.RepeatEveryDays is < 1 or > 365)
            return BadRequest(new { Errors = "A recorrencia deve ter de 1 a 30 ocorrencias e intervalo de 1 a 365 dias." });
        if (input.CredentialType == AccessCredentialTypeEnum.Face)
        {
            var validation = FaceImageValidator.Validate(input.ImageBase64, 1_000_000);
            if (!validation.Success) return BadRequest(new { Errors = validation.Error });
        }
        var host = await context.Residents.Include(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == input.HostResidentId && x.Unit.Block.LicenseId == licenseId);
        if (host is null) return NotFound();
        var normalizedDocument = NormalizeDocument(input.Document);
        var normalizedPlate = NormalizePlate(input.VehiclePlate);
        var watchlistMatch = await context.AccessWatchlistEntries.AsNoTracking().FirstOrDefaultAsync(x =>
            x.LicenseId == licenseId && x.IsActive && (!x.ExpiresAt.HasValue || x.ExpiresAt > DateTime.UtcNow) &&
            ((!string.IsNullOrEmpty(normalizedDocument) && x.Document == normalizedDocument) || (!string.IsNullOrEmpty(normalizedPlate) && x.VehiclePlate == normalizedPlate)));
        if (watchlistMatch is not null)
        {
            Audit(licenseId, watchlistMatch.Id, "WatchlistMatch", "Blocked", $"Autorizacao bloqueada para {input.VisitorName}.", new { watchlistMatch.Reason, watchlistMatch.Severity });
            await context.SaveChangesAsync();
            return Conflict(new { Result = "WatchlistMatch", Errors = $"Autorizacao bloqueada pela lista de restricao: {watchlistMatch.Reason}" });
        }
        if (!string.IsNullOrWhiteSpace(input.IdempotencyKey))
        {
            var existing = await VisitQuery(licenseId).FirstOrDefaultAsync(x => x.CreatedBy == $"key:{input.IdempotencyKey}");
            if (existing is not null) return Ok(ToOut(existing));
        }

        var visitorRoutes = await context.AccessRoutes
            .Where(x => x.LicenseId == licenseId && x.IsActive && (x.Audience & AccessRouteAudienceEnum.Visitor) != 0)
            .ToListAsync();
        if (visitorRoutes.Count == 0)
            return BadRequest(new { Errors = "Configure ao menos uma rota ativa para visitantes antes de autorizar a visita." });
        if (input.RouteIds.Count > 0 && !input.RouteIds.ToHashSet().IsSubsetOf(visitorRoutes.Select(x => x.Id).ToHashSet()))
            return BadRequest(new { Errors = "Uma ou mais rotas selecionadas nao pertencem a esta licenca ou nao aceitam visitantes." });

        var photoReference = string.IsNullOrWhiteSpace(input.ImageBase64)
            ? string.Empty
            : await media.StoreDataUriAsync(licenseId, input.ImageBase64.Trim(), HttpContext.RequestAborted);
        var now = DateTime.UtcNow; var guestId = Guid.NewGuid(); var credentialId = Guid.NewGuid();
        var guest = new ResidentAccessDTO
        {
            Id = guestId, UnitId = host.UnitId, Name = input.VisitorName.Trim(), Email = string.Empty, Password = string.Empty,
            PhoneNumber = input.PhoneNumber.Trim(), CommercialPhone = string.Empty, CPF = input.Document.Trim(), RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = host.Unit.Number, ImgUrl = photoReference, Description = input.Purpose.Trim(),
            AccessType = ResidentAccessTypeEnum.Guest, FirstAccess = true, NotifyAccess = false, IsActive = true,
            Temporary = true, Expire = validTo, LastAccess = now, CreatedAt = now, AccessCredentials = []
        };
        var credential = new ResidentAccessCredentialDTO
        {
            Id = credentialId, ResidentId = guestId, Resident = guest, CredentialType = input.CredentialType,
            Identifier = input.CredentialType == AccessCredentialTypeEnum.Face ? $"FACE-{guestId:N}" : $"VIS-{Token()}",
            IsActive = !input.RequireApproval, IsTemporary = true, MaxUses = input.MaxUses is > 0 ? input.MaxUses : null,
            ValidFrom = validFrom, ValidTo = validTo, CreatedAt = now, Devices = []
        };
        guest.AccessCredentials.Add(credential);
        context.Residents.Add(guest);
        context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(), ResidentId = guestId, UnitId = host.UnitId, Relationship = ResidentUnitRelationshipEnum.Resident,
            Description = $"Visitante de {host.Name}", IsPrimary = true, IsActive = true, StartsAt = validFrom, EndsAt = validTo, CreatedAt = now, UpdatedAt = now
        });
        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, HostResidentId = host.Id, GuestResidentId = guestId, CredentialId = credentialId,
            VisitorName = guest.Name, Document = normalizedDocument, PhoneNumber = input.PhoneNumber.Trim(), Company = input.Company.Trim(),
            Purpose = input.Purpose.Trim(), VehiclePlate = normalizedPlate, PhotoUrl = guest.ImgUrl,
            Status = input.RequireApproval ? AccessVisitStatusEnum.PendingApproval : AccessVisitStatusEnum.Scheduled, ValidFrom = validFrom, ValidTo = validTo,
            ApprovalRequired = input.RequireApproval, ExpectedCheckoutAt = validTo,
            RecurrenceGroupId = input.RepeatCount > 1 ? Guid.NewGuid() : null, RecurrenceSequence = 1, RecurrenceCount = input.RepeatCount,
            CreatedBy = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? CurrentUser() : $"key:{input.IdempotencyKey}", CreatedAt = now, UpdatedAt = now
        };
        context.AccessVisits.Add(visit);

        if (input.RouteIds.Count > 0)
        {
            var selected = input.RouteIds.ToHashSet();
            foreach (var route in visitorRoutes)
                context.AccessRouteResidentOverrides.Add(new AccessRouteResidentOverrideDTO
                {
                    Id = Guid.NewGuid(), AccessRouteId = route.Id, ResidentId = guestId,
                    Mode = selected.Contains(route.Id) ? AccessRouteOverrideModeEnum.Include : AccessRouteOverrideModeEnum.Exclude,
                    Reason = "Rotas definidas para visita temporaria", CreatedAt = now, UpdatedAt = now
                });
        }
        if (input.RepeatCount > 1 && visit.RecurrenceGroupId.HasValue)
        {
            for (var sequence = 2; sequence <= input.RepeatCount; sequence++)
            {
                var offset = TimeSpan.FromDays((sequence - 1) * input.RepeatEveryDays);
                var recurring = CreateRecurringOccurrence(licenseId, host, input, photoReference, normalizedDocument, normalizedPlate,
                    validFrom.Add(offset), validTo.Add(offset), visit.RecurrenceGroupId.Value, sequence, now);
                if (input.RouteIds.Count > 0)
                {
                    var selected = input.RouteIds.ToHashSet();
                    foreach (var route in visitorRoutes)
                        context.AccessRouteResidentOverrides.Add(new AccessRouteResidentOverrideDTO
                        {
                            Id = Guid.NewGuid(), AccessRouteId = route.Id, ResidentId = recurring.GuestResidentId,
                            Mode = selected.Contains(route.Id) ? AccessRouteOverrideModeEnum.Include : AccessRouteOverrideModeEnum.Exclude,
                            Reason = "Rotas definidas para visita recorrente", CreatedAt = now, UpdatedAt = now
                        });
                }
                if (!input.RequireApproval) Queue(licenseId, recurring.CredentialId, $"{input.IdempotencyKey}:{sequence}", CurrentUser());
            }
        }
        if (!input.RequireApproval) Queue(licenseId, credentialId, input.IdempotencyKey, CurrentUser());
        Audit(licenseId, visit.Id, "VisitCreated", "Queued", $"Visita de {visit.VisitorName} para {host.Name} agendada.",
            new { input.HostResidentId, input.VisitorName, input.CredentialType, validFrom, validTo, input.MaxUses, input.RouteIds, HasPhoto = !string.IsNullOrWhiteSpace(input.ImageBase64) });
        await context.SaveChangesAsync();
        return Created("", ToOut(visit, host, credential));
    }

    [HttpPost("visits/{visitId:guid}/approval")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> DecideApproval(Guid licenseId, Guid visitId, [FromBody] DecideVisitApprovalIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var visit = await VisitQuery(licenseId).FirstOrDefaultAsync(x => x.Id == visitId);
        if (visit is null) return NotFound();
        if (visit.Status != AccessVisitStatusEnum.PendingApproval) return Conflict(new { Errors = "Esta visita nao possui aprovacao pendente." });
        var now = DateTime.UtcNow;
        visit.ApprovedAt = now; visit.ApprovedBy = CurrentUser(); visit.ApprovalNotes = input.Notes.Trim(); visit.UpdatedAt = now;
        visit.Status = input.Approved ? AccessVisitStatusEnum.Scheduled : AccessVisitStatusEnum.Denied;
        visit.Credential.IsActive = input.Approved;
        visit.Credential.UpdatedAt = now;
        if (input.Approved) Queue(licenseId, visit.CredentialId, $"approval:{visit.Id:N}", CurrentUser());
        Audit(licenseId, visit.Id, input.Approved ? "VisitApproved" : "VisitRejected", "Success", input.Approved ? "Visita aprovada." : "Visita recusada.", input);
        await context.SaveChangesAsync();
        return Ok(ToOut(visit));
    }

    [HttpGet("watchlist")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> GetWatchlist(Guid licenseId)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var rows = await context.AccessWatchlistEntries.AsNoTracking().Where(x => x.LicenseId == licenseId && x.IsActive)
            .OrderByDescending(x => x.Severity).ThenBy(x => x.Name).ToListAsync();
        return Ok(rows.Select(ToWatchlistOut));
    }

    [HttpPost("watchlist")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> AddWatchlist(Guid licenseId, [FromBody] CreateWatchlistEntryIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var document = NormalizeDocument(input.Document); var plate = NormalizePlate(input.VehiclePlate);
        if (string.IsNullOrWhiteSpace(document) && string.IsNullOrWhiteSpace(plate)) return BadRequest(new { Errors = "Informe um documento ou placa para a restricao." });
        if (string.IsNullOrWhiteSpace(input.Reason)) return BadRequest(new { Errors = "Informe o motivo da restricao." });
        if (await context.AccessWatchlistEntries.AnyAsync(x => x.LicenseId == licenseId && x.IsActive && ((!string.IsNullOrEmpty(document) && x.Document == document) || (!string.IsNullOrEmpty(plate) && x.VehiclePlate == plate))))
            return Conflict(new { Errors = "Ja existe uma restricao ativa para este documento ou placa." });
        var row = new AccessWatchlistEntryDTO { Id = Guid.NewGuid(), LicenseId = licenseId, Name = input.Name.Trim(), Document = document, VehiclePlate = plate, Reason = input.Reason.Trim(), Severity = Math.Clamp(input.Severity, 1, 3), ExpiresAt = input.ExpiresAt.HasValue ? Utc(input.ExpiresAt.Value) : null, CreatedBy = CurrentUser(), CreatedAt = DateTime.UtcNow };
        context.AccessWatchlistEntries.Add(row);
        Audit(licenseId, row.Id, "WatchlistAdded", "Success", $"Restricao adicionada para {row.Name}.", input);
        await context.SaveChangesAsync();
        return Created("", ToWatchlistOut(row));
    }

    [HttpDelete("watchlist/{entryId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> RemoveWatchlist(Guid licenseId, Guid entryId)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var row = await context.AccessWatchlistEntries.FirstOrDefaultAsync(x => x.Id == entryId && x.LicenseId == licenseId);
        if (row is null) return NotFound();
        row.IsActive = false;
        Audit(licenseId, row.Id, "WatchlistRemoved", "Success", $"Restricao removida para {row.Name}.", new { row.Document, row.VehiclePlate });
        await context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("visits/{visitId:guid}/status")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> UpdateStatus(Guid licenseId, Guid visitId, [FromBody] UpdateConciergeVisitStatusIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var visit = await VisitQuery(licenseId).FirstOrDefaultAsync(x => x.Id == visitId);
        if (visit is null) return NotFound();
        if (!IsValidTransition(visit.Status, input.Status))
            return Conflict(new { Errors = $"Nao e possivel alterar uma visita {visit.Status} para {input.Status}." });
        var now = DateTime.UtcNow; visit.Status = input.Status; visit.UpdatedAt = now;
        if (input.Status == AccessVisitStatusEnum.CheckedIn && (now < visit.ValidFrom || now > visit.ValidTo))
            return Conflict(new { Errors = "A visita esta fora da janela de acesso autorizada." });
        if (input.Status == AccessVisitStatusEnum.CheckedIn) visit.CheckedInAt = now;
        if (input.Status == AccessVisitStatusEnum.CheckedOut) visit.CheckedOutAt = now;
        if (input.Status is AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied or AccessVisitStatusEnum.CheckedOut)
        {
            visit.Credential.IsActive = false; visit.Credential.UpdatedAt = now;
            Queue(licenseId, visit.CredentialId, Guid.NewGuid().ToString("N"), CurrentUser());
        }
        Audit(licenseId, visit.Id, "VisitStatus", "Success", $"Visita alterada para {input.Status}. {input.Reason}", input);
        await context.SaveChangesAsync();
        return Ok(ToOut(visit));
    }

    private IQueryable<AccessVisitDTO> VisitQuery(Guid licenseId) => context.AccessVisits
        .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.GuestResident).Include(x => x.Credential).Where(x => x.LicenseId == licenseId);
    private AccessVisitDTO CreateRecurringOccurrence(Guid licenseId, ResidentAccessDTO host, CreateConciergeVisitIn input,
        string photoReference, string document, string plate, DateTime validFrom, DateTime validTo, Guid groupId, int sequence, DateTime now)
    {
        var guestId = Guid.NewGuid(); var credentialId = Guid.NewGuid();
        var guest = new ResidentAccessDTO
        {
            Id = guestId, UnitId = host.UnitId, Name = input.VisitorName.Trim(), Email = string.Empty, Password = string.Empty,
            PhoneNumber = input.PhoneNumber.Trim(), CommercialPhone = string.Empty, CPF = document, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = host.Unit.Number, ImgUrl = photoReference, Description = input.Purpose.Trim(),
            AccessType = ResidentAccessTypeEnum.Guest, FirstAccess = true, NotifyAccess = false, IsActive = true,
            Temporary = true, Expire = validTo, LastAccess = now, CreatedAt = now, AccessCredentials = []
        };
        var credential = new ResidentAccessCredentialDTO
        {
            Id = credentialId, ResidentId = guestId, Resident = guest, CredentialType = input.CredentialType,
            Identifier = input.CredentialType == AccessCredentialTypeEnum.Face ? $"FACE-{guestId:N}" : $"VIS-{Token()}",
            IsActive = !input.RequireApproval, IsTemporary = true, MaxUses = input.MaxUses is > 0 ? input.MaxUses : null,
            ValidFrom = validFrom, ValidTo = validTo, CreatedAt = now, Devices = []
        };
        guest.AccessCredentials.Add(credential);
        context.Residents.Add(guest);
        context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(), ResidentId = guestId, UnitId = host.UnitId, Relationship = ResidentUnitRelationshipEnum.Resident,
            Description = $"Visitante recorrente de {host.Name}", IsPrimary = true, IsActive = true, StartsAt = validFrom, EndsAt = validTo, CreatedAt = now, UpdatedAt = now
        });
        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, HostResidentId = host.Id, GuestResidentId = guestId, CredentialId = credentialId,
            VisitorName = guest.Name, Document = document, PhoneNumber = input.PhoneNumber.Trim(), Company = input.Company.Trim(),
            Purpose = input.Purpose.Trim(), VehiclePlate = plate, PhotoUrl = photoReference,
            Status = input.RequireApproval ? AccessVisitStatusEnum.PendingApproval : AccessVisitStatusEnum.Scheduled,
            ValidFrom = validFrom, ValidTo = validTo, ExpectedCheckoutAt = validTo, ApprovalRequired = input.RequireApproval,
            RecurrenceGroupId = groupId, RecurrenceSequence = sequence, RecurrenceCount = input.RepeatCount,
            CreatedBy = string.IsNullOrWhiteSpace(input.IdempotencyKey) ? CurrentUser() : $"key:{input.IdempotencyKey}", CreatedAt = now, UpdatedAt = now
        };
        context.AccessVisits.Add(visit);
        return visit;
    }
    private void Queue(Guid licenseId, Guid credentialId, string key, string actor) => context.AccessBatchOperations.Add(new AccessBatchOperationDTO
    {
        Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials", IdempotencyKey = string.IsNullOrWhiteSpace(key) ? Guid.NewGuid().ToString("N") : $"visit:{key}",
        Status = AccessBatchStatusEnum.Queued, RequestedBy = actor, FilterJson = JsonSerializer.Serialize(new { credentialIds = new[] { credentialId } }), CreatedAt = DateTime.UtcNow
    });
    private void Audit(Guid licenseId, Guid id, string action, string status, string summary, object details) => context.AccessOperationAudits.Add(new AccessOperationAuditDTO
    {
        Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Visit", EntityId = id, Action = action, Status = status,
        Summary = summary, DetailsJson = JsonSerializer.Serialize(details), UserName = CurrentUser(), CreatedAt = DateTime.UtcNow
    });
    private async Task<bool> HasAccessAsync(Guid licenseId) => Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId) &&
        await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    private string CurrentUser() => User.FindFirstValue("name") ?? User.Identity?.Name ?? "Portaria";
    private static ConciergeVisitOut ToOut(AccessVisitDTO x) => ToOut(x, x.HostResident, x.Credential);
    private static ConciergeVisitOut ToOut(AccessVisitDTO x, ResidentAccessDTO host, ResidentAccessCredentialDTO credential) => new()
    {
        Id = x.Id, HostResidentId = x.HostResidentId, HostName = host.Name, BlockName = host.Unit.Block.Name, UnitNumber = host.Unit.Number,
        VisitorName = x.VisitorName, Document = x.Document, PhoneNumber = x.PhoneNumber, Company = x.Company, Purpose = x.Purpose,
        VehiclePlate = x.VehiclePlate, PhotoUrl = x.PhotoUrl, Status = x.Status.ToString(), CredentialType = credential.CredentialType.ToString(),
        CredentialCode = credential.Identifier, UseCount = credential.UseCount, MaxUses = credential.MaxUses,
        ValidFrom = x.ValidFrom, ValidTo = x.ValidTo, CheckedInAt = x.CheckedInAt, CheckedOutAt = x.CheckedOutAt,
        ApprovalRequired = x.ApprovalRequired, ApprovedBy = x.ApprovedBy, ApprovedAt = x.ApprovedAt, ApprovalNotes = x.ApprovalNotes,
        ExpectedCheckoutAt = x.ExpectedCheckoutAt, IsOverstayed = x.Status == AccessVisitStatusEnum.CheckedIn && (x.ExpectedCheckoutAt ?? x.ValidTo) < DateTime.UtcNow,
        RecurrenceSequence = x.RecurrenceSequence, RecurrenceCount = x.RecurrenceCount
    };
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static string NormalizePlate(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string NormalizeDocument(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static AccessWatchlistEntryOut ToWatchlistOut(AccessWatchlistEntryDTO x) => new() { Id = x.Id, Name = x.Name, Document = x.Document, VehiclePlate = x.VehiclePlate, Reason = x.Reason, Severity = x.Severity, ExpiresAt = x.ExpiresAt };
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static bool IsValidTransition(AccessVisitStatusEnum current, AccessVisitStatusEnum next) =>
        (current, next) switch
        {
            (AccessVisitStatusEnum.Scheduled, AccessVisitStatusEnum.CheckedIn or AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied) => true,
            (AccessVisitStatusEnum.CheckedIn, AccessVisitStatusEnum.CheckedOut) => true,
            _ => false
        };
}
