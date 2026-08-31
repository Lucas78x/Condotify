using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Condotify.Models
{
    public class CreateLicenseViewModel
    {
        [Required(ErrorMessage = "Informe o nome do condomínio.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o CNPJ.")]
        public string CNPJ { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe a cidade.")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o país.")]
        public string Country { get; set; } = "Brasil";

        // O cliente gera um codigo estavel quando o operador deixa este campo
        // opcional em branco (ver CondotifyApiClient.CreateLicenseAsync).
        public string Code { get; set; } = string.Empty;

        public int Organization { get; set; } = 1;
        public int Building { get; set; } = 2;
        public int Type { get; set; } = 1;

        [DataType(DataType.Date)]
        public DateTime ExpireDate { get; set; } = CondotifyTime.Today.AddYears(1);

        public float LocationX { get; set; }
        public float LocationY { get; set; }
        public string? ErrorMessage { get; set; }
    }

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

        [Required(ErrorMessage = "Informe o nome do equipamento.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o endereço IP do equipamento.")]
        public string IPAddress { get; set; } = string.Empty;

        [Range(1, 65535, ErrorMessage = "Informe uma porta entre 1 e 65535.")]
        public int Port { get; set; } = 80;

        [Required(ErrorMessage = "Informe o usuário de acesso ao equipamento.")]
        public string Username { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string DeviceModel { get; set; } = string.Empty;

        public string DeviceBrand { get; set; } = "Intelbras";
        public int Type { get; set; }
        public bool IsActive { get; set; }
        public float LocationX { get; set; }
        public float LocationY { get; set; }
    }

    public class AccessDeviceCreationViewModel
    {
        public bool IsActive { get; set; }
        public bool ConnectionTested { get; set; }
        public bool ConnectionSucceeded { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CftvDeviceFormViewModel : LicenseModuleViewModel
    {
        public List<CftvDeviceRowViewModel> Devices { get; set; } = new();

        [Required(ErrorMessage = "Informe o nome da câmera ou gravador.")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o endereço IP do dispositivo.")]
        public string IpAddress { get; set; } = string.Empty;

        [Required(ErrorMessage = "Informe o usuário de acesso ao dispositivo.")]
        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string HTTPPort { get; set; } = "80";
        public string RTSPPort { get; set; } = "554";
        public int IpType { get; set; }
        public int Proportion { get; set; }
        public int Mark { get; set; }
        public int DeviceType { get; set; } = 1;
        public int MaxChannels { get; set; } = 1;
        public bool ResidentVisible { get; set; }
        public List<CftvChannelViewModel> Channels { get; set; } = [];
    }

    public class CftvChannelViewModel
    {
        public int ChannelNumber { get; set; }
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public bool ResidentVisible { get; set; }
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
        public string CommercialPhone { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ApartmentNumber { get; set; } = string.Empty;
        public int AccessType { get; set; }
        public int Relationship { get; set; } = 3;
        public bool NotifyAccess { get; set; }
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

    public class DeliveriesPageViewModel : LicenseModuleViewModel
    {
        public DeliveryFormViewModel Form { get; set; } = new();
        public List<DeliveryRowViewModel> Deliveries { get; set; } = new();
    }

    public class DeliveryFormViewModel
    {
        public int Type { get; set; } = 999;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string PhotoBase64 { get; set; } = string.Empty;
        public string ReceivedBy { get; set; } = string.Empty;
        public Guid? RecipientResidentId { get; set; }
        public Guid? UnitId { get; set; }
    }

    public class DeliveryStatusFormViewModel
    {
        public Guid DeliveryId { get; set; }
        public int Status { get; set; }
        public string PersonName { get; set; } = string.Empty;
        public string ProofUrl { get; set; } = string.Empty;
    }

    public class DeliveryProofFormViewModel
    {
        public Guid? PersonId { get; set; }
        [Required] public string PersonName { get; set; } = string.Empty;
        public string ProofBase64 { get; set; } = string.Empty;
    }

    public class DeliveryRowViewModel
    {
        public Guid Id { get; set; }
        public Guid LicenseId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int StatusValue { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string DeliveryProofUrl { get; set; } = string.Empty;
        public string ReceivedBy { get; set; } = string.Empty;
        public DateTime? ReceivedAt { get; set; }
        public string DeliveredTo { get; set; } = string.Empty;
        public DateTime? DeliveredAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public Guid? RecipientResidentId { get; set; }
        public Guid? UnitId { get; set; }
    }

    public class AccessDeviceRowViewModel
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

    public class DeviceActionRowViewModel
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    public class CredentialFormViewModel
    {
        [Required]
        public Guid ResidentId { get; set; }

        [Required]
        public Guid DeviceId { get; set; }

        public int Type { get; set; } = 1;
        public string Identifier { get; set; } = string.Empty;
        public string? ImageBase64 { get; set; }
        public DateTime? ValidFrom { get; set; } = CondotifyTime.Today;
        public DateTime? ValidTo { get; set; } = CondotifyTime.Today.AddYears(1);
        public bool IsTemporary { get; set; }
        public int? MaxRenewals { get; set; }
        public int? MaxUses { get; set; }
    }

    public class CredentialRowViewModel
    {
        public Guid Id { get; set; }
        public Guid ResidentId { get; set; }
        public string ResidentName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsTemporary { get; set; }
        public int RenewalCount { get; set; }
        public int MaxRenewals { get; set; }
        public int UseCount { get; set; }
        public int? MaxUses { get; set; }
        public bool CanRenew { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public List<CredentialDeviceRowViewModel> Devices { get; set; } = new();
    }

    public class CredentialDeviceRowViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string ExternalUserId { get; set; } = string.Empty;
        public string ExternalCredentialId { get; set; } = string.Empty;
        public bool IsSynced { get; set; }
        public DateTime LastSyncAt { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? LastSuccessAt { get; set; }
        public string RouteNames { get; set; } = string.Empty;
        public string PortalNumbers { get; set; } = string.Empty;
    }

    public class CredentialOperationViewModel
    {
        public bool Success { get; set; }
        public bool Synced { get; set; }
        public string Message { get; set; } = string.Empty;
        public CredentialRowViewModel? Credential { get; set; }
    }

    [Flags]
    public enum AccessRouteAudience : long
    {
        None = 0,
        OwnerResponsible = 1L << 0,
        TenantResponsible = 1L << 1,
        Responsible = 1L << 2,
        Resident = 1L << 3,
        Dependent = 1L << 4,
        Visitor = 1L << 5,
        ServiceProvider = 1L << 6,
        All = OwnerResponsible | TenantResponsible | Responsible | Resident | Dependent | Visitor | ServiceProvider
    }

    public class AccessRouteViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long Audience { get; set; }
        public bool IsActive { get; set; }
        public bool AllowTemporary { get; set; }
        public int DaysOfWeekMask { get; set; } = 127;
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; } = new(23, 59, 59);
        public List<AccessRouteDeviceViewModel> Devices { get; set; } = [];
    }

    public class AccessRouteDeviceViewModel
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public int PortalNumber { get; set; } = 1;
        public string Direction { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class AccessRouteFormViewModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public long Audience { get; set; }
        public bool IsActive { get; set; } = true;
        public bool AllowTemporary { get; set; }
        public int DaysOfWeekMask { get; set; } = 127;
        public TimeSpan? StartTime { get; set; } = TimeSpan.Zero;
        public TimeSpan? EndTime { get; set; } = new TimeSpan(23, 59, 59);
        public List<AccessRouteTargetFormViewModel> Devices { get; set; } = [];
    }

    public class AccessRouteTargetFormViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceModel { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public bool DeviceIsActive { get; set; }
        public bool Selected { get; set; }
        public int PortalNumber { get; set; } = 1;
        public int Direction { get; set; }
        public bool IsActive { get; set; } = true;
        public List<DevicePortalCapabilityViewModel> Portals { get; set; } = [];
    }

    public class ResidentRouteResolutionViewModel
    {
        public long Audience { get; set; }
        public string AudienceName { get; set; } = string.Empty;
        public List<string> Routes { get; set; } = [];
        public List<ResolvedRouteDeviceViewModel> Devices { get; set; } = [];
    }

    public class ResolvedRouteDeviceViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public List<string> RouteNames { get; set; } = [];
        public List<int> Portals { get; set; } = [];
    }

    [Flags]
    public enum LicensePermission : long
    {
        None = 0,
        ViewDashboard = 1L << 0,
        ViewStructure = 1L << 1,
        ManageStructure = 1L << 2,
        ViewPeople = 1L << 3,
        ManagePeople = 1L << 4,
        ViewCredentials = 1L << 5,
        ManageCredentials = 1L << 6,
        ViewDevices = 1L << 7,
        ManageDevices = 1L << 8,
        OperateDevices = 1L << 9,
        ViewEvents = 1L << 10,
        ViewDeliveries = 1L << 11,
        ManageDeliveries = 1L << 12,
        ViewUsers = 1L << 13,
        ManageUsers = 1L << 14,
        ViewSettings = 1L << 15,
        ManageSettings = 1L << 16,
        ViewBookings = 1L << 17,
        ManageBookings = 1L << 18,
        ViewBackups = 1L << 19,
        ManageBackups = 1L << 20,
        ViewAlerts = 1L << 21,
        ManageAlerts = 1L << 22,
        ViewIncidents = 1L << 23,
        ManageIncidents = 1L << 24,
        ViewAutomations = 1L << 25,
        ManageAutomations = 1L << 26,
        ViewEmergency = 1L << 27,
        ManageEmergency = 1L << 28,
        ViewVehicles = 1L << 29,
        ManageVehicles = 1L << 30,
        ViewFinance = 1L << 31,
        ManageFinance = 1L << 32,
        ViewDocuments = 1L << 33,
        ManageDocuments = 1L << 34,
        ManageAnnouncements = 1L << 35,
        ViewAssemblies = 1L << 36,
        ManageAssemblies = 1L << 37,
        All = (1L << 38) - 1
    }

    public class LicenseAdministrationViewModel
    {
        public CurrentLicenseAccessViewModel CurrentAccess { get; set; } = new();
        public List<LicenseUserAccessViewModel> Users { get; set; } = [];
        public CredentialPolicyViewModel Policy { get; set; } = new();
    }

    public class CurrentLicenseAccessViewModel
    {
        public string Role { get; set; } = string.Empty;
        public long Permissions { get; set; }
        public bool IsEnterpriseAdministrator { get; set; }
        public bool Has(LicensePermission permission) => (Permissions & (long)permission) == (long)permission;
    }

    public class LicenseUserAccessViewModel
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public long Permissions { get; set; }
        public bool IsActive { get; set; }
        public DateTime LastAccess { get; set; }
    }

    public class LicenseUserFormViewModel
    {
        [Required(ErrorMessage = "Informe o nome completo.")] public string Name { get; set; } = string.Empty;
        [Required(ErrorMessage = "Informe o e-mail de acesso.")]
        [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int Role { get; set; } = 3;
        public long Permissions { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class CredentialPolicyViewModel
    {
        [Range(5, 43200)] public int QrCodeValidityMinutes { get; set; } = 1440;
        public bool AllowQrCodeRenewal { get; set; } = true;
        [Range(0, 50)] public int MaxQrCodeRenewals { get; set; } = 2;
        [Range(5, 43200)] public int QrCodeRenewalMinutes { get; set; } = 1440;
        [Range(5, 43200)] public int TemporaryFaceValidityMinutes { get; set; } = 1440;
        [Range(5, 525600)] public int MaxTemporaryFaceValidityMinutes { get; set; } = 10080;
        public bool RequireFacePhoto { get; set; } = true;
        public bool AutoDeactivateExpiredCredentials { get; set; } = true;
        public bool RemoveExpiredCredentialsFromDevices { get; set; } = true;
        public bool AllowResidentDigitalPass { get; set; } = true;
    }

    public class AccessEventRowViewModel
    {
        public string Id { get; set; } = string.Empty;
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public bool Authorized { get; set; }
        public DateTime OccurredAt { get; set; }
        public string ExternalUserId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
        public string Portal { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class GlobalResidentSearchViewModel
    {
        public Guid Id { get; set; }
        public Guid LicenseId { get; set; }
        public Guid UnitId { get; set; }
        public string LicenseName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AccessType { get; set; } = string.Empty;
        public bool Temporary { get; set; }
        public DateTime Expire { get; set; }
        public List<GlobalCredentialSearchViewModel> Credentials { get; set; } = [];
        public List<GlobalVehicleSearchViewModel> Vehicles { get; set; } = [];
    }

    public class GlobalCredentialSearchViewModel
    {
        public string Type { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class GlobalVehicleSearchViewModel
    {
        public string Plate { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class VehiclePlateSearchViewModel
    {
        public Guid VehicleId { get; set; }
        public string Plate { get; set; } = string.Empty;
        public string ResidentName { get; set; } = string.Empty;
        public string UnitLabel { get; set; } = string.Empty;
    }

    public class CftvDeviceRowViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string HTTPPort { get; set; } = string.Empty;
        public string RTSPPort { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int IpType { get; set; }
        public int Proportion { get; set; }
        public int Mark { get; set; }
        public int DeviceTypeValue { get; set; }
        public string DeviceType { get; set; } = string.Empty;
        public int MaxChannels { get; set; }
        public bool ResidentVisible { get; set; }
        public List<CftvChannelViewModel> Channels { get; set; } = [];
        public List<CftvAccessActionViewModel> AccessActions { get; set; } = [];
    }

    public class CftvConnectionDiagnosticViewModel
    {
        public bool PingOk { get; set; }
        public bool TcpRtspOk { get; set; }
        public bool RtspReady { get; set; }
        public int ChannelsReady { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class CftvAccessActionViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public int Channel { get; set; }
        public bool IsOnline { get; set; }
    }

    public class LicenseStructureViewModel
    {
        public Guid LicenseId { get; set; }
        public string GroupLabelSingular { get; set; } = "Bloco";
        public string GroupLabelPlural { get; set; } = "Blocos";
        public string UnitLabelSingular { get; set; } = "Unidade";
        public string UnitLabelPlural { get; set; } = "Unidades";
        public List<BlockRowViewModel> Blocks { get; set; } = new();
    }

    public class StructureSettingsViewModel
    {
        [Required] public string GroupLabelSingular { get; set; } = "Bloco";
        [Required] public string GroupLabelPlural { get; set; } = "Blocos";
        [Required] public string UnitLabelSingular { get; set; } = "Unidade";
        [Required] public string UnitLabelPlural { get; set; } = "Unidades";
    }

    public class UnitDetailViewModel
    {
        public Guid Id { get; set; }
        public Guid BlockId { get; set; }
        public string BlockName { get; set; } = string.Empty;
        public string Number { get; set; } = string.Empty;
        public string Floor { get; set; } = string.Empty;
        public List<PersonSummaryViewModel> People { get; set; } = new();
        public List<VehicleViewModel> Vehicles { get; set; } = new();
    }

    public class PersonSummaryViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Document { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int CredentialCount { get; set; }
        public int VehicleCount { get; set; }
        public bool HasFaceCredential { get; set; }
        public bool HasActiveFaceCredential { get; set; }
    }

    public class PersonProfileViewModel : PersonSummaryViewModel
    {
        public Guid UnitId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string CommercialPhone { get; set; } = string.Empty;
        public bool NotifyAccess { get; set; }
        public List<PersonCredentialViewModel> Credentials { get; set; } = new();
        public List<VehicleViewModel> Vehicles { get; set; } = new();
        public List<RegistrationInviteViewModel> Invites { get; set; } = new();
    }

    public class PersonProfileFormViewModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        [EmailAddress] public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CommercialPhone { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public bool NotifyAccess { get; set; }
        public bool IsActive { get; set; } = true;
        public int Relationship { get; set; } = 3;
    }

    public class PersonCredentialViewModel
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsTemporary { get; set; }
        public int RenewalCount { get; set; }
        public int MaxRenewals { get; set; }
        public int DeviceCount { get; set; }
        public int SyncedDeviceCount { get; set; }
        public int UseCount { get; set; }
        public int? MaxUses { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public List<PersonCredentialDeviceViewModel> Devices { get; set; } = [];
    }

    public class PersonCredentialDeviceViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string RouteNames { get; set; } = string.Empty;
        public string PortalNumbers { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public DateTime LastSyncAt { get; set; }
        public DateTime? NextAttemptAt { get; set; }
    }

    public class DeviceInspectionViewModel
    {
        public Guid DeviceId { get; set; }
        public bool Online { get; set; }
        public int? ResponseTimeMs { get; set; }
        public string Message { get; set; } = string.Empty;
        public string FirmwareVersion { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }
        public List<DevicePortalCapabilityViewModel> Portals { get; set; } = [];
    }

    public class DevicePortalCapabilityViewModel
    {
        public int Number { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public bool Discovered { get; set; }
    }
    public sealed class DevicePortalCapabilityJson
    {
        public int Number { get; set; }

        public string? Name { get; set; }

        public JsonElement Direction { get; set; }

        public bool Discovered { get; set; }
    }

    public class ReconciliationPreviewViewModel
    {
        public int CredentialCount { get; set; }
        public int ResidentCount { get; set; }
        public int TargetCount { get; set; }
        public int PendingCount { get; set; }
        public List<string> Warnings { get; set; } = [];
    }

    public class RouteSimulationViewModel
    {
        public string Audience { get; set; } = string.Empty;
        public List<string> Routes { get; set; } = [];
        public List<RouteSimulationTargetViewModel> Targets { get; set; } = [];
        public List<string> Warnings { get; set; } = [];
    }

    public class RouteSimulationTargetViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public bool Online { get; set; }
        public List<string> Portals { get; set; } = [];
    }

    public class AccessBatchOperationViewModel
    {
        public Guid Id { get; set; }
        public Guid LicenseId { get; set; }
        public string LicenseName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessfulItems { get; set; }
        public int FailedItems { get; set; }
        public int Priority { get; set; }
        public int AttemptCount { get; set; }
        public int MaxAttempts { get; set; }
        public string RequestedBy { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public DateTime? NextAttemptAt { get; set; }
        public List<AccessOperationItemViewModel> Items { get; set; } = [];
    }

    public class AccessOperationItemViewModel
    {
        public Guid Id { get; set; }
        public Guid? CredentialId { get; set; }
        public Guid? DeviceId { get; set; }
        public string CredentialName { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int AttemptCount { get; set; }
        public string Error { get; set; } = string.Empty;
        public DateTime? NextAttemptAt { get; set; }
        public DateTime? FinishedAt { get; set; }
    }

    public class DeviceInventoryItemViewModel
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
        public bool Selected { get; set; }
    }

    public class DeviceInventorySummaryViewModel
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int Synced { get; set; }
        public int Divergent { get; set; }
        public int Missing { get; set; }
        public int Orphan { get; set; }
    }

    public class AccessAuditViewModel
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string DetailsJson { get; set; } = "{}";
        public string UserName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ResidentRouteOverrideViewModel
    {
        public Guid Id { get; set; }
        public Guid RouteId { get; set; }
        public string RouteName { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public class CredentialBackupViewModel
    {
        public int SchemaVersion { get; set; } = 1;
        public Guid LicenseId { get; set; }
        public DateTime ExportedAt { get; set; }
        public List<CredentialBackupItemViewModel> Credentials { get; set; } = [];
    }

    public class CredentialBackupItemViewModel
    {
        public Guid ResidentId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Identifier { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsTemporary { get; set; }
        public int RenewalCount { get; set; }
        public int MaxRenewals { get; set; }
        public int UseCount { get; set; }
        public int? MaxUses { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
    }

    public class VehicleViewModel
    {
        public Guid Id { get; set; }
        public Guid UnitId { get; set; }
        public Guid? ResidentId { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string Plate { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string TagIdentifier { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    public class VehicleFormViewModel
    {
        public Guid UnitId { get; set; }
        [Required] public string Plate { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public string Type { get; set; } = "Carro";
        public string TagIdentifier { get; set; } = string.Empty;
    }

    public class StructureImportPreviewViewModel
    {
        public Guid PreviewId { get; set; }
        public string FileSha256 { get; set; } = string.Empty;
        public bool CanExecute { get; set; }
        public int TotalRows { get; set; }
        public int ValidRows { get; set; }
        public int InvalidRows { get; set; }
        public int NewBlocks { get; set; }
        public int NewUnits { get; set; }
        public int NewPeople { get; set; }
        public int NewVehicles { get; set; }
        public List<string> Errors { get; set; } = [];
        public List<StructureImportRowViewModel> Rows { get; set; } = [];
    }

    public class AssistedMigrationContextViewModel
    {
        public string SourceSystem { get; set; } = string.Empty;
        public string ProcessingBasis { get; set; } = string.Empty;
        [MaxLength(150)] public string AuthorizedBy { get; set; } = string.Empty;
        [MaxLength(180)] public string AuthorizationReference { get; set; } = string.Empty;
        public bool ControllerAuthorizationConfirmed { get; set; }
        public bool PurposeLimitationConfirmed { get; set; }
        public bool NoRestrictedDataConfirmed { get; set; }
    }

    public class StructureImportRowViewModel
    {
        public int RowNumber { get; set; }
        public string Block { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string VehiclePlate { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public List<string> Messages { get; set; } = [];
    }

    public class StructureImportExecutionViewModel
    {
        public Guid ImportId { get; set; }
        public int CreatedBlocks { get; set; }
        public int CreatedUnits { get; set; }
        public int CreatedPeople { get; set; }
        public int CreatedVehicles { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class RecycleBinItemViewModel
    {
        public Guid Id { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string DeletedBy { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int DaysRemaining { get; set; }
    }

    public class RecycleBinRestoreViewModel
    {
        public Guid ItemId { get; set; }
        public Guid EntityId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class RegistrationInviteViewModel
    {
        public Guid Id { get; set; }
        public Guid ResidentId { get; set; }
        public string ResidentName { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public int SendCount { get; set; }
        public DateTime SentAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public string? InviteUrl { get; set; }
    }

    public class PublicRegistrationInviteViewModel
    {
        public string ResidentName { get; set; } = string.Empty;
        public string LicenseName { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool ExistingAccount { get; set; }
    }

    public class CompleteRegistrationInviteViewModel
    {
        [Required] public string Name { get; set; } = string.Empty;
        [EmailAddress] public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string CPF { get; set; } = string.Empty;
        public string RG { get; set; } = string.Empty;
        public string BirthDate { get; set; } = string.Empty;

        // Compartilhado com o futuro app MAUI (SP-0): este mesmo contrato serve a tela de
        // convite mobile, entao o campo fica aqui, nao apenas na pagina Blazor.
        [MinLength(8), MaxLength(100)] public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password), ErrorMessage = "As senhas nao coincidem.")] public string PasswordConfirmation { get; set; } = string.Empty;
        public bool ConfirmExistingAccount { get; set; }
    }

    public class ConciergeDashboardViewModel
    {
        public List<ConciergeVisitViewModel> Visits { get; set; } = [];
        public List<ConciergeEventViewModel> Events { get; set; } = [];
        public List<ConciergeDeviceViewModel> Devices { get; set; } = [];
        public int ExpectedToday { get; set; }
        public int InsideNow { get; set; }
        public int OfflineDevices { get; set; }
        public int DeniedToday { get; set; }
        public int PendingApprovals { get; set; }
        public int Overstays { get; set; }
        public List<AccessWatchlistEntryViewModel> Watchlist { get; set; } = [];
    }

    public class ConciergeVisitViewModel
    {
        public Guid Id { get; set; }
        public Guid LicenseId { get; set; }
        public Guid HostResidentId { get; set; }
        public string HostName { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string VisitorName { get; set; } = string.Empty;
        public string Document { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;
        public string VehiclePlate { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CredentialType { get; set; } = string.Empty;
        public string CredentialCode { get; set; } = string.Empty;
        public int UseCount { get; set; }
        public int? MaxUses { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public DateTime? CheckedInAt { get; set; }
        public DateTime? CheckedOutAt { get; set; }
        public bool ApprovalRequired { get; set; }
        public string ApprovedBy { get; set; } = string.Empty;
        public DateTime? ApprovedAt { get; set; }
        public string ApprovalNotes { get; set; } = string.Empty;
        public DateTime? ExpectedCheckoutAt { get; set; }
        public bool IsOverstayed { get; set; }
        public int RecurrenceSequence { get; set; }
        public int RecurrenceCount { get; set; }
        public string FacialInviteStatus { get; set; } = string.Empty;
        public string FacialInviteUrl { get; set; } = string.Empty;
    }

    public class ConciergeEventViewModel
    {
        public Guid Id { get; set; }
        public Guid DeviceId { get; set; }
        public Guid? CredentialId { get; set; }
        public Guid? ResidentId { get; set; }
        public Guid? UnitId { get; set; }
        public Guid? VisitId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string CredentialType { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
        public bool? CredentialActive { get; set; }
        public DateTime? CredentialValidFrom { get; set; }
        public DateTime? CredentialValidTo { get; set; }
        public string Details { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public string HostPhoneNumber { get; set; } = string.Empty;
        public string Event { get; set; } = string.Empty;
        public bool Authorized { get; set; }
        public string Portal { get; set; } = string.Empty;
        public DateTime OccurredAt { get; set; }
        public bool RequiresAttention { get; set; }
        public DateTime? AttentionResolvedAt { get; set; }
        public string AttentionResolvedBy { get; set; } = string.Empty;
        public string AttentionResolutionNote { get; set; } = string.Empty;
    }

    public class ConciergeDeviceViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool Online { get; set; }
        public string HealthMessage { get; set; } = string.Empty;
        public string DiscoveredPortalsJson { get; set; } = "[]";
    }

    public class ConciergeVisitFormViewModel
    {
        [Required(ErrorMessage = "Selecione o morador que receberá a visita.")] public Guid HostResidentId { get; set; }
        [Required(ErrorMessage = "Informe o nome do visitante."), MaxLength(150, ErrorMessage = "O nome do visitante deve ter no máximo 150 caracteres.")] public string VisitorName { get; set; } = string.Empty;
        [MaxLength(20, ErrorMessage = "O documento deve ter no máximo 20 caracteres.")] public string Document { get; set; } = string.Empty;
        [MaxLength(20, ErrorMessage = "O telefone deve ter no máximo 20 caracteres.")] public string PhoneNumber { get; set; } = string.Empty;
        [MaxLength(150, ErrorMessage = "A empresa deve ter no máximo 150 caracteres.")] public string Company { get; set; } = string.Empty;
        [MaxLength(200, ErrorMessage = "O motivo da visita deve ter no máximo 200 caracteres.")] public string Purpose { get; set; } = string.Empty;
        [MaxLength(20, ErrorMessage = "A placa deve ter no máximo 20 caracteres.")] public string VehiclePlate { get; set; } = string.Empty;
        public string ImageBase64 { get; set; } = string.Empty;
        public int CredentialType { get; set; } = 2;
        public bool CreateFacialInvite { get; set; }
        public DateTime ValidFrom { get; set; } = CondotifyTime.Now;
        public DateTime ValidTo { get; set; } = CondotifyTime.Now.AddHours(4);
        [Range(1, 100, ErrorMessage = "O limite de acessos deve estar entre 1 e 100.")] public int? MaxUses { get; set; } = 1;
        public List<Guid> RouteIds { get; set; } = [];
        public bool RequireApproval { get; set; }
        public int RepeatCount { get; set; } = 1;
        public int RepeatEveryDays { get; set; } = 7;
    }

    public class PublicVisitFacialInviteViewModel
    {
        public string VisitorName { get; set; } = string.Empty;
        public string LicenseName { get; set; } = string.Empty;
        public string BlockName { get; set; } = string.Empty;
        public string UnitNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime ValidFrom { get; set; }
        public DateTime ValidTo { get; set; }
        public DateTime ExpiresAt { get; set; }
        public int? MaxUses { get; set; }
        public List<PublicVisitRouteViewModel> Routes { get; set; } = [];
    }

    public class PublicVisitRouteViewModel
    {
        public string Name { get; set; } = string.Empty;
        public int DaysOfWeekMask { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }

    public class CompleteVisitFacialInviteViewModel
    {
        public string ImageBase64 { get; set; } = string.Empty;
        public bool Consent { get; set; }
    }

    public class VisitFacialInviteIssuedViewModel
    {
        public Guid VisitId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string InviteUrl { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class AccessWatchlistEntryViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Document { get; set; } = string.Empty;
        public string VehiclePlate { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public int Severity { get; set; } = 2;
        public DateTime? ExpiresAt { get; set; }
    }

    public class AccessWatchlistFormViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Document { get; set; } = string.Empty;
        public string VehiclePlate { get; set; } = string.Empty;
        [Required] public string Reason { get; set; } = string.Empty;
        public int Severity { get; set; } = 2;
        public DateTime? ExpiresAt { get; set; }
    }

    public class ConfigurationBackupViewModel
    {
        public Guid Id { get; set; }
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int DeviceCount { get; set; }
        public int RouteCount { get; set; }
        public int CredentialCount { get; set; }
        public int BindingCount { get; set; }
        public int OverrideCount { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRestoredAt { get; set; }
        public string LastRestoredBy { get; set; } = string.Empty;
    }

    public class ConfigurationBackupCreateViewModel
    {
        [MaxLength(120)] public string Name { get; set; } = string.Empty;
        [MaxLength(500)] public string Description { get; set; } = string.Empty;
    }

    public class ConfigurationRestoreOptionsViewModel
    {
        public string Mode { get; set; } = "Merge";
        public bool IncludeDevices { get; set; } = true;
        public bool IncludeRoutes { get; set; } = true;
        public bool IncludeCredentials { get; set; } = true;
        public bool CompareWithEquipment { get; set; } = true;
        public List<Guid> TargetDeviceIds { get; set; } = [];
        public string Confirmation { get; set; } = string.Empty;
    }

    public class ConfigurationRestorePreviewViewModel
    {
        public Guid BackupId { get; set; }
        public int Version { get; set; }
        public string Mode { get; set; } = string.Empty;
        public bool CanRestore { get; set; }
        public int CreateCount { get; set; }
        public int UpdateCount { get; set; }
        public int DeactivateCount { get; set; }
        public int ConflictCount { get; set; }
        public int WarningCount { get; set; }
        public int SelectedDeviceCount { get; set; }
        public bool IsMassRestore { get; set; }
        public DateTime ComparedAt { get; set; }
        public List<ConfigurationRestoreSectionViewModel> Sections { get; set; } = [];
        public List<string> Conflicts { get; set; } = [];
        public List<ConfigurationRestoreConflictViewModel> ConflictReport { get; set; } = [];
        public List<ConfigurationEquipmentComparisonViewModel> EquipmentComparisons { get; set; } = [];
    }

    public class ConfigurationBackupTargetViewModel
    {
        public Guid DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int CredentialCount { get; set; }
        public int RouteCount { get; set; }
    }

    public class ConfigurationRestoreConflictViewModel
    {
        public string Code { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public Guid? EntityId { get; set; }
        public string EntityName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string SuggestedAction { get; set; } = string.Empty;
        public bool Blocking { get; set; }
    }

    public class ConfigurationEquipmentComparisonViewModel
    {
        public Guid DeviceId { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int ExpectedCount { get; set; }
        public int RemoteCount { get; set; }
        public int SyncedCount { get; set; }
        public int DivergentCount { get; set; }
        public int MissingCount { get; set; }
        public int OrphanCount { get; set; }
    }

    public class ConfigurationRestoreSectionViewModel
    {
        public string Section { get; set; } = string.Empty;
        public int CreateCount { get; set; }
        public int UpdateCount { get; set; }
        public int DeactivateCount { get; set; }
        public int ConflictCount { get; set; }
    }

    public class ConfigurationRestoreExecutionViewModel
    {
        public Guid BackupId { get; set; }
        public int Version { get; set; }
        public string Mode { get; set; } = string.Empty;
        public int CreatedCount { get; set; }
        public int UpdatedCount { get; set; }
        public int DeactivatedCount { get; set; }
        public int CredentialsQueued { get; set; }
        public int TargetDeviceCount { get; set; }
        public bool IsMassRestore { get; set; }
        public Guid? ReconciliationBatchId { get; set; }
        public DateTime RestoredAt { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BackupAutomationPolicyViewModel
    {
        public bool Enabled { get; set; }
        public int IntervalHours { get; set; } = 24;
        public bool ExportEnabled { get; set; } = true;
        public int ExternalRetentionDays { get; set; } = 90;
        public DateTime? LastRunAt { get; set; }
        public DateTime? LastSuccessAt { get; set; }
        public DateTime? NextRunAt { get; set; }
        public string LastError { get; set; } = string.Empty;
        public bool ExternalStorageReady { get; set; }
        public string ExternalStorageLabel { get; set; } = string.Empty;
    }

    public class BackupRunViewModel
    {
        public ConfigurationBackupViewModel Backup { get; set; } = new();
        public bool Exported { get; set; }
        public string ExportFileName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class BackupArchiveViewModel
    {
        public string FileName { get; set; } = string.Empty;
        public string ContentBase64 { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }
}
