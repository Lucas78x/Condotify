using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.DTO.AccessControl;

public sealed class OfflineAccessDeviceDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserAccessDTO User { get; set; } = null!;
    public string InstallationId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public OfflineDeviceStatusEnum Status { get; set; }
    public string DeviceSecret { get; set; } = string.Empty;
    public int OfflineWindowMinutes { get; set; } = 480;
    public bool IsPrimaryValidator { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public Guid? LastBundleId { get; set; }
    public DateTime? LastBundleGeneratedAt { get; set; }
    public DateTime? LastBundleExpiresAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string RevokedBy { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<OfflineAccessOperationDTO> Operations { get; set; } = new List<OfflineAccessOperationDTO>();
}

public sealed class OfflineAccessOperationDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid DeviceId { get; set; }
    public OfflineAccessDeviceDTO Device { get; set; } = null!;
    public Guid? VisitId { get; set; }
    public AccessVisitDTO? Visit { get; set; }
    public Guid ClientOperationId { get; set; }
    public Guid BundleId { get; set; }
    public OfflineOperationKindEnum Kind { get; set; }
    public OfflineOperationStatusEnum Status { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string BeforeStatus { get; set; } = string.Empty;
    public string AfterStatus { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}
