using CondotifyAPI.Domain.Enums.AccessControl;

namespace CondotifyAPI.Data.AccessControl;

public sealed class SaveAccessRouteIn
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AccessRouteAudienceEnum Audience { get; set; }
    public bool IsActive { get; set; } = true;
    public bool AllowTemporary { get; set; }
    public int DaysOfWeekMask { get; set; } = 127;
    public TimeSpan StartTime { get; set; } = TimeSpan.Zero;
    public TimeSpan EndTime { get; set; } = new(23, 59, 59);
    public List<SaveAccessRouteDeviceIn> Devices { get; set; } = [];
}

public sealed class SaveAccessRouteDeviceIn
{
    public Guid DeviceId { get; set; }
    public int PortalNumber { get; set; } = 1;
    public AccessRouteDirectionEnum Direction { get; set; } = AccessRouteDirectionEnum.Entry;
    public bool IsActive { get; set; } = true;
}

public sealed class AccessRouteOut
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public long Audience { get; set; }
    public bool IsActive { get; set; }
    public bool AllowTemporary { get; set; }
    public int DaysOfWeekMask { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public List<AccessRouteDeviceOut> Devices { get; set; } = [];
}

public sealed class AccessRouteDeviceOut
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public int PortalNumber { get; set; }
    public string Direction { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class ResidentRouteResolutionOut
{
    public long Audience { get; set; }
    public string AudienceName { get; set; } = string.Empty;
    public List<string> Routes { get; set; } = [];
    public List<ResolvedRouteDeviceOut> Devices { get; set; } = [];
}

public sealed class ResolvedRouteDeviceOut
{
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public List<string> RouteNames { get; set; } = [];
    public List<int> Portals { get; set; } = [];
}

public sealed class ActivateFacialByRoutesIn
{
    public string? ImageBase64 { get; set; }
}

public sealed class SaveResidentRouteOverrideIn
{
    public Guid RouteId { get; set; }
    public AccessRouteOverrideModeEnum Mode { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ResidentRouteOverrideOut
{
    public Guid Id { get; set; }
    public Guid RouteId { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class DevicePortalCapabilityOut
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public bool Discovered { get; set; }
}

public sealed class DeviceInspectionOut
{
    public Guid DeviceId { get; set; }
    public bool Online { get; set; }
    public int? ResponseTimeMs { get; set; }
    public string Message { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
    public List<DevicePortalCapabilityOut> Portals { get; set; } = [];
}

public sealed class DeviceInventoryItemOut
{
    public Guid Id { get; set; }
    public Guid? CredentialId { get; set; }
    public string RemoteKey { get; set; } = string.Empty;
    public string ExternalUserId { get; set; } = string.Empty;
    public string CredentialType { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public bool RemoteActive { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ObservedAt { get; set; }
}

public sealed class DeviceInventorySummaryOut
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Synced { get; set; }
    public int Divergent { get; set; }
    public int Missing { get; set; }
    public int Orphan { get; set; }
}

public sealed class RepairInventoryIn
{
    public List<Guid> InventoryItemIds { get; set; } = [];
    public string IdempotencyKey { get; set; } = string.Empty;
}
