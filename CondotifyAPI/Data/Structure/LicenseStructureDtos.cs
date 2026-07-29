namespace CondotifyAPI.Data.Structure
{
    public class CreateBlockIn
    {
        public string? Name { get; set; }
    }

    public class UpdateBlockIn
    {
        public string? Name { get; set; }
    }

    public class CreateUnitIn
    {
        public Guid BlockId { get; set; }
        public string? Number { get; set; }
        public string? Floor { get; set; }
    }

    public class UpdateUnitIn : CreateUnitIn
    {
    }

    public class UpdateAccessDeviceIn
    {
        public string? Name { get; set; }
        public string? IPAddress { get; set; }
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool IsActive { get; set; }
    }

    public class CreateResidentIn
    {
        public Guid UnitId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CommercialPhone { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string ApartmentNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool NotifyAccess { get; set; }
        public CondotifyAPI.Domain.Enums.Resident.ResidentUnitRelationshipEnum Relationship { get; set; } = CondotifyAPI.Domain.Enums.Resident.ResidentUnitRelationshipEnum.Resident;
        public ResidentAccessTypeEnum AccessType { get; set; } = ResidentAccessTypeEnum.Responsible;
        public bool Temporary { get; set; }
        public DateTime? Expire { get; set; }
    }

    public class LicenseStructureOut
    {
        public Guid LicenseId { get; set; }
        public string GroupLabelSingular { get; set; } = "Bloco";
        public string GroupLabelPlural { get; set; } = "Blocos";
        public string UnitLabelSingular { get; set; } = "Unidade";
        public string UnitLabelPlural { get; set; } = "Unidades";
        public List<BlockOut> Blocks { get; set; } = new();
    }

    public class UpdateStructureSettingsIn
    {
        public string? GroupLabelSingular { get; set; }
        public string? GroupLabelPlural { get; set; }
        public string? UnitLabelSingular { get; set; }
        public string? UnitLabelPlural { get; set; }
    }

    public class BlockOut
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalUnits { get; set; }
        public int TotalResidents { get; set; }
        public List<UnitOut> Units { get; set; } = new();
    }

    public class UnitOut
    {
        public Guid Id { get; set; }
        public Guid BlockId { get; set; }
        public string BlockName { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public int TotalResidents { get; set; }
        public List<ResidentOut> Residents { get; set; } = new();
    }

    public class ResidentOut
    {
        public Guid Id { get; set; }
        public Guid UnitId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string ApartmentNumber { get; set; } = string.Empty;
        public string AccessType { get; set; } = string.Empty;
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
    }

    public class AccessDeviceOut
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public string FirmwareVersion { get; set; } = string.Empty;
        public DateTime? LastHealthCheckAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public int? LastResponseTimeMs { get; set; }
        public string HealthMessage { get; set; } = string.Empty;
        public string DiscoveredPortalsJson { get; set; } = "[]";
    }

    public class CftvDeviceOut
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = string.Empty;
        public string RTSPPort { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public int MaxChannels { get; set; }
    }
}
