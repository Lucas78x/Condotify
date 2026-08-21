using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Hubs;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class AccessEventIngestionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AccessEventIngestionWorker> _logger;
    private readonly TimeSpan _interval;
    private readonly Dictionary<Guid, DeviceRetryState> _retryByDevice = [];

    public AccessEventIngestionWorker(IServiceScopeFactory scopeFactory, ILogger<AccessEventIngestionWorker> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        var seconds = Math.Clamp(configuration.GetValue("AccessEvents:PollIntervalSeconds", 15), 5, 300);
        _interval = TimeSpan.FromSeconds(seconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await IngestAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task IngestAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();
            var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<ConciergeHub>>();
            var devices = await context.Devices.Where(x => x.IsActive).ToListAsync(cancellationToken);
            var reconciliationByLicense = new Dictionary<Guid, HashSet<Guid>>();
            var pendingPublish = new List<(Guid LicenseId, ConciergeEventOut Payload)>();

            foreach (var device in devices)
            {
                if (_retryByDevice.TryGetValue(device.Id, out var retry) && retry.NextAttemptAt > DateTime.UtcNow)
                    continue;
                try
                {
                    var events = await accessControl.GetAccessEventsAsync(mapper.Map<AccessControlDevice>(device), 200);
                    pendingPublish.AddRange(await PersistAsync(context, device.Id, device.LicenseId, device.Name, events, reconciliationByLicense, cancellationToken));
                    _retryByDevice.Remove(device.Id);
                    device.LastHealthCheckAt = DateTime.UtcNow;
                    device.LastSeenAt = DateTime.UtcNow;
                    device.HealthMessage = "Online; eventos atualizados.";
                }
                catch (Exception exception)
                {
                    var failures = (_retryByDevice.TryGetValue(device.Id, out var previous) ? previous.Failures : 0) + 1;
                    var retrySeconds = Math.Min(300, 15 * Math.Pow(2, Math.Min(failures, 5)));
                    _retryByDevice[device.Id] = new DeviceRetryState(failures, DateTime.UtcNow.AddSeconds(retrySeconds));
                    device.LastHealthCheckAt = DateTime.UtcNow;
                    device.HealthMessage = Short($"{exception.Message} Nova tentativa em {retrySeconds:0}s.", 300);
                    _logger.LogDebug(exception, "Nao foi possivel coletar eventos do equipamento {DeviceId}", device.Id);
                }
            }

            foreach (var (licenseId, credentialIds) in reconciliationByLicense.Where(x => x.Value.Count > 0))
            {
                context.AccessBatchOperations.Add(new AccessBatchOperationDTO
                {
                    Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials",
                    Status = AccessBatchStatusEnum.Queued, RequestedBy = "Limite automatico de utilizacoes",
                    FilterJson = JsonSerializer.Serialize(new { credentialIds }), CreatedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync(cancellationToken);

            foreach (var (licenseId, payload) in pendingPublish)
                await hub.Clients.Group(ConciergeHub.GroupName(licenseId)).SendAsync("AccessEventRecorded", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha na coleta automatica de eventos de acesso");
        }
    }

    private static async Task<List<(Guid LicenseId, ConciergeEventOut Payload)>> PersistAsync(
        DatabaseContext context,
        Guid deviceId,
        Guid licenseId,
        string deviceName,
        IReadOnlyList<DeviceAccessEvent> events,
        Dictionary<Guid, HashSet<Guid>> reconciliationByLicense,
        CancellationToken cancellationToken)
    {
        var published = new List<(Guid, ConciergeEventOut)>();
        if (events.Count == 0) return published;
        var externalIds = events.Select(x => StableEventId(x)).Distinct().ToList();
        var existingIds = await context.AccessEventRecords.AsNoTracking()
            .Where(x => x.DeviceId == deviceId && externalIds.Contains(x.ExternalEventId))
            .Select(x => x.ExternalEventId).ToHashSetAsync(cancellationToken);
        var bindings = await context.ResidentAccessDevices
            .Include(x => x.Credential).ThenInclude(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Where(x => x.DeviceId == deviceId)
            .ToListAsync(cancellationToken);
        var credentialIds = bindings.Select(x => x.ResidentAccessCredentialId).Distinct().ToList();
        var visits = await context.AccessVisits.AsNoTracking().Include(x => x.HostResident)
            .Where(x => credentialIds.Contains(x.CredentialId))
            .OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
        var visitByCredential = visits.GroupBy(x => x.CredentialId).ToDictionary(x => x.Key, x => x.First());

        foreach (var accessEvent in events.OrderBy(x => x.OccurredAt))
        {
            var externalId = StableEventId(accessEvent);
            if (!existingIds.Add(externalId)) continue;
            var binding = ResolveBinding(bindings, accessEvent);
            var credential = binding?.Credential;
            var resident = credential?.Resident;
            var visit = credential is null ? null : visitByCredential.GetValueOrDefault(credential.Id);
            var recordId = Guid.NewGuid();
            context.AccessEventRecords.Add(new AccessEventRecordDTO
            {
                Id = recordId, LicenseId = licenseId, DeviceId = deviceId, CredentialId = credential?.Id,
                ExternalEventId = externalId, Event = Short(accessEvent.Event, 120), Authorized = accessEvent.Authorized,
                OccurredAt = Utc(accessEvent.OccurredAt), ExternalUserId = Short(accessEvent.ExternalUserId, 150),
                PersonName = Short(accessEvent.PersonName, 200), Credential = Short(accessEvent.Credential, 200),
                Portal = Short(accessEvent.Portal, 120), Details = Short(accessEvent.Details, 1000), CreatedAt = DateTime.UtcNow
            });
            published.Add((licenseId, new ConciergeEventOut
            {
                Id = recordId, DeviceId = deviceId, CredentialId = credential?.Id, ResidentId = resident?.Id,
                UnitId = resident?.UnitId, VisitId = visit?.Id, DeviceName = deviceName,
                PersonName = Short(accessEvent.PersonName, 200), PhotoUrl = resident?.ImgUrl ?? string.Empty,
                PhoneNumber = resident?.PhoneNumber ?? string.Empty, BlockName = resident?.Unit?.Block?.Name ?? string.Empty,
                UnitNumber = resident?.Unit?.Number ?? string.Empty, CredentialType = credential?.CredentialType.ToString() ?? string.Empty,
                Credential = Short(accessEvent.Credential, 200), CredentialActive = credential?.IsActive,
                CredentialValidFrom = credential?.ValidFrom, CredentialValidTo = credential?.ValidTo, Details = Short(accessEvent.Details, 1000),
                HostName = visit?.HostResident?.Name ?? string.Empty, HostPhoneNumber = visit?.HostResident?.PhoneNumber ?? string.Empty,
                Event = Short(accessEvent.Event, 120), Authorized = accessEvent.Authorized,
                Portal = Short(accessEvent.Portal, 120), OccurredAt = Utc(accessEvent.OccurredAt),
                RequiresAttention = !accessEvent.Authorized
            }));

            if (!accessEvent.Authorized || credential is null || !credential.IsActive) continue;
            credential.UseCount++;
            credential.UpdatedAt = DateTime.UtcNow;
            if (credential.MaxUses is not > 0 || credential.UseCount < credential.MaxUses) continue;
            credential.IsActive = false;
            foreach (var item in credential.Devices)
            {
                item.IsSynced = false;
                item.SyncStatus = CredentialSyncStatusEnum.RemovalPending;
                item.NextAttemptAt = DateTime.UtcNow;
            }
            if (!reconciliationByLicense.TryGetValue(licenseId, out var ids))
                reconciliationByLicense[licenseId] = ids = [];
            ids.Add(credential.Id);
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Credential", EntityId = credential.Id,
                Action = "UsageLimitReached", Status = "Queued",
                Summary = $"{credential.Resident.Name}: limite de {credential.MaxUses} utilizacao(oes) atingido.",
                DetailsJson = JsonSerializer.Serialize(new { credential.UseCount, credential.MaxUses, deviceId, externalId }),
                UserName = "Monitor automatico", CreatedAt = DateTime.UtcNow
            });
        }
        return published;
    }

    private static ResidentAccessDeviceDTO? ResolveBinding(IEnumerable<ResidentAccessDeviceDTO> bindings, DeviceAccessEvent accessEvent)
    {
        if (!string.IsNullOrWhiteSpace(accessEvent.ExternalUserId))
        {
            var byUser = bindings.FirstOrDefault(x => string.Equals(x.ExternalUserId, accessEvent.ExternalUserId, StringComparison.OrdinalIgnoreCase));
            if (byUser is not null) return byUser;
        }
        return string.IsNullOrWhiteSpace(accessEvent.Credential)
            ? null
            : bindings.FirstOrDefault(x => string.Equals(x.Credential.Identifier, accessEvent.Credential, StringComparison.OrdinalIgnoreCase));
    }

    private static string StableEventId(DeviceAccessEvent accessEvent)
    {
        if (!string.IsNullOrWhiteSpace(accessEvent.ExternalId)) return Short(accessEvent.ExternalId, 200);
        var source = $"{accessEvent.OccurredAt:O}|{accessEvent.ExternalUserId}|{accessEvent.Credential}|{accessEvent.Portal}|{accessEvent.Event}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Short(string? value, int length) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= length ? value : value[..length];
    private sealed record DeviceRetryState(int Failures, DateTime NextAttemptAt);
}
