using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class ExpiredCredentialCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredCredentialCleanupService> _logger;

    public ExpiredCredentialCleanupService(IServiceScopeFactory scopeFactory, ILogger<ExpiredCredentialCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CleanupAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var accessControl = scope.ServiceProvider.GetRequiredService<IAccessControlService>();
            var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
            var now = DateTime.UtcNow;

            var policies = await context.LicenseCredentialPolicies.AsNoTracking()
                .ToDictionaryAsync(x => x.LicenseId, cancellationToken);
            var credentials = await context.ResidentAccessCredentials
                .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
                .Include(x => x.Devices)
                .Where(x => x.IsActive && x.ValidTo <= now)
                .Take(500)
                .ToListAsync(cancellationToken);
            credentials = credentials.Where(x => x.IsTemporary ||
                (policies.TryGetValue(x.Resident.Unit.Block.LicenseId, out var policy) && policy.AutoDeactivateExpiredCredentials)).ToList();

            var expiredInvites = await context.VisitFacialInvites
                .Include(x => x.Visit).ThenInclude(x => x.Credential)
                .Where(x => (x.Status == VisitFacialInviteStatusEnum.Pending || x.Status == VisitFacialInviteStatusEnum.Opened) && x.ExpiresAt <= now)
                .Take(500)
                .ToListAsync(cancellationToken);
            foreach (var invite in expiredInvites)
            {
                invite.Status = VisitFacialInviteStatusEnum.Expired;
                invite.UpdatedAt = now;
                invite.Visit.Credential.IsActive = false;
                if (invite.Visit.Status == AccessVisitStatusEnum.PendingEnrollment)
                    invite.Visit.Status = AccessVisitStatusEnum.Expired;
                invite.Visit.UpdatedAt = now;
            }

            var expiredVisits = await context.AccessVisits
                .Include(x => x.Credential)
                .Where(x => (x.Status == AccessVisitStatusEnum.Scheduled || x.Status == AccessVisitStatusEnum.PendingEnrollment) && x.ValidTo <= now)
                .Take(500)
                .ToListAsync(cancellationToken);
            foreach (var visit in expiredVisits)
            {
                visit.Status = AccessVisitStatusEnum.Expired;
                visit.Credential.IsActive = false;
                visit.Credential.UpdatedAt = now;
                visit.UpdatedAt = now;
            }

            var deviceIds = credentials.SelectMany(x => x.Devices).Select(x => x.DeviceId).Distinct().ToArray();
            var devices = await context.Devices.AsNoTracking().Where(x => deviceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            foreach (var credential in credentials)
            {
                credential.IsActive = false;
                credential.UpdatedAt = now;
                var licenseId = credential.Resident.Unit.Block.LicenseId;
                var removeFromDevices = credential.IsTemporary ||
                    (policies.TryGetValue(licenseId, out var policy) && policy.RemoveExpiredCredentialsFromDevices);
                if (!removeFromDevices) continue;

                foreach (var binding in credential.Devices.ToList())
                {
                    if (!devices.TryGetValue(binding.DeviceId, out var device))
                    {
                        context.ResidentAccessDevices.Remove(binding);
                        continue;
                    }

                    if (!device.IsActive)
                    {
                        MarkRemovalPending(binding, now, "Equipamento offline durante a remoção da credencial temporária.");
                        AddRemovalAudit(context, licenseId, credential.Id, device.Id, credential.Resident.Name, device.Name, "WaitingDevice");
                        continue;
                    }

                    try
                    {
                        var request = new CredentialProvisionRequest(
                            credential.Id,
                            credential.Resident.Name,
                            AccessControlDeviceRegistration.FromResidentId(credential.ResidentId),
                            credential.CredentialType,
                            credential.Identifier,
                            null,
                            credential.ValidFrom,
                            credential.ValidTo,
                            false,
                            binding.ExternalUserId,
                            binding.ExternalCredentialId);
                        var result = await accessControl.RemoveCredentialAsync(mapper.Map<AccessControlDevice>(device), request);
                        if (result.Success)
                            context.ResidentAccessDevices.Remove(binding);
                        else
                        {
                            MarkRemovalPending(binding, now, result.Message ?? "Remoção pendente");
                            AddRemovalAudit(context, licenseId, credential.Id, device.Id, credential.Resident.Name, device.Name, "Pending");
                        }
                    }
                    catch (Exception exception)
                    {
                        MarkRemovalPending(binding, now, exception.Message);
                        AddRemovalAudit(context, licenseId, credential.Id, device.Id, credential.Resident.Name, device.Name, "WaitingDevice");
                        _logger.LogWarning(exception, "Falha ao remover a credencial expirada {CredentialId} do equipamento {DeviceId}", credential.Id, binding.DeviceId);
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            if (credentials.Count > 0 || expiredInvites.Count > 0 || expiredVisits.Count > 0)
                _logger.LogInformation("{Credentials} credencial(is), {Invites} convite(s) facial(is) e {Visits} visita(s) expirada(s) processada(s)", credentials.Count, expiredInvites.Count, expiredVisits.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha na rotina de expiracao de credenciais");
        }
    }

    private static void MarkRemovalPending(ResidentAccessDeviceDTO binding, DateTime now, string message)
    {
        binding.IsSynced = false;
        binding.SyncStatus = CondotifyAPI.Domain.Enums.AccessControl.CredentialSyncStatusEnum.RemovalPending;
        binding.AttemptCount++;
        binding.LastSyncAt = now;
        binding.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, Math.Max(1, binding.AttemptCount)))));
        binding.ExtraJson = JsonSerializer.Serialize(new { success = false, message });
    }

    private static void AddRemovalAudit(DatabaseContext context, Guid licenseId, Guid credentialId, Guid deviceId, string personName, string deviceName, string status)
    {
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Credential", EntityId = credentialId,
            Action = "ExpiredCredentialRemoval", Status = status,
            Summary = $"Remoção da credencial temporária de {personName} aguardando o equipamento {deviceName}.",
            DetailsJson = JsonSerializer.Serialize(new { deviceId, deviceName }), UserName = "Expiração automática", CreatedAt = DateTime.UtcNow
        });
    }
}
