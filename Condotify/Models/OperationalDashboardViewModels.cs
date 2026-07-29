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
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public Guid? LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string TargetUrl { get; set; } = string.Empty;
}
