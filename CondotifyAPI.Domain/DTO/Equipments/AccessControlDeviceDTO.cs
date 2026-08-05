using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Location;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.Equipments;

namespace CondotifyAPI.Domain.DTO.Equipments
{
    public class AccessControlDeviceDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? MACAddress { get; set; }
        public string Model { get; set; } = string.Empty;
        public string? SerialNumber { get; set; }
        public string? FirmwareVersion { get; set; }
        public DeviceTypeEnum Type { get; set; }
        public bool IsActive { get; set; }
        public LocationDTO Location { get; set; } = new();
        public List<DeviceAuditDTO> Audit { get; set; } = [];
        public List<AccessRouteDeviceDTO> AccessRoutes { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime? LastHealthCheckAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public int? LastResponseTimeMs { get; set; }
        public string HealthMessage { get; set; } = string.Empty;
        public string CapacityJson { get; set; } = "{}";
        public string DiscoveredPortalsJson { get; set; } = "[]";
        public Guid? LprCameraId { get; set; }
        public int? LprCameraChannel { get; set; }
        public LprModeEnum? LprMode { get; set; }


        public Guid LicenseId { get; set; }
        public LicenseDTO License { get; set; } = null!;
    }
}
