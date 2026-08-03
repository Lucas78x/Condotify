using CondotifyAPI.Domain.Enums.Mobile;

namespace CondotifyAPI.Domain.DTO.Mobile;

public sealed class PushNotificationDTO
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public MobileNotificationCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string DeepLink { get; set; } = string.Empty;
    public string DataJson { get; set; } = "{}";
    public string DeduplicationKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? FanoutCompletedAt { get; set; }
    public ICollection<PushDeliveryDTO> Deliveries { get; set; } = [];
}

public sealed class PushDeliveryDTO
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public PushNotificationDTO Notification { get; set; } = null!;
    public Guid InstallationId { get; set; }
    public PushInstallationDTO Installation { get; set; } = null!;
    public PushDeliveryStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime? LeaseExpiresAt { get; set; }
    public int? ResponseCode { get; set; }
    public string ProviderMessageId { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
