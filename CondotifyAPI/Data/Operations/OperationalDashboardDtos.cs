namespace CondotifyAPI.Data.Operations;

public sealed class OperationalDashboardOut
{
    public int LicenseCount { get; set; }
    public int ResidentCount { get; set; }
    public int DeviceCount { get; set; }
    public int OnlineDeviceCount { get; set; }
    public int CredentialCount { get; set; }
    public int PendingCredentialCount { get; set; }
    public int AccessEventCount { get; set; }
    public int AuthorizedAccessCount { get; set; }
    public int PendingBookingCount { get; set; }
    public int TodayBookingCount { get; set; }
    public int PendingBatchCount { get; set; }
    public int FailedBatchCount { get; set; }
    public decimal SynchronizationRate { get; set; }
    public decimal AuthorizationRate { get; set; }
    public List<OperationalTrendPointOut> AccessTrend { get; set; } = [];
    public List<OperationalActivityOut> RecentActivities { get; set; } = [];
    public List<OperationalAlertOut> Alerts { get; set; } = [];
}

public sealed class OperationalTrendPointOut
{
    public DateTime Date { get; set; }
    public int Authorized { get; set; }
    public int Denied { get; set; }
}

public sealed class OperationalActivityOut
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

public sealed class OperationalAlertOut
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
