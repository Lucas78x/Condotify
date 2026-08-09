using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class ExpiredCredentialCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
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
                .Where(x => x.AutoDeactivateExpiredCredentials)
                .ToDictionaryAsync(x => x.LicenseId, cancellationToken);
            if (policies.Count == 0) return;

            var licenseIds = policies.Keys.ToArray();
            var credentials = await context.ResidentAccessCredentials
                .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
                .Include(x => x.Devices)
                .Where(x => x.IsActive && x.ValidTo <= now && licenseIds.Contains(x.Resident.Unit.Block.LicenseId))
                .ToListAsync(cancellationToken);
            if (credentials.Count == 0) return;

            var deviceIds = credentials.SelectMany(x => x.Devices).Select(x => x.DeviceId).Distinct().ToArray();
            var devices = await context.Devices.AsNoTracking().Where(x => deviceIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            foreach (var credential in credentials)
            {
                credential.IsActive = false;
                credential.UpdatedAt = now;
                var licenseId = credential.Resident.Unit.Block.LicenseId;
                if (!policies[licenseId].RemoveExpiredCredentialsFromDevices) continue;

                foreach (var binding in credential.Devices.ToList())
                {
                    if (!devices.TryGetValue(binding.DeviceId, out var device))
                    {
                        context.ResidentAccessDevices.Remove(binding);
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
                            binding.IsSynced = false;
                            binding.LastSyncAt = now;
                            binding.ExtraJson = JsonSerializer.Serialize(new { success = false, message = result.Message ?? "Remocao pendente" });
                        }
                    }
                    catch (Exception exception)
                    {
                        binding.IsSynced = false;
                        binding.LastSyncAt = now;
                        binding.ExtraJson = JsonSerializer.Serialize(new { success = false, message = exception.Message });
                        _logger.LogWarning(exception, "Falha ao remover a credencial expirada {CredentialId} do equipamento {DeviceId}", credential.Id, binding.DeviceId);
                    }
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("{Count} credencial(is) expirada(s) processada(s)", credentials.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha na rotina de expiracao de credenciais");
        }
    }
}
