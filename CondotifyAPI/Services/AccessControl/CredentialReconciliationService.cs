using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Services.AccessControl;

public interface ICredentialReconciliationService
{
    Task<CredentialReconciliationResult> ReconcileCredentialAsync(
        Guid credentialId,
        string actor,
        string? imageBase64 = null,
        Guid? licenseId = null,
        CancellationToken cancellationToken = default);
}

public sealed record CredentialReconciliationResult(
    bool Success,
    int TargetCount,
    int SyncedCount,
    int FailedCount,
    int RemovedCount,
    string Message);

public sealed class CredentialReconciliationService : ICredentialReconciliationService
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControl;
    private readonly IAccessRouteResolver _routeResolver;
    private readonly IMapper _mapper;
    private readonly ILogger<CredentialReconciliationService> _logger;
    private readonly IPrivateMediaStore _media;

    public CredentialReconciliationService(
        DatabaseContext context,
        IAccessControlService accessControl,
        IAccessRouteResolver routeResolver,
        IMapper mapper,
        ILogger<CredentialReconciliationService> logger,
        IPrivateMediaStore media)
    {
        _context = context;
        _accessControl = accessControl;
        _routeResolver = routeResolver;
        _mapper = mapper;
        _logger = logger;
        _media = media;
    }

    public async Task<CredentialReconciliationResult> ReconcileCredentialAsync(
        Guid credentialId,
        string actor,
        string? imageBase64 = null,
        Guid? licenseId = null,
        CancellationToken cancellationToken = default)
    {
        var credential = await _context.ResidentAccessCredentials
            .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident).ThenInclude(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Devices)
            .FirstOrDefaultAsync(x => x.Id == credentialId, cancellationToken);
        if (credential is null)
            return new(false, 0, 0, 0, 0, "Credencial nao encontrada.");

        var resident = credential.Resident;
        var selectedLicenseId = licenseId ?? ResidentLicenseScope.ResolvePrimaryUnit(resident)?.Block?.LicenseId;
        if (!selectedLicenseId.HasValue || selectedLicenseId.Value == Guid.Empty ||
            ResidentLicenseScope.ResolveUnitForLicense(resident, selectedLicenseId.Value) is null)
            return new(false, 0, 0, 0, 0, "A pessoa nao possui uma unidade valida para reconciliar a credencial.");
        var resolution = await _routeResolver.ResolveAsync(selectedLicenseId.Value, resident, credential.CredentialType);
        var devices = await _context.Devices
            .Where(x => x.LicenseId == selectedLicenseId.Value)
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var targetIds = credential.IsActive
            ? resolution.Targets.Select(x => x.Device.Id).ToHashSet()
            : [];
        var now = DateTime.UtcNow;
        var removed = 0;
        var failures = 0;

        foreach (var stale in credential.Devices.Where(x => !targetIds.Contains(x.DeviceId)).ToList())
        {
            if (!devices.TryGetValue(stale.DeviceId, out var staleDevice))
            {
                _context.ResidentAccessDevices.Remove(stale);
                removed++;
                continue;
            }

            var result = await ExecuteSafelyAsync(() => _accessControl.RemoveCredentialAsync(
                _mapper.Map<AccessControlDevice>(staleDevice), BuildRequest(credential, resident, stale, null, null)));
            if (result.Success)
            {
                _context.ResidentAccessDevices.Remove(stale);
                removed++;
            }
            else
            {
                UpdateBinding(stale, result, CredentialSyncStatusEnum.RemovalPending, now, null);
                failures++;
            }
            AddDeviceAudit(staleDevice.Id, result.Success ? "Removed" : "RemovalPending", actor,
                $"Reconciliacao de rota | {resident.Name} | {result.Message}");
        }

        var synced = 0;
        var activeTargets = credential.IsActive
            ? resolution.Targets
            : Array.Empty<ResolvedAccessRouteTarget>();

        foreach (var target in activeTargets)
        {
            var binding = credential.Devices.FirstOrDefault(x => x.DeviceId == target.Device.Id);
            var photo = credential.CredentialType == AccessCredentialTypeEnum.Face
                ? await _media.ResolveDataUriAsync(selectedLicenseId.Value, imageBase64 ?? resident.ImgUrl, cancellationToken)
                : null;
            if (credential.CredentialType == AccessCredentialTypeEnum.Face && string.IsNullOrWhiteSpace(photo))
            {
                var missingPhoto = CredentialOperationResult.Fail("A pessoa nao possui foto preparada para sincronizacao.");
                if (binding is null)
                {
                    binding = NewBinding(credential, target.Device, now);
                    _context.ResidentAccessDevices.Add(binding);
                }
                UpdateBinding(binding, missingPhoto, CredentialSyncStatusEnum.Failed, now, target.Portals);
                failures++;
                continue;
            }

            if (binding is not null)
            {
                binding.SyncStatus = CredentialSyncStatusEnum.Syncing;
                binding.NextAttemptAt = null;
            }

            var operation = await ExecuteSafelyAsync(() => _accessControl.UpsertCredentialAsync(
                _mapper.Map<AccessControlDevice>(target.Device),
                BuildRequest(credential, resident, binding, photo, target.Portals)));
            if (binding is null)
            {
                binding = NewBinding(credential, target.Device, now);
                _context.ResidentAccessDevices.Add(binding);
            }
            UpdateBinding(binding, operation, operation.Success ? CredentialSyncStatusEnum.Synced : CredentialSyncStatusEnum.Failed, now, target.Portals);
            if (operation.Success) synced++; else failures++;
            AddDeviceAudit(target.Device.Id, operation.Success ? "Synced" : "Pending", actor,
                $"Rotas {string.Join(", ", target.Portals.Select(x => x.RouteName).Distinct())} | {resident.Name} | {operation.Message}");
        }

        var success = resolution.Targets.Count > 0 && failures == 0;
        var summary = resolution.Targets.Count == 0
            ? "Nenhuma rota ativa atende a esta credencial. Vinculos antigos foram removidos ou marcados como pendentes."
            : $"{synced} de {resolution.Targets.Count} terminal(is) sincronizado(s); {failures} pendencia(s); {removed} vinculo(s) removido(s).";
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = selectedLicenseId.Value, EntityType = "Credential", EntityId = credential.Id,
            Action = "Reconcile", Status = success ? "Success" : failures > 0 ? "Pending" : "NoRoute",
            Summary = summary, DetailsJson = JsonSerializer.Serialize(new { resolution.RouteNames, TargetCount = resolution.Targets.Count, synced, failures, removed }),
            UserName = actor, CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
        return new(success, resolution.Targets.Count, synced, failures, removed, summary);
    }

    private static CredentialProvisionRequest BuildRequest(
        ResidentAccessCredentialDTO credential,
        ResidentAccessDTO resident,
        ResidentAccessDeviceDTO? binding,
        string? image,
        IReadOnlyList<AccessPortalAssignment>? portals) =>
        new(credential.Id, resident.Name, AccessControlDeviceRegistration.FromResidentId(resident.Id),
            credential.CredentialType, credential.Identifier, image, credential.ValidFrom, credential.ValidTo,
            credential.IsActive, binding?.ExternalUserId, binding?.ExternalCredentialId, portals);

    private static ResidentAccessDeviceDTO NewBinding(
        ResidentAccessCredentialDTO credential,
        AccessControlDeviceDTO device,
        DateTime now) => new()
    {
        Id = Guid.NewGuid(), ResidentAccessCredentialId = credential.Id, Credential = credential,
        DeviceId = device.Id, DeviceType = device.Type, LastSyncAt = now,
        SyncStatus = CredentialSyncStatusEnum.Pending
    };

    private static void UpdateBinding(
        ResidentAccessDeviceDTO binding,
        CredentialOperationResult result,
        CredentialSyncStatusEnum status,
        DateTime now,
        IReadOnlyList<AccessPortalAssignment>? portals)
    {
        binding.ExternalUserId = result.ExternalUserId ?? binding.ExternalUserId ?? string.Empty;
        binding.ExternalCredentialId = result.ExternalCredentialId ?? binding.ExternalCredentialId ?? string.Empty;
        binding.IsSynced = result.Success && status == CredentialSyncStatusEnum.Synced;
        binding.SyncStatus = status;
        binding.LastSyncAt = now;
        binding.AttemptCount++;
        binding.ExtraJson = JsonSerializer.Serialize(new { result.Success, result.Message });
        binding.RouteNames = portals is null ? binding.RouteNames : string.Join(", ", portals.Select(x => x.RouteName).Distinct());
        binding.PortalNumbers = portals is null ? binding.PortalNumbers : string.Join(",", portals.Select(x => x.PortalNumber).Distinct().OrderBy(x => x));
        if (result.Success)
        {
            binding.LastSuccessAt = now;
            binding.LastErrorAt = null;
            binding.NextAttemptAt = null;
        }
        else
        {
            binding.LastErrorAt = now;
            binding.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, binding.AttemptCount))));
        }
    }

    private async Task<CredentialOperationResult> ExecuteSafelyAsync(Func<Task<CredentialOperationResult>> operation)
    {
        try { return await operation(); }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha durante reconciliacao de credencial");
            return CredentialOperationResult.Fail(exception.Message);
        }
    }

    private void AddDeviceAudit(Guid deviceId, string status, string actor, string details) =>
        _context.DeviceAudits.Add(new DeviceAuditDTO
        {
            Id = Guid.NewGuid(), DeviceId = deviceId, Action = ActionTypeEnum.ProvisionCredential,
            ChangedFields = $"{status} | {details}"[..Math.Min(500, $"{status} | {details}".Length)],
            Timestamp = DateTime.UtcNow, UserId = Guid.Empty, UserName = actor
        });
}
