using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed record OfflineBundleBuildResult(
    OfflineAccessBundlePayloadViewModel Payload,
    OfflineAccessBundleEnvelopeViewModel Envelope);

public interface IOfflineAccessBundleService
{
    Task<OfflineBundleBuildResult> BuildAsync(OfflineAccessDeviceDTO device, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, List<OfflineRouteWindowViewModel>>> ResolveRouteWindowsAsync(
        Guid licenseId,
        IReadOnlyCollection<Guid> residentIds,
        CancellationToken cancellationToken = default);
}

public sealed class OfflineAccessBundleService(DatabaseContext context) : IOfflineAccessBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<OfflineBundleBuildResult> BuildAsync(
        OfflineAccessDeviceDTO device,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(Math.Clamp(device.OfflineWindowMinutes, 15, 720));
        var visits = await context.AccessVisits.AsNoTracking()
            .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Credential)
            .Where(x => x.LicenseId == device.LicenseId &&
                        (x.Status == AccessVisitStatusEnum.Scheduled || x.Status == AccessVisitStatusEnum.CheckedIn) &&
                        x.Credential.CredentialType == AccessCredentialTypeEnum.QrCode &&
                        ((x.Status == AccessVisitStatusEnum.Scheduled && x.Credential.IsActive && x.ValidTo >= now && x.ValidFrom <= expiresAt) ||
                         (x.Status == AccessVisitStatusEnum.CheckedIn && x.CheckedInAt >= now.AddDays(-7))))
            .OrderBy(x => x.ValidFrom)
            .Take(3000)
            .ToListAsync(cancellationToken);

        var routeWindows = await ResolveRouteWindowsAsync(
            device.LicenseId,
            visits.Select(x => x.GuestResidentId).Distinct().ToArray(),
            cancellationToken);
        var licenseName = await context.Licenses.AsNoTracking()
            .Where(x => x.Id == device.LicenseId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var payload = new OfflineAccessBundlePayloadViewModel
        {
            BundleId = Guid.NewGuid(),
            DeviceId = device.Id,
            LicenseId = device.LicenseId,
            LicenseName = licenseName,
            GeneratedAt = now,
            ExpiresAt = expiresAt,
            ServerTime = now,
            UtcOffsetMinutes = OfflineOperationsTimeZone.OffsetMinutes(now),
            IsPrimaryValidator = device.IsPrimaryValidator,
            Visits = visits.Select(visit => new OfflineVisitPermitViewModel
            {
                VisitId = visit.Id,
                CodeHash = OfflineAccessCode.Hash(visit.Credential.Identifier),
                VisitorName = visit.VisitorName,
                HostName = visit.HostResident.Name,
                BlockName = visit.HostResident.Unit.Block.Name,
                UnitNumber = visit.HostResident.Unit.Number,
                Purpose = visit.Purpose,
                VehiclePlate = visit.VehiclePlate,
                Status = visit.Status.ToString(),
                ValidFrom = visit.ValidFrom,
                ValidTo = visit.ValidTo,
                UseCount = visit.Credential.UseCount,
                MaxUses = visit.Credential.MaxUses,
                Routes = routeWindows.GetValueOrDefault(visit.GuestResidentId) ?? []
            }).ToList()
        };
        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        var payloadBase64 = Convert.ToBase64String(payloadBytes);
        var envelope = new OfflineAccessBundleEnvelopeViewModel
        {
            KeyId = device.Id.ToString("N"),
            PayloadBase64 = payloadBase64,
            Signature = OfflineBundleAuthenticator.Sign(payloadBase64, device.DeviceSecret)
        };

        return new OfflineBundleBuildResult(payload, envelope);
    }

    public async Task<IReadOnlyDictionary<Guid, List<OfflineRouteWindowViewModel>>> ResolveRouteWindowsAsync(
        Guid licenseId,
        IReadOnlyCollection<Guid> residentIds,
        CancellationToken cancellationToken = default)
    {
        if (residentIds.Count == 0) return new Dictionary<Guid, List<OfflineRouteWindowViewModel>>();
        var ids = residentIds.Distinct().ToArray();
        var routes = await context.AccessRoutes.AsNoTracking()
            .Include(x => x.ResidentOverrides.Where(item => ids.Contains(item.ResidentId)))
            .Where(x => x.LicenseId == licenseId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var output = new Dictionary<Guid, List<OfflineRouteWindowViewModel>>(ids.Length);
        foreach (var residentId in ids)
        {
            output[residentId] = routes.Where(route =>
                route.ResidentOverrides.FirstOrDefault(x => x.ResidentId == residentId)?.Mode switch
                {
                    AccessRouteOverrideModeEnum.Exclude => false,
                    AccessRouteOverrideModeEnum.Include => true,
                    _ => route.AllowTemporary && (route.Audience & AccessRouteAudienceEnum.Visitor) != 0
                })
                .Select(route => new OfflineRouteWindowViewModel
                {
                    RouteId = route.Id,
                    Name = route.Name,
                    DaysOfWeekMask = route.DaysOfWeekMask,
                    StartTime = route.StartTime,
                    EndTime = route.EndTime
                }).ToList();
        }

        return output;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

internal static class OfflineOperationsTimeZone
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static int OffsetMinutes(DateTime utc) => (int)Zone.GetUtcOffset(
        utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime()).TotalMinutes;

    private static TimeZoneInfo Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("CONDOTIFY_TIME_ZONE");
        foreach (var id in new[] { configured, "America/Sao_Paulo", "E. South America Standard Time" })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
