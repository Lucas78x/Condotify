using System.ComponentModel.DataAnnotations;

namespace Condotify.Models
{
    public class LicenseModuleViewModel
    {
        public Guid LicenseId { get; set; }
        public string LicenseName { get; set; } = string.Empty;
        public string ActiveTab { get; set; } = string.Empty;
        public string? SuccessMessage { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AccessDeviceFormViewModel : LicenseModuleViewModel
    {
        public List<AccessDeviceRowViewModel> Devices { get; set; } = new();

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string IPAddress { get; set; } = string.Empty;

        [Range(1, 65535)]
        public int Port { get; set; } = 80;

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string MACAddress { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public int Type { get; set; }
        public bool IsActive { get; set; } = true;
        public float LocationX { get; set; }
        public float LocationY { get; set; }
    }

    public class CftvDeviceFormViewModel : LicenseModuleViewModel
    {
        public List<CftvDeviceRowViewModel> Devices { get; set; } = new();

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string IpAddress { get; set; } = string.Empty;

        [Required]
        public string UserName { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;

        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";
        public int IpType { get; set; }
        public int Proportion { get; set; }
        public int Mark { get; set; }
        public int DeviceType { get; set; } = 1;
        public int MaxChannels { get; set; } = 1;
    }

    public class BlocksPageViewModel : LicenseModuleViewModel
    {
        public List<BlockRowViewModel> Blocks { get; set; } = new();
        public BlockFormViewModel Form { get; set; } = new();
    }

    public class BlockFormViewModel
    {
        [Required]
        public string Name { get; set; } = string.Empty;
    }

    public class BlockRowViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int TotalUnits { get; set; }
        public int TotalResidents { get; set; }
        public List<UnitRowViewModel> Units { get; set; } = new();
    }

    public class UnitsPageViewModel : LicenseModuleViewModel
    {
        public List<BlockRowViewModel> Blocks { get; set; } = new();
        public UnitFormViewModel Form { get; set; } = new();
    }

    public class UnitFormViewModel
    {
        public Guid BlockId { get; set; }

        [Required]
        public string Number { get; set; } = string.Empty;

        public string Floor { get; set; } = string.Empty;
    }

    public class UnitRowViewModel
    {
        public Guid Id { get; set; }
        public Guid BlockId { get; set; }
        public string BlockName { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public int TotalResidents { get; set; }
        public List<ResidentRowViewModel> Residents { get; set; } = new();
    }

    public class ResidentsPageViewModel : LicenseModuleViewModel
    {
        public List<BlockRowViewModel> Blocks { get; set; } = new();
        public ResidentFormViewModel Form { get; set; } = new();
    }

    public class ResidentFormViewModel
    {
        public Guid UnitId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string ApartmentNumber { get; set; } = string.Empty;
        public int AccessType { get; set; }
        public bool Temporary { get; set; }
        public DateTime? Expire { get; set; }
    }

    public class ResidentRowViewModel
    {
        public Guid Id { get; set; }
        public Guid UnitId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string AccessType { get; set; } = string.Empty;
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
    }

    public class AccessDeviceRowViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Model { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string MACAddress { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class CftvDeviceRowViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = string.Empty;
        public string RTSPPort { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public int MaxChannels { get; set; }
    }

    public class LicenseStructureViewModel
    {
        public Guid LicenseId { get; set; }
        public List<BlockRowViewModel> Blocks { get; set; } = new();
    }
}
