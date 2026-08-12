using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.DTO.Finance;

public enum FinancialChargeStatusEnum
{
    Open = 0,
    PaymentReported = 1,
    Paid = 2,
    Negotiated = 3,
    Disputed = 4,
    Cancelled = 5
}
public sealed class FinancialChargeDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid? BoletoDocumentId { get; set; }
    public BoletoDocumentDTO? BoletoDocument { get; set; }
    public string RequestKey { get; set; } = string.Empty;
    public string Competence { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public decimal BaseAmount { get; set; }
    public decimal FineAmount { get; set; }
    public decimal InterestAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public FinancialChargeStatusEnum Status { get; set; } = FinancialChargeStatusEnum.Open;
    public DateTime? PaidAt { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
    public ICollection<FinancialChargeEventDTO> Events { get; set; } = new List<FinancialChargeEventDTO>();
}

public sealed class FinancialChargeEventDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid ChargeId { get; set; }
    public FinancialChargeDTO Charge { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public FinancialChargeStatusEnum? PreviousStatus { get; set; }
    public FinancialChargeStatusEnum NewStatus { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
