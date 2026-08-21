using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Enums.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Services.Mobile;
using CondotifyAPI.Services.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/concierge")]
public sealed class ConciergeController(
    DatabaseContext context,
    IPrivateMediaStore media,
    IHubContext<CondotifyAPI.Hubs.ConciergeHub> hub,
    IVisitFacialInviteService facialInvites,
    IPlatformPushNotifier? push = null) : ControllerBase
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
        var events = await EventQuery(context, licenseId)
            .OrderByDescending(x => x.OccurredAt).Take(80).ToListAsync();
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

    [HttpGet("events")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> EventsFeed(Guid licenseId, [FromQuery] string? search, [FromQuery] bool? authorized, [FromQuery] int take = 100)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var events = await GetEventsFeedCore(context, licenseId, search, authorized, take);
        return Ok(events);
    }

    internal static async Task<List<ConciergeEventOut>> GetEventsFeedCore(DatabaseContext context, Guid licenseId, string? search, bool? authorized, int take)
    {
        var records = context.AccessEventRecords.AsNoTracking().Where(x => x.LicenseId == licenseId);
        if (authorized.HasValue) records = records.Where(x => x.Authorized == authorized.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            records = records.Where(x => EF.Functions.ILike(x.PersonName, pattern) || EF.Functions.ILike(x.Portal, pattern) ||
                EF.Functions.ILike(x.Device.Name, pattern) || EF.Functions.ILike(x.Details, pattern));
        }
        return await EventQuery(context, licenseId, records)
            .OrderByDescending(x => x.OccurredAt).Take(Math.Clamp(take, 1, 500)).ToListAsync();
    }

    private static IQueryable<ConciergeEventOut> EventQuery(
        DatabaseContext context,
        Guid licenseId,
        IQueryable<AccessEventRecordDTO>? records = null)
    {
        records ??= context.AccessEventRecords.AsNoTracking().Where(x => x.LicenseId == licenseId);
        return records.Select(x => new ConciergeEventOut
            {
                Id = x.Id,
                DeviceId = x.DeviceId,
                CredentialId = x.CredentialId,
                ResidentId = x.AccessCredential != null ? x.AccessCredential.ResidentId : null,
                UnitId = x.AccessCredential != null ? x.AccessCredential.Resident.UnitId : null,
                VisitId = context.AccessVisits.Where(v => v.CredentialId == x.CredentialId)
                    .OrderByDescending(v => v.CreatedAt).Select(v => (Guid?)v.Id).FirstOrDefault(),
                DeviceName = x.Device.Name,
                PersonName = x.PersonName,
                PhotoUrl = x.AccessCredential != null ? x.AccessCredential.Resident.ImgUrl : string.Empty,
                PhoneNumber = x.AccessCredential != null ? x.AccessCredential.Resident.PhoneNumber : string.Empty,
                BlockName = x.AccessCredential != null ? x.AccessCredential.Resident.Unit.Block.Name : string.Empty,
                UnitNumber = x.AccessCredential != null ? x.AccessCredential.Resident.Unit.Number : string.Empty,
                CredentialType = x.AccessCredential != null ? x.AccessCredential.CredentialType.ToString() : string.Empty,
                Credential = x.Credential,
                CredentialActive = x.AccessCredential != null ? x.AccessCredential.IsActive : null,
                CredentialValidFrom = x.AccessCredential != null ? x.AccessCredential.ValidFrom : null,
                CredentialValidTo = x.AccessCredential != null ? x.AccessCredential.ValidTo : null,
                Details = x.Details,
                HostName = context.AccessVisits.Where(v => v.CredentialId == x.CredentialId)
                    .OrderByDescending(v => v.CreatedAt).Select(v => v.HostResident.Name).FirstOrDefault() ?? string.Empty,
                HostPhoneNumber = context.AccessVisits.Where(v => v.CredentialId == x.CredentialId)
                    .OrderByDescending(v => v.CreatedAt).Select(v => v.HostResident.PhoneNumber).FirstOrDefault() ?? string.Empty,
                Event = x.Event,
                Authorized = x.Authorized,
                Portal = x.Portal,
                OccurredAt = x.OccurredAt,
                RequiresAttention = !x.Authorized && !x.AttentionResolvedAt.HasValue,
                AttentionResolvedAt = x.AttentionResolvedAt,
                AttentionResolvedBy = x.AttentionResolvedBy,
                AttentionResolutionNote = x.AttentionResolutionNote
            });
    }

    [HttpPost("events/{eventId:guid}/resolve")]
    [RequireLicensePermission(LicensePermissionEnum.OperateDevices)]
    public async Task<IActionResult> ResolveEvent(Guid licenseId, Guid eventId, [FromBody] ResolveConciergeEventIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var accessEvent = await context.AccessEventRecords
            .FirstOrDefaultAsync(x => x.Id == eventId && x.LicenseId == licenseId);
        if (accessEvent is null) return NotFound();
        if (accessEvent.Authorized)
            return Conflict(new { Errors = "Somente ocorrencias que exigem atencao precisam ser resolvidas." });

        var note = string.IsNullOrWhiteSpace(input.Note) ? "Verificado pela portaria." : input.Note.Trim();
        var now = DateTime.UtcNow;
        accessEvent.AttentionResolvedAt = now;
        accessEvent.AttentionResolvedBy = CurrentUser();
        accessEvent.AttentionResolutionNote = note;
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "AccessEvent", EntityId = eventId,
            Action = "ResolveAttention", Status = "Success",
            Summary = $"Ocorrencia de {accessEvent.PersonName} resolvida pela portaria.",
            DetailsJson = JsonSerializer.Serialize(new { note, accessEvent.DeviceId, accessEvent.Portal }),
            UserName = accessEvent.AttentionResolvedBy, CreatedAt = now
        });
        await context.SaveChangesAsync();

        var payload = await EventQuery(context, licenseId).FirstAsync(x => x.Id == eventId);
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId))
            .SendAsync("AccessEventResolved", payload, HttpContext.RequestAborted);
        return Ok(payload);
    }

    [HttpGet("visits")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> Visits(Guid licenseId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var start = (from ?? DateTime.UtcNow.Date.AddDays(-90)).Date;
        var end = (to ?? DateTime.UtcNow.Date.AddDays(91)).Date;
        if (end <= start || end > start.AddYears(1))
            return BadRequest(new { Errors = "Informe um periodo valido de ate um ano." });

        var visits = await VisitQuery(licenseId)
            .Where(visit => visit.ValidTo >= start && visit.ValidFrom < end)
            .OrderByDescending(visit => visit.ValidFrom)
            .Take(500)
            .ToListAsync();
        return Ok(visits.Select(ToOut));
    }

    [HttpPost("visits")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> CreateVisit(Guid licenseId, [FromBody] CreateConciergeVisitIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        NormalizeCreateVisitInput(input);
        var inputError = ValidateCreateVisitInput(input);
        if (inputError is not null) return BadRequest(new { Errors = inputError });
        var validFrom = Utc(input.ValidFrom); var validTo = Utc(input.ValidTo);
        if (validTo <= validFrom || validTo <= DateTime.UtcNow) return BadRequest(new { Errors = "A janela de acesso informada e invalida." });
        if (input.CredentialType is not (AccessCredentialTypeEnum.QrCode or AccessCredentialTypeEnum.Face))
            return BadRequest(new { Errors = "Visitas aceitam QR Code ou reconhecimento facial temporario." });
        if (input.MaxUses is <= 0 or > 100)
            return BadRequest(new { Errors = "O limite de acessos deve estar entre 1 e 100." });
        if (input.RepeatCount is < 1 or > 30 || input.RepeatEveryDays is < 1 or > 365)
            return BadRequest(new { Errors = "A recorrencia deve ter de 1 a 30 ocorrencias e intervalo de 1 a 365 dias." });
        var createFacialInvite = input.CredentialType == AccessCredentialTypeEnum.Face && input.CreateFacialInvite;
        if (createFacialInvite && input.RequireApproval)
            return BadRequest(new { Errors = "O convite facial não pode depender de uma segunda aprovação." });
        if (createFacialInvite && input.RepeatCount > 1)
            return BadRequest(new { Errors = "Crie um convite facial separado para cada período de visita." });
        if (input.CredentialType == AccessCredentialTypeEnum.Face && !createFacialInvite)
        {
            var validation = FaceImageValidator.Validate(input.ImageBase64, 1_000_000);
            if (!validation.Success) return BadRequest(new { Errors = validation.Error });
        }
        var host = await context.Residents
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == input.HostResidentId);
        var hostUnit = host is null ? null : ResidentLicenseScope.ResolveCurrentUnitForLicense(host, licenseId, DateTime.UtcNow);
        if (host is null || hostUnit is null) return NotFound();
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

        var visitorRoutes = await VisitorRouteQuery(context, licenseId).ToListAsync();
        if (visitorRoutes.Count == 0)
            return BadRequest(new { Errors = "Configure ao menos uma rota ativa para visitantes antes de autorizar a visita." });
        if (input.RouteIds.Count > 0 && !input.RouteIds.ToHashSet().IsSubsetOf(visitorRoutes.Select(x => x.Id).ToHashSet()))
            return BadRequest(new { Errors = "Uma ou mais rotas selecionadas nao pertencem a esta licenca ou nao aceitam visitantes." });
        var selectedRouteIds = input.RouteIds.Count == 0
            ? visitorRoutes.Select(x => x.Id).ToHashSet()
            : input.RouteIds.ToHashSet();
        if (createFacialInvite && !visitorRoutes
                .Where(x => selectedRouteIds.Contains(x.Id))
                .SelectMany(x => x.Devices)
                .Any(x => x.IsActive && x.DeviceType.SupportsFace()))
            return BadRequest(new { Errors = "Selecione ao menos uma rota com equipamento facial." });

        var photoReference = createFacialInvite || input.CredentialType != AccessCredentialTypeEnum.Face || string.IsNullOrWhiteSpace(input.ImageBase64)
            ? string.Empty
            : await media.StoreDataUriAsync(licenseId, input.ImageBase64.Trim(), HttpContext.RequestAborted);
        var now = DateTime.UtcNow; var guestId = Guid.NewGuid(); var credentialId = Guid.NewGuid();
        var guest = new ResidentAccessDTO
        {
            Id = guestId, UnitId = hostUnit.Id, Name = input.VisitorName.Trim(), Email = string.Empty, Password = string.Empty,
            PhoneNumber = input.PhoneNumber, CommercialPhone = string.Empty, CPF = normalizedDocument, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = hostUnit.Number, ImgUrl = photoReference, Description = input.Purpose.Trim(),
            AccessType = ResidentAccessTypeEnum.Guest, FirstAccess = true, NotifyAccess = false, IsActive = true,
            Temporary = true, Expire = validTo, LastAccess = now, CreatedAt = now, AccessCredentials = []
        };
        var credential = new ResidentAccessCredentialDTO
        {
            Id = credentialId, ResidentId = guestId, Resident = guest, CredentialType = input.CredentialType,
            Identifier = input.CredentialType == AccessCredentialTypeEnum.Face ? $"FACE-{guestId:N}" : $"VIS-{Token()}",
            IsActive = !input.RequireApproval && !createFacialInvite, IsTemporary = true, MaxUses = input.MaxUses is > 0 ? input.MaxUses : null,
            ValidFrom = validFrom, ValidTo = validTo, CreatedAt = now, Devices = []
        };
        guest.AccessCredentials.Add(credential);
        context.Residents.Add(guest);
        context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(), ResidentId = guestId, UnitId = hostUnit.Id, Relationship = ResidentUnitRelationshipEnum.Resident,
            Description = $"Visitante de {host.Name}", IsPrimary = true, IsActive = true, StartsAt = validFrom, EndsAt = validTo, CreatedAt = now, UpdatedAt = now
        });
        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, HostResidentId = host.Id, GuestResidentId = guestId, CredentialId = credentialId,
            VisitorName = guest.Name, Document = normalizedDocument, PhoneNumber = input.PhoneNumber.Trim(), Company = input.Company.Trim(),
            Purpose = input.Purpose, VehiclePlate = normalizedPlate, PhotoUrl = guest.ImgUrl,
            Status = createFacialInvite ? AccessVisitStatusEnum.PendingEnrollment : input.RequireApproval ? AccessVisitStatusEnum.PendingApproval : AccessVisitStatusEnum.Scheduled, ValidFrom = validFrom, ValidTo = validTo,
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
                var recurring = CreateRecurringOccurrence(licenseId, host, hostUnit, input, photoReference, normalizedDocument, normalizedPlate,
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
        if (!input.RequireApproval && !createFacialInvite) Queue(licenseId, credentialId, input.IdempotencyKey, CurrentUser());
        Audit(licenseId, visit.Id, "VisitCreated", createFacialInvite ? "PendingEnrollment" : "Queued",
            createFacialInvite ? $"Convite facial temporário criado para {visit.VisitorName}." : $"Visita de {visit.VisitorName} para {host.Name} agendada.",
            new { input.HostResidentId, input.VisitorName, input.CredentialType, validFrom, validTo, input.MaxUses, input.RouteIds, createFacialInvite, HasPhoto = input.CredentialType == AccessCredentialTypeEnum.Face && !string.IsNullOrWhiteSpace(input.ImageBase64) });
        await context.SaveChangesAsync();
        string facialInviteUrl = string.Empty;
        if (createFacialInvite)
        {
            var issued = await facialInvites.IssueAsync(licenseId, visit.Id, CurrentUser(), validTo, HttpContext.RequestAborted);
            facialInviteUrl = issued.Url;
        }
        await NotifyVisitAsync(
            visit,
            input.RequireApproval ? "Visitante aguardando aprovacao" : "Visita agendada",
            $"{visit.VisitorName} foi registrado para o seu endereco.",
            $"visitor-created:{visit.Id:N}");
        return Created("", ToOut(visit, host, credential, facialInviteUrl, hostUnit));
    }

    internal static IQueryable<VisitorRouteCandidate> VisitorRouteQuery(
        DatabaseContext database,
        Guid licenseId) =>
        database.AccessRoutes
            .AsNoTracking()
            .IgnoreAutoIncludes()
            .Where(route =>
                route.LicenseId == licenseId &&
                route.IsActive &&
                route.AllowTemporary &&
                (route.Audience & AccessRouteAudienceEnum.Visitor) != 0)
            .Select(route => new VisitorRouteCandidate
            {
                Id = route.Id,
                Devices = route.Devices
                    .Select(link => new VisitorRouteDeviceCandidate
                    {
                        IsActive = link.IsActive,
                        DeviceType = link.Device.Type
                    })
                    .ToList()
            });

    internal sealed class VisitorRouteCandidate
    {
        public Guid Id { get; init; }
        public List<VisitorRouteDeviceCandidate> Devices { get; init; } = [];
    }

    internal sealed class VisitorRouteDeviceCandidate
    {
        public bool IsActive { get; init; }
        public DeviceTypeEnum DeviceType { get; init; }
    }

    internal static void NormalizeCreateVisitInput(CreateConciergeVisitIn input)
    {
        input.VisitorName = Clean(input.VisitorName);
        input.Document = Clean(input.Document);
        input.PhoneNumber = Clean(input.PhoneNumber);
        input.Company = Clean(input.Company);
        input.Purpose = Clean(input.Purpose);
        input.VehiclePlate = Clean(input.VehiclePlate);
        input.ImageBase64 = Clean(input.ImageBase64);
        input.IdempotencyKey = Clean(input.IdempotencyKey);
        input.RouteIds ??= [];
    }

    internal static string? ValidateCreateVisitInput(CreateConciergeVisitIn input)
    {
        if (string.IsNullOrWhiteSpace(input.VisitorName)) return "Informe o nome do visitante.";
        if (input.VisitorName.Length > 150) return "O nome do visitante deve ter no máximo 150 caracteres.";
        if (NormalizeDocument(input.Document).Length > 14) return "O documento deve ter no máximo 14 caracteres alfanuméricos.";
        if (input.PhoneNumber.Length > 20) return "O telefone deve ter no máximo 20 caracteres.";
        if (input.Company.Length > 150) return "A empresa deve ter no máximo 150 caracteres.";
        if (input.Purpose.Length > 200) return "O motivo da visita deve ter no máximo 200 caracteres.";
        if (NormalizePlate(input.VehiclePlate).Length > 20) return "A placa deve ter no máximo 20 caracteres alfanuméricos.";
        if (input.IdempotencyKey.Length > 140) return "A chave de idempotência é inválida.";
        if (input.RouteIds.Count > 100) return "Selecione no máximo 100 rotas.";
        return null;
    }

    [HttpPost("visits/{visitId:guid}/facial-invite")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> ReissueFacialInvite(Guid licenseId, Guid visitId)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var visit = await VisitQuery(licenseId).FirstOrDefaultAsync(x => x.Id == visitId);
        if (visit is null) return NotFound();
        if (visit.Credential.CredentialType != AccessCredentialTypeEnum.Face)
            return Conflict(new { Errors = "Esta visita não utiliza convite facial." });
        if (visit.FacialInvite?.Status == VisitFacialInviteStatusEnum.Completed)
            return Conflict(new { Errors = "O visitante já concluiu o cadastro facial." });
        if (visit.ValidTo <= DateTime.UtcNow)
            return Conflict(new { Errors = "A autorização da visita já expirou." });

        visit.Status = AccessVisitStatusEnum.PendingEnrollment;
        visit.Credential.IsActive = false;
        visit.UpdatedAt = DateTime.UtcNow;
        var issued = await facialInvites.IssueAsync(licenseId, visit.Id, CurrentUser(), visit.ValidTo, HttpContext.RequestAborted);
        Audit(licenseId, visit.Id, "FacialInviteReissued", "PendingEnrollment", $"Novo link facial emitido para {visit.VisitorName}.", new { visit.ValidTo });
        await context.SaveChangesAsync();
        return Ok(new VisitFacialInviteIssuedOut { VisitId = visit.Id, Status = "Pending", InviteUrl = issued.Url, ExpiresAt = issued.ExpiresAt });
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
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(
            visit,
            input.Approved ? "Visita aprovada" : "Visita recusada",
            $"A autorizacao de {visit.VisitorName} foi {(input.Approved ? "aprovada" : "recusada")}.",
            $"visitor-approval:{visit.Id:N}:{input.Approved}");
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
            if (visit.FacialInvite?.Status is VisitFacialInviteStatusEnum.Pending or VisitFacialInviteStatusEnum.Opened)
            {
                visit.FacialInvite.Status = VisitFacialInviteStatusEnum.Canceled;
                visit.FacialInvite.UpdatedAt = now;
            }
            Queue(licenseId, visit.CredentialId, Guid.NewGuid().ToString("N"), CurrentUser());
        }
        Audit(licenseId, visit.Id, "VisitStatus", "Success", $"Visita alterada para {input.Status}. {input.Reason}", input);
        await context.SaveChangesAsync();
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(
            visit,
            "Atualizacao da visita",
            $"{visit.VisitorName}: {input.Status}.",
            $"visitor-status:{visit.Id:N}:{input.Status}");
        return Ok(ToOut(visit));
    }

    [HttpPost("visits/scan")]
    [RequireLicensePermission(LicensePermissionEnum.ManagePeople)]
    public async Task<IActionResult> ScanVisit(Guid licenseId, [FromBody] ScanConciergeVisitIn input)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var code = NormalizeScannedCode(input.Code);
        if (string.IsNullOrWhiteSpace(code))
            return BadRequest(new { Errors = "Leia um QR Code valido." });

        var visit = await VisitQuery(licenseId)
            .FirstOrDefaultAsync(x => x.Credential.Identifier.ToUpper() == code);
        if (visit is null) return NotFound(new { Errors = "Este convite nao foi encontrado neste condominio." });
        if (visit.Status == AccessVisitStatusEnum.CheckedIn)
            return Conflict(new { Errors = "A entrada deste visitante ja foi registrada." });
        if (visit.Status != AccessVisitStatusEnum.Scheduled)
            return Conflict(new { Errors = $"Este convite esta {visit.Status} e nao pode ser utilizado." });

        var now = DateTime.UtcNow;
        if (!visit.Credential.IsActive || now < visit.ValidFrom || now > visit.ValidTo)
            return Conflict(new { Errors = "O convite esta fora da janela de acesso autorizada." });
        if (visit.Credential.MaxUses.HasValue && visit.Credential.UseCount >= visit.Credential.MaxUses.Value)
            return Conflict(new { Errors = "O limite de acessos deste convite foi atingido." });

        visit.Status = AccessVisitStatusEnum.CheckedIn;
        visit.CheckedInAt = now;
        visit.UpdatedAt = now;
        visit.Credential.UseCount++;
        visit.Credential.UpdatedAt = now;
        Audit(licenseId, visit.Id, "VisitQrScanned", "Success", $"Entrada de {visit.VisitorName} validada por QR Code.", new { Code = code });
        await context.SaveChangesAsync();
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(visit, "Visitante na portaria", $"A entrada de {visit.VisitorName} foi registrada.", $"visitor-scan:{visit.Id:N}");
        return Ok(ToOut(visit));
    }

    private Task NotifyVisitAsync(AccessVisitDTO visit, string title, string body, string key) =>
        push?.NotifyResidentAsync(
            visit.HostResidentId,
            Domain.Enums.Mobile.MobileNotificationCategory.Visitor,
            title,
            body,
            $"/visitors/{visit.Id:D}",
            key,
            HttpContext.RequestAborted) ?? Task.CompletedTask;

    private IQueryable<AccessVisitDTO> VisitQuery(Guid licenseId) => context.AccessVisits
        .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.GuestResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
        .Include(x => x.Credential).Include(x => x.FacialInvite).Where(x => x.LicenseId == licenseId);
    private AccessVisitDTO CreateRecurringOccurrence(Guid licenseId, ResidentAccessDTO host, UnitDTO hostUnit, CreateConciergeVisitIn input,
        string photoReference, string document, string plate, DateTime validFrom, DateTime validTo, Guid groupId, int sequence, DateTime now)
    {
        var guestId = Guid.NewGuid(); var credentialId = Guid.NewGuid();
        var guest = new ResidentAccessDTO
        {
            Id = guestId, UnitId = hostUnit.Id, Name = input.VisitorName.Trim(), Email = string.Empty, Password = string.Empty,
            PhoneNumber = input.PhoneNumber, CommercialPhone = string.Empty, CPF = document, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = hostUnit.Number, ImgUrl = photoReference, Description = input.Purpose,
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
            Id = Guid.NewGuid(), ResidentId = guestId, UnitId = hostUnit.Id, Relationship = ResidentUnitRelationshipEnum.Resident,
            Description = $"Visitante recorrente de {host.Name}", IsPrimary = true, IsActive = true, StartsAt = validFrom, EndsAt = validTo, CreatedAt = now, UpdatedAt = now
        });
        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, HostResidentId = host.Id, GuestResidentId = guestId, CredentialId = credentialId,
            VisitorName = guest.Name, Document = document, PhoneNumber = input.PhoneNumber, Company = input.Company,
            Purpose = input.Purpose, VehiclePlate = plate, PhotoUrl = photoReference,
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
    private static ConciergeVisitOut ToOut(AccessVisitDTO x, ResidentAccessDTO host, ResidentAccessCredentialDTO credential, string facialInviteUrl = "", UnitDTO? selectedUnit = null)
    {
        var unit = selectedUnit ?? x.GuestResident?.Unit ?? host.Unit;
        return new ConciergeVisitOut
        {
            Id = x.Id, LicenseId = x.LicenseId, HostResidentId = x.HostResidentId, HostName = host.Name,
            BlockName = unit?.Block?.Name ?? string.Empty, UnitNumber = unit?.Number ?? string.Empty,
            VisitorName = x.VisitorName, Document = x.Document, PhoneNumber = x.PhoneNumber, Company = x.Company, Purpose = x.Purpose,
            VehiclePlate = x.VehiclePlate, PhotoUrl = x.PhotoUrl, Status = x.Status.ToString(), CredentialType = credential.CredentialType.ToString(),
            CredentialCode = credential.Identifier, UseCount = credential.UseCount, MaxUses = credential.MaxUses,
            ValidFrom = x.ValidFrom, ValidTo = x.ValidTo, CheckedInAt = x.CheckedInAt, CheckedOutAt = x.CheckedOutAt,
            ApprovalRequired = x.ApprovalRequired, ApprovedBy = x.ApprovedBy, ApprovedAt = x.ApprovedAt, ApprovalNotes = x.ApprovalNotes,
            ExpectedCheckoutAt = x.ExpectedCheckoutAt, IsOverstayed = x.Status == AccessVisitStatusEnum.CheckedIn && (x.ExpectedCheckoutAt ?? x.ValidTo) < DateTime.UtcNow,
            RecurrenceSequence = x.RecurrenceSequence, RecurrenceCount = x.RecurrenceCount,
            FacialInviteStatus = x.FacialInvite?.Status.ToString() ?? (x.Status == AccessVisitStatusEnum.PendingEnrollment ? "Pending" : string.Empty),
            FacialInviteUrl = facialInviteUrl
        };
    }
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
    private static string NormalizePlate(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string NormalizeDocument(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static string NormalizeScannedCode(string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            if (query.TryGetValue("code", out var code)) raw = code.ToString();
            else raw = uri.Segments.LastOrDefault()?.Trim('/') ?? raw;
        }
        return raw.Trim().ToUpperInvariant();
    }
    private static AccessWatchlistEntryOut ToWatchlistOut(AccessWatchlistEntryDTO x) => new() { Id = x.Id, Name = x.Name, Document = x.Document, VehiclePlate = x.VehiclePlate, Reason = x.Reason, Severity = x.Severity, ExpiresAt = x.ExpiresAt };
    private static DateTime Utc(DateTime value) => value.ToCondotifyUtc();
    private static bool IsValidTransition(AccessVisitStatusEnum current, AccessVisitStatusEnum next) =>
        (current, next) switch
        {
            (AccessVisitStatusEnum.Scheduled, AccessVisitStatusEnum.CheckedIn or AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied) => true,
            (AccessVisitStatusEnum.PendingEnrollment, AccessVisitStatusEnum.Canceled or AccessVisitStatusEnum.Denied) => true,
            (AccessVisitStatusEnum.CheckedIn, AccessVisitStatusEnum.CheckedOut) => true,
            _ => false
        };
}
