namespace Condotify.Models;

public sealed class LicenseReportsViewModel
{
    public DateTime GeneratedAt { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public int PeriodDays { get; set; }
    public int QualityScore { get; set; }
    public ResidentReportSummaryViewModel Residents { get; set; } = new();
    public StructureReportSummaryViewModel Structure { get; set; } = new();
    public OperationReportSummaryViewModel Operation { get; set; } = new();
    public List<ReportTrendPointViewModel> Trend { get; set; } = [];
    public List<ReportQualityIndicatorViewModel> QualityIndicators { get; set; } = [];
    public List<ReportBlockAdoptionViewModel> AdoptionByBlock { get; set; } = [];
    public List<ReportDistributionItemViewModel> ResidentDistribution { get; set; } = [];
    public List<ReportDistributionItemViewModel> CredentialDistribution { get; set; } = [];
    public List<ReportHourViewModel> AccessByHour { get; set; } = [];
    public List<ReportAttentionItemViewModel> AttentionItems { get; set; } = [];
}

public sealed class ResidentReportSummaryViewModel
{
    public int Registered { get; set; }
    public int Active { get; set; }
    public int AccountsCreated { get; set; }
    public int AppLinked { get; set; }
    public int RecentlyActive { get; set; }
    public int WithCompleteContact { get; set; }
    public int WithDocument { get; set; }
    public int WithProfilePhoto { get; set; }
    public int WithActiveCredential { get; set; }
    public int WithFacialCredential { get; set; }
    public decimal AccountActivationRate { get; set; }
    public decimal AppAdoptionRate { get; set; }
    public decimal RecentUsageRate { get; set; }
}

public sealed class StructureReportSummaryViewModel
{
    public int Blocks { get; set; }
    public int Units { get; set; }
    public int OccupiedUnits { get; set; }
    public int VacantUnits { get; set; }
    public int Vehicles { get; set; }
    public decimal OccupancyRate { get; set; }
}

public sealed class OperationReportSummaryViewModel
{
    public int AccessEvents { get; set; }
    public int AuthorizedAccesses { get; set; }
    public int DeniedAccesses { get; set; }
    public decimal AuthorizationRate { get; set; }
    public int VisitorsCreated { get; set; }
    public int VisitorsCheckedIn { get; set; }
    public int VisitorsPending { get; set; }
    public int VisitorsExpired { get; set; }
    public int PeakHour { get; set; }
}

public sealed class ReportTrendPointViewModel
{
    public DateTime Date { get; set; }
    public DateTime EndDate { get; set; }
    public int Authorized { get; set; }
    public int Denied { get; set; }
    public int Visitors { get; set; }
}

public sealed class ReportQualityIndicatorViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Value { get; set; }
    public int Total { get; set; }
    public decimal Percentage { get; set; }
    public string Tone { get; set; } = "neutral";
    public string TargetUrl { get; set; } = string.Empty;
}

public sealed class ReportBlockAdoptionViewModel
{
    public Guid BlockId { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public int Units { get; set; }
    public int OccupiedUnits { get; set; }
    public int Residents { get; set; }
    public int AccountsCreated { get; set; }
    public int AppLinked { get; set; }
    public decimal AdoptionRate { get; set; }
}

public sealed class ReportDistributionItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Percentage { get; set; }
}

public sealed class ReportHourViewModel
{
    public int Hour { get; set; }
    public int Authorized { get; set; }
    public int Denied { get; set; }
    public int Total => Authorized + Denied;
}

public sealed class ReportAttentionItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Severity { get; set; } = "info";
    public string TargetUrl { get; set; } = string.Empty;
}
