using CondotifyAPI.Domain.Enums.Mobile;

namespace CondotifyAPI.Domain.DTO.Mobile;

public sealed class PushInstallationDTO
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string PushToken { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public MobilePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Locale { get; set; } = string.Empty;
    public string TimeZone { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<PushDeliveryDTO> Deliveries { get; set; } = [];
}
