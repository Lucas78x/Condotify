using CondotifyAPI.Domain.Models.Audit;

namespace CondotifyAPI.Domain.Models.Equipments
{
    public class AccessControlDevice
    {
        public AccessControlDevice() { }

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
        public List<DeviceAudit> Audit { get; set; } = [];
        public Location Location { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public DateTime? LastHealthCheckAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public int? LastResponseTimeMs { get; set; }
        public string HealthMessage { get; set; } = string.Empty;
        public string CapacityJson { get; set; } = "{}";
        public string DiscoveredPortalsJson { get; set; } = "[]";

        private AccessControlDevice(string name, string ipAddress, int port, string username, string password, string? macAddress, string model, string? serialNumber, string? firmwareVersion, DeviceTypeEnum type, bool isActive, Location location, DateTime createdAt, DateTime lastUpdatedAt)
        {
            Id = Guid.NewGuid();
            Name = name;
            IPAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            MACAddress = macAddress;
            Model = model;
            SerialNumber = serialNumber;
            FirmwareVersion = firmwareVersion;
            Type  = type;
            IsActive = isActive;
            Location = location;
            CreatedAt = createdAt;
            LastUpdatedAt = lastUpdatedAt;
        }

        public static AccessControlDevice Create(string name, string ipAddress, int port, string username, string password, string? macAddress, string model, string? serialNumber, string? firmwareVersion,DeviceTypeEnum type, bool isActive, Location location, DateTime createdAt, DateTime lastUpdatedAt)
        {
            return new AccessControlDevice(name, ipAddress, port, username, password, macAddress, model, serialNumber, firmwareVersion,type, isActive, location, createdAt, lastUpdatedAt);
        }

        public bool Update(string name, string ipAddress, int port, string username, string password, string? macAddress, string model, string? serialNumber, string? firmwareVersion, DeviceTypeEnum type,bool isActive, Location location, DateTime lastUpdatedAt)
        {
            Name = name;
            IPAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            MACAddress = macAddress;
            Model = model;
            SerialNumber = serialNumber;
            FirmwareVersion = firmwareVersion;
            Type = type;
            IsActive = isActive;
            Location = location;
            LastUpdatedAt = lastUpdatedAt;

            return true;
        }

        public void CreateAudit()
        {
            //var newAudit = DeviceAudit.Create(ActionTypeMessages.Get(ActionTypeEnum.Create),)
        }
    }
}
