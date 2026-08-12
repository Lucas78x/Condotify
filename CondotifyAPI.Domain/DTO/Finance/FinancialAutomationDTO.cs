using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.DTO.Finance;

public enum FinancialImportStatusEnum
{
    Imported = 0,
    Failed = 1
}
public enum FinancialReminderChannelEnum
{
    Push = 0,
    Email = 1
}

public enum FinancialReminderDeliveryStatusEnum
{
    Queued = 0,
    Sending = 1,
    Delivered = 2,
    Failed = 3,
    DeadLetter = 4,
    Cancelled = 5
}

public sealed class FinancialRecurringRuleDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public bool AllUnits { get; set; }
    public int GenerationDay { get; set; }
    public int DueDay { get; set; }
    public DateTime StartMonth { get; set; }
    public DateTime? EndMonth { get; set; }
    public string ReferenceTemplate { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal BaseAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public int LastGeneratedCount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public ICollection<FinancialRecurringRuleUnitDTO> Units { get; set; } = new List<FinancialRecurringRuleUnitDTO>();
}

public sealed class FinancialRecurringRuleUnitDTO : ILicenseScoped
{
    public Guid RuleId { get; set; }
    public FinancialRecurringRuleDTO Rule { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
}

public sealed class FinancialImportBatchDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public FinancialImportStatusEnum Status { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public int InvalidRows { get; set; }
    public decimal TotalAmount { get; set; }
    public string ErrorSummary { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class FinancialReminderPolicyDTO : ILicenseScoped
{
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool PushEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public string BeforeDueDays { get; set; } = "5,1";
    public bool OnDueDate { get; set; } = true;
    public int FirstOverdueDay { get; set; } = 1;
    public int RepeatEveryDays { get; set; } = 7;
    public int MaxOverdueDays { get; set; } = 90;
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

public sealed class FinancialReminderDeliveryDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid ChargeId { get; set; }
    public FinancialChargeDTO Charge { get; set; } = null!;
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public FinancialReminderChannelEnum Channel { get; set; }
    public FinancialReminderDeliveryStatusEnum Status { get; set; }
    public string StageKey { get; set; } = string.Empty;
    public string DeliveryKey { get; set; } = string.Empty;
    public string DestinationLabel { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public DateTime? NextAttemptAt { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
