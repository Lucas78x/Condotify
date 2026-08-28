namespace Condotify.Models;

public sealed class OperationalDashboardViewModel
{
    public int LicenseCount { get; set; }
    public int ResidentCount { get; set; }
    public int DeviceCount { get; set; }
    public int OnlineDeviceCount { get; set; }
    public int CredentialCount { get; set; }
    public int PendingCredentialCount { get; set; }
    public int AccessEventCount { get; set; }
    public int AuthorizedAccessCount { get; set; }
    public int Last24HourAccessCount { get; set; }
    public int Last24HourAuthorizedCount { get; set; }
    public int Last24HourDeniedCount { get; set; }
    public int PendingBookingCount { get; set; }
    public int TodayBookingCount { get; set; }
    public int PendingBatchCount { get; set; }
    public int FailedBatchCount { get; set; }
    public decimal SynchronizationRate { get; set; }
    public decimal AuthorizationRate { get; set; }
    public List<OperationalTrendPointViewModel> AccessTrend { get; set; } = [];
    public List<OperationalActivityViewModel> RecentActivities { get; set; } = [];
    public List<OperationalAlertViewModel> Alerts { get; set; } = [];
}

public sealed class OperationalTrendPointViewModel
{
    public DateTime Date { get; set; }
    public int Authorized { get; set; }
    public int Denied { get; set; }
}

public sealed class OperationalActivityViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class OperationalAlertViewModel
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsConditionActive { get; set; }
    public int OccurrenceCount { get; set; }
    public DateTime FirstOccurredAt { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string AcknowledgedBy { get; set; } = string.Empty;
    public string AcknowledgementNote { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public string ResolvedBy { get; set; } = string.Empty;
    public string ResolutionNote { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public bool CanManage { get; set; }
    public DateTime? SuppressedUntil { get; set; }
    public string SuppressedBy { get; set; } = string.Empty;
    public string SuppressionReason { get; set; } = string.Empty;
}

public sealed class OperationalAlertPageViewModel
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Acknowledged { get; set; }
    public int Critical { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public List<OperationalAlertViewModel> Items { get; set; } = [];
}

public sealed class OperationalAlertSummaryViewModel
{
    public int Active { get; set; }
    public int Critical { get; set; }
    public int Warning { get; set; }
    public int Suppressed { get; set; }
}

public sealed class AlertNotificationPolicyViewModel
{
    public bool Enabled { get; set; }
    public string MinimumSeverity { get; set; } = "Warning";
    public int WarningSlaMinutes { get; set; } = 60;
    public int CriticalSlaMinutes { get; set; } = 15;
    public int EscalationRepeatMinutes { get; set; } = 60;
    public bool WebhookEnabled { get; set; }
    public bool WebhookConfigured { get; set; }
    public string WebhookEndpointLabel { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool ClearWebhook { get; set; }
    public bool EmailEnabled { get; set; }
    public string EmailRecipients { get; set; } = string.Empty;
    public bool EmailTransportReady { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AlertNotificationDeliveryViewModel
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public string AlertTitle { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int EscalationLevel { get; set; }
    public string DestinationLabel { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int? ResponseCode { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public sealed class AlertNotificationTestViewModel
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class SmtpSettingsViewModel
{
    public bool Configured { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool PasswordConfigured { get; set; }
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "F&F Access";
    public bool EnableSsl { get; set; } = true;
}
