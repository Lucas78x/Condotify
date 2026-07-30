namespace CondotifyAPI.Data.Operations;

public sealed class AlertNotificationPolicyOut
{
    public bool Enabled { get; set; }
    public string MinimumSeverity { get; set; } = "Warning";
    public int WarningSlaMinutes { get; set; }
    public int CriticalSlaMinutes { get; set; }
    public int EscalationRepeatMinutes { get; set; }
    public bool WebhookEnabled { get; set; }
    public bool WebhookConfigured { get; set; }
    public string WebhookEndpointLabel { get; set; } = string.Empty;
    public bool EmailEnabled { get; set; }
    public string EmailRecipients { get; set; } = string.Empty;
    public bool EmailTransportReady { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpdateAlertNotificationPolicyIn
{
    public bool Enabled { get; set; }
    public string MinimumSeverity { get; set; } = "Warning";
    public int WarningSlaMinutes { get; set; } = 60;
    public int CriticalSlaMinutes { get; set; } = 15;
    public int EscalationRepeatMinutes { get; set; } = 60;
    public bool WebhookEnabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public bool ClearWebhook { get; set; }
    public bool EmailEnabled { get; set; }
    public string EmailRecipients { get; set; } = string.Empty;
}

public sealed class AlertNotificationDeliveryOut
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

public sealed class TestAlertNotificationChannelIn
{
    public string Channel { get; set; } = string.Empty;
}

public sealed class TestAlertNotificationChannelOut
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class SmtpSettingsOut
{
    public bool Configured { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public bool PasswordConfigured { get; set; }
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Condotify";
    public bool EnableSsl { get; set; } = true;
}

public sealed class UpdateSmtpSettingsIn
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Condotify";
    public bool EnableSsl { get; set; } = true;
}
