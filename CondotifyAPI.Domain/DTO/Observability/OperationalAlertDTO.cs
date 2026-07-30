using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Observability;

public enum OperationalAlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum OperationalAlertStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2
}

public sealed class OperationalAlertDTO
{
    public Guid Id { get; set; }
    public Guid EnterpriseId { get; set; }
    public EnterpriseDTO Enterprise { get; set; } = null!;
    public Guid? LicenseId { get; set; }
    public LicenseDTO? License { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public OperationalAlertSeverity Severity { get; set; }
    public OperationalAlertStatus Status { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public Guid? ResourceId { get; set; }
    public bool IsConditionActive { get; set; } = true;
    public int OccurrenceCount { get; set; } = 1;
    public DateTime FirstOccurredAt { get; set; }
    public DateTime LastOccurredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public Guid? AcknowledgedById { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string AcknowledgementNote { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedById { get; set; }
    public string ResolvedBy { get; set; } = string.Empty;
    public string ResolutionNote { get; set; } = string.Empty;
    public int EscalationLevel { get; set; }
    public DateTime? LastNotifiedAt { get; set; }
    public DateTime? NextEscalationAt { get; set; }
    public DateTime? SuppressedUntil { get; set; }
    public Guid? SuppressedById { get; set; }
    public string SuppressedBy { get; set; } = string.Empty;
    public string SuppressionReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum NotificationChannel
{
    Webhook = 0,
    Email = 1
}

public enum NotificationDeliveryStatus
{
    Queued = 0,
    Sending = 1,
    Delivered = 2,
    Failed = 3,
    DeadLetter = 4,
    Cancelled = 5
}

public sealed class AlertNotificationPolicyDTO
{
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public bool Enabled { get; set; }
    public OperationalAlertSeverity MinimumSeverity { get; set; } = OperationalAlertSeverity.Warning;
    public int WarningSlaMinutes { get; set; } = 60;
    public int CriticalSlaMinutes { get; set; } = 15;
    public int EscalationRepeatMinutes { get; set; } = 60;
    public bool WebhookEnabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
    public string EmailRecipients { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUsername { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public string SmtpFromEmail { get; set; } = string.Empty;
    public string SmtpFromName { get; set; } = "Condotify";
    public bool SmtpEnableSsl { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}

public sealed class AlertNotificationDeliveryDTO
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public OperationalAlertDTO Alert { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public NotificationChannel Channel { get; set; }
    public NotificationDeliveryStatus Status { get; set; }
    public int EscalationLevel { get; set; }
    public string DeliveryKey { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime? LeaseExpiresAt { get; set; }
    public int? ResponseCode { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
