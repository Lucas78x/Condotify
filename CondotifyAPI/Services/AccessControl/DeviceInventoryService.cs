using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public interface IDeviceInventoryService
{
    Task<DeviceInventoryRefreshResult> RefreshAsync(Guid licenseId, Guid deviceId, string actor, CancellationToken cancellationToken = default);
    Task<List<AccessInventoryItemDTO>> GetAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default);
}

public sealed record DeviceInventoryRefreshResult(bool Success, string Message, int Synced, int Divergent, int Missing, int Orphan);

public sealed class DeviceInventoryService : IDeviceInventoryService
{
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControl;
    private readonly IMapper _mapper;

    public DeviceInventoryService(DatabaseContext context, IAccessControlService accessControl, IMapper mapper)
    {
        _context = context;
        _accessControl = accessControl;
        _mapper = mapper;
    }

    public Task<List<AccessInventoryItemDTO>> GetAsync(Guid licenseId, Guid deviceId, CancellationToken cancellationToken = default) =>
        _context.AccessInventoryItems.AsNoTracking()
            .Include(x => x.Credential).ThenInclude(x => x!.Resident)
            .Where(x => x.LicenseId == licenseId && x.DeviceId == deviceId)
            .OrderBy(x => x.Status).ThenBy(x => x.PersonName)
            .ToListAsync(cancellationToken);

    public async Task<DeviceInventoryRefreshResult> RefreshAsync(
        Guid licenseId,
        Guid deviceId,
        string actor,
        CancellationToken cancellationToken = default)
    {
        var device = await _context.Devices.FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (device is null) return new(false, "Equipamento nao encontrado.", 0, 0, 0, 0);

        DeviceCredentialInventoryResult remote;
        try { remote = await _accessControl.ReadCredentialInventoryAsync(_mapper.Map<AccessControlDevice>(device)); }
        catch (Exception exception) { remote = DeviceCredentialInventoryResult.Unavailable(exception.Message); }
        if (!remote.Success)
        {
            device.LastHealthCheckAt = DateTime.UtcNow;
            device.HealthMessage = Short(remote.Message, 300);
            await _context.SaveChangesAsync(cancellationToken);
            return new(false, remote.Message, 0, 0, 0, 0);
        }

        var bindings = await _context.ResidentAccessDevices
            .Include(x => x.Credential).ThenInclude(x => x.Resident)
            .Where(x => x.DeviceId == deviceId)
            .ToListAsync(cancellationToken);
        var oldItems = await _context.AccessInventoryItems.Where(x => x.DeviceId == deviceId).ToListAsync(cancellationToken);
        _context.AccessInventoryItems.RemoveRange(oldItems);
        var now = DateTime.UtcNow;
        var matchedCredentialIds = new HashSet<Guid>();
        var items = new List<AccessInventoryItemDTO>();

        foreach (var item in remote.Items)
        {
            var binding = Match(bindings, item);
            var credential = binding?.Credential;
            if (credential is not null) matchedCredentialIds.Add(credential.Id);
            var status = credential is null
                ? InventoryMatchStatusEnum.OrphanRemote
                : credential.IsActive == item.Active
                    ? InventoryMatchStatusEnum.Synced
                    : InventoryMatchStatusEnum.Divergent;
            items.Add(ToEntity(licenseId, deviceId, item, credential, status, now));
        }

        foreach (var binding in bindings.Where(x => !matchedCredentialIds.Contains(x.ResidentAccessCredentialId)))
        {
            var credential = binding.Credential;
            items.Add(new AccessInventoryItemDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, DeviceId = deviceId, CredentialId = credential.Id,
                RemoteKey = $"central:{credential.Id:N}", ExternalUserId = binding.ExternalUserId,
                ExternalCredentialId = binding.ExternalCredentialId, CredentialType = credential.CredentialType.ToString(),
                Identifier = Mask(credential.Identifier, credential.CredentialType.ToString()), PersonName = credential.Resident.Name,
                RemoteActive = false, Status = InventoryMatchStatusEnum.MissingRemote,
                DetailsJson = JsonSerializer.Serialize(new { ExpectedActive = credential.IsActive, binding.SyncStatus }), ObservedAt = now
            });
        }

        _context.AccessInventoryItems.AddRange(items);
        device.LastHealthCheckAt = now;
        device.LastSeenAt = now;
        device.HealthMessage = $"Inventario: {items.Count} item(ns), {items.Count(x => x.Status != InventoryMatchStatusEnum.Synced)} divergencia(s).";
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Device", EntityId = deviceId,
            Action = "InventoryRefresh", Status = "Success", Summary = device.HealthMessage,
            DetailsJson = JsonSerializer.Serialize(new { remote.Message, Total = items.Count }), UserName = actor, CreatedAt = now
        });
        await _context.SaveChangesAsync(cancellationToken);
        return Result(items, remote.Message);
    }

    private static ResidentAccessDeviceDTO? Match(IEnumerable<ResidentAccessDeviceDTO> bindings, DeviceCredentialInventoryItem remote)
    {
        if (!string.IsNullOrWhiteSpace(remote.ExternalCredentialId))
        {
            var match = bindings.FirstOrDefault(x => string.Equals(x.ExternalCredentialId, remote.ExternalCredentialId, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        if (!string.IsNullOrWhiteSpace(remote.ExternalUserId))
        {
            var candidates = bindings.Where(x => string.Equals(x.ExternalUserId, remote.ExternalUserId, StringComparison.OrdinalIgnoreCase));
            var match = remote.Type.HasValue ? candidates.FirstOrDefault(x => x.Credential.CredentialType == remote.Type) : candidates.FirstOrDefault();
            if (match is not null) return match;
        }
        return string.IsNullOrWhiteSpace(remote.Identifier)
            ? null
            : bindings.FirstOrDefault(x => string.Equals(x.Credential.Identifier, remote.Identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static AccessInventoryItemDTO ToEntity(Guid licenseId, Guid deviceId, DeviceCredentialInventoryItem remote,
        ResidentAccessCredentialDTO? credential, InventoryMatchStatusEnum status, DateTime now) => new()
    {
        Id = Guid.NewGuid(), LicenseId = licenseId, DeviceId = deviceId, CredentialId = credential?.Id,
        RemoteKey = Short(remote.RemoteKey, 220), ExternalUserId = Short(remote.ExternalUserId, 150),
        ExternalCredentialId = Short(remote.ExternalCredentialId, 150), CredentialType = remote.Type?.ToString() ?? "User",
        Identifier = Mask(remote.Identifier, remote.Type?.ToString() ?? "User"), PersonName = Short(remote.PersonName, 200),
        RemoteActive = remote.Active, Status = status, DetailsJson = ValidJson(remote.RawJson), ObservedAt = now
    };

    private static DeviceInventoryRefreshResult Result(List<AccessInventoryItemDTO> items, string message) => new(true, message,
        items.Count(x => x.Status == InventoryMatchStatusEnum.Synced),
        items.Count(x => x.Status == InventoryMatchStatusEnum.Divergent),
        items.Count(x => x.Status == InventoryMatchStatusEnum.MissingRemote),
        items.Count(x => x.Status == InventoryMatchStatusEnum.OrphanRemote));
    private static string Mask(string value, string type) => string.IsNullOrWhiteSpace(value) ? string.Empty
        : type.Equals("Password", StringComparison.OrdinalIgnoreCase) ? "********"
        : value.Length <= 6 ? value : $"{value[..3]}...{value[^3..]}";
    private static string ValidJson(string value) { try { JsonDocument.Parse(value).Dispose(); return value; } catch { return "{}"; } }
    private static string Short(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
