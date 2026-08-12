using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public enum FinancialChargeStatus
{
    Open = 0,
    PaymentReported = 1,
    Paid = 2,
    Negotiated = 3,
    Disputed = 4,
    Cancelled = 5
}

public enum FinancialChargeAction
{
    ConfirmPayment = 0,
    RejectPaymentReport = 1,
    MarkNegotiated = 2,
    MarkDisputed = 3,
    Cancel = 4,
    Reopen = 5
}

public sealed class FinancialUnitOptionViewModel
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class FinancialChargeViewModel
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public Guid? BoletoDocumentId { get; set; }
    public string Competence { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public FinancialChargeStatus Status { get; set; }
    public string DisplayStatus { get; set; } = string.Empty;
    public bool IsOverdue { get; set; }
    public int DaysOverdue { get; set; }
    public DateTime? PaidAt { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class FinancialChargeEventViewModel
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public FinancialChargeStatus? PreviousStatus { get; set; }
    public FinancialChargeStatus NewStatus { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class FinancialChargeDetailViewModel
{
    public FinancialChargeViewModel Charge { get; set; } = new();
    public List<FinancialChargeEventViewModel> Events { get; set; } = [];
}

public sealed class FinancialAgingBucketViewModel
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal Amount { get; set; }
}

public sealed class FinancialSummaryViewModel
{
    public decimal OpenAmount { get; set; }
    public decimal OverdueAmount { get; set; }
    public decimal PaidThisMonthAmount { get; set; }
    public int OpenCharges { get; set; }
    public int OverdueCharges { get; set; }
    public int DelinquentUnits { get; set; }
    public int PaymentReportsPending { get; set; }
    public List<FinancialAgingBucketViewModel> Aging { get; set; } = [];
}

public sealed class FinancialManagementViewModel
{
    public FinancialSummaryViewModel Summary { get; set; } = new();
    public List<FinancialChargeViewModel> Charges { get; set; } = [];
    public List<FinancialUnitOptionViewModel> Units { get; set; } = [];
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public sealed class CreateFinancialChargesViewModel
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    [MinLength(1)] public List<Guid> UnitIds { get; set; } = [];
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] public string Competence { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Reference { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99")] public decimal BaseAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal FineAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal InterestAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal DiscountAmount { get; set; }
    [StringLength(1000)] public string Notes { get; set; } = string.Empty;
}

public sealed class UpdateFinancialChargeViewModel
{
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] public string Competence { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Reference { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    [Range(typeof(decimal), "0.01", "999999999999.99")] public decimal BaseAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal FineAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal InterestAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal DiscountAmount { get; set; }
    [StringLength(1000)] public string Notes { get; set; } = string.Empty;
}

public sealed class FinancialChargeActionViewModel
{
    public FinancialChargeAction Action { get; set; }
    [StringLength(500)] public string Note { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    [StringLength(100)] public string PaymentReference { get; set; } = string.Empty;
}

public sealed class ResidentFinancialActionViewModel
{
    [Required, StringLength(500, MinimumLength = 3)] public string Note { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}

public sealed class ResidentFinancialOverviewViewModel
{
    public decimal OpenAmount { get; set; }
    public decimal OverdueAmount { get; set; }
    public int OverdueCharges { get; set; }
    public int PendingAnalysis { get; set; }
    public List<FinancialChargeViewModel> Charges { get; set; } = [];
    public List<ResidentFinancialReminderViewModel> RecentReminders { get; set; } = [];
}

public enum FinancialImportStatus
{
    Imported = 0,
    Failed = 1
}

public enum FinancialReminderChannel
{
    Push = 0,
    Email = 1
}

public enum FinancialReminderDeliveryStatus
{
    Queued = 0,
    Sending = 1,
    Delivered = 2,
    Failed = 3,
    DeadLetter = 4,
    Cancelled = 5
}

public sealed class FinancialRecurringRuleViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool AllUnits { get; set; }
    public List<Guid> UnitIds { get; set; } = [];
    public int UnitCount { get; set; }
    public int GenerationDay { get; set; }
    public int DueDay { get; set; }
    public string StartMonth { get; set; } = string.Empty;
    public string EndMonth { get; set; } = string.Empty;
    public string ReferenceTemplate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BaseAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int LastGeneratedCount { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpsertFinancialRecurringRuleViewModel
{
    [Required, StringLength(100)] public string Name { get; set; } = string.Empty;
    public bool AllUnits { get; set; } = true;
    public List<Guid> UnitIds { get; set; } = [];
    [Range(1, 28)] public int GenerationDay { get; set; } = 1;
    [Range(1, 28)] public int DueDay { get; set; } = 10;
    [Required, RegularExpression(@"^\d{4}-(0[1-9]|1[0-2])$")] public string StartMonth { get; set; } = string.Empty;
    [RegularExpression(@"^$|^\d{4}-(0[1-9]|1[0-2])$")] public string EndMonth { get; set; } = string.Empty;
    [Required, StringLength(80)] public string ReferenceTemplate { get; set; } = "Condomínio {competencia}";
    [Required, StringLength(200)] public string Description { get; set; } = "Contribuição condominial";
    [Range(typeof(decimal), "0.01", "999999999999.99")] public decimal BaseAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal FineAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal InterestAmount { get; set; }
    [Range(typeof(decimal), "0", "999999999999.99")] public decimal DiscountAmount { get; set; }
    [StringLength(1000)] public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class FinancialReminderPolicyViewModel
{
    public bool Enabled { get; set; }
    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public List<int> BeforeDueDays { get; set; } = [5, 1];
    public bool OnDueDate { get; set; } = true;
    public int FirstOverdueDay { get; set; } = 1;
    public int RepeatEveryDays { get; set; } = 7;
    public int MaxOverdueDays { get; set; } = 90;
    public bool EmailTransportReady { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class UpdateFinancialReminderPolicyViewModel
{
    public bool Enabled { get; set; }
    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public List<int> BeforeDueDays { get; set; } = [5, 1];
    public bool OnDueDate { get; set; } = true;
    [Range(1, 30)] public int FirstOverdueDay { get; set; } = 1;
    [Range(1, 60)] public int RepeatEveryDays { get; set; } = 7;
    [Range(1, 365)] public int MaxOverdueDays { get; set; } = 90;
}

public sealed class FinancialImportRequestViewModel
{
    [Required, StringLength(200)] public string FileName { get; set; } = string.Empty;
    [Required, StringLength(2_000_000)] public string Content { get; set; } = string.Empty;
    [Required, StringLength(64)] public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class FinancialImportRowViewModel
{
    public int RowNumber { get; set; }
    public Guid? UnitId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public string Block { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Competence { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<string> Messages { get; set; } = [];
    public bool IsValid => Messages.Count == 0;
}

public sealed class FinancialImportPreviewViewModel
{
    public string FileName { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public decimal TotalAmount { get; set; }
    public bool CanExecute => TotalRows > 0 && InvalidRows == 0;
    public List<string> Errors { get; set; } = [];
    public List<FinancialImportRowViewModel> Rows { get; set; } = [];
}

public sealed class FinancialImportBatchViewModel
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public FinancialImportStatus Status { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int InvalidRows { get; set; }
    public decimal TotalAmount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class FinancialImportExecutionViewModel
{
    public FinancialImportBatchViewModel Batch { get; set; } = new();
    public int CreatedCharges { get; set; }
}

public sealed class FinancialReminderDeliveryViewModel
{
    public Guid Id { get; set; }
    public Guid ChargeId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public FinancialReminderChannel Channel { get; set; }
    public FinancialReminderDeliveryStatus Status { get; set; }
    public string StageLabel { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public sealed class FinancialAutomationViewModel
{
    public List<FinancialRecurringRuleViewModel> Rules { get; set; } = [];
    public FinancialReminderPolicyViewModel ReminderPolicy { get; set; } = new();
    public List<FinancialImportBatchViewModel> Imports { get; set; } = [];
    public List<FinancialReminderDeliveryViewModel> RecentDeliveries { get; set; } = [];
}

public sealed class FinancialAutomationRunViewModel
{
    public int GeneratedCharges { get; set; }
    public int ScheduledReminders { get; set; }
    public int DeliveredReminders { get; set; }
}

public sealed class ResidentFinancialReminderViewModel
{
    public Guid ChargeId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string StageLabel { get; set; } = string.Empty;
    public FinancialReminderChannel Channel { get; set; }
    public DateTime SentAt { get; set; }
}

public static class FinancialChargeCalculator
{
    public static decimal Total(decimal baseAmount, decimal fineAmount, decimal interestAmount, decimal discountAmount) =>
        Math.Max(0m, decimal.Round(baseAmount + fineAmount + interestAmount - discountAmount, 2, MidpointRounding.AwayFromZero));

    public static bool IsOverdue(FinancialChargeStatus status, DateTime dueDate, DateTime now) =>
        (status is FinancialChargeStatus.Open or FinancialChargeStatus.PaymentReported or FinancialChargeStatus.Negotiated or FinancialChargeStatus.Disputed) &&
        dueDate.Date < now.Date;

    public static int DaysOverdue(FinancialChargeStatus status, DateTime dueDate, DateTime now) =>
        IsOverdue(status, dueDate, now) ? Math.Max(0, (now.Date - dueDate.Date).Days) : 0;

    public static string DisplayStatus(FinancialChargeStatus status, DateTime dueDate, DateTime now) => status switch
    {
        FinancialChargeStatus.Open when IsOverdue(status, dueDate, now) => "Vencida",
        FinancialChargeStatus.Open => "Em aberto",
        FinancialChargeStatus.PaymentReported => "Pagamento informado",
        FinancialChargeStatus.Paid => "Paga",
        FinancialChargeStatus.Negotiated => "Negociada",
        FinancialChargeStatus.Disputed => "Contestada",
        FinancialChargeStatus.Cancelled => "Cancelada",
        _ => status.ToString()
    };
}
