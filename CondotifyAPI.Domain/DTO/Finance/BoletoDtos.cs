using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Domain.DTO.Finance;

public sealed class BoletoBatchDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public Guid UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public BoletoBatchStatusEnum Status { get; set; }
    public string SourceFileName { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public List<BoletoDocumentDTO> Documents { get; set; } = [];
}

public sealed class BoletoDocumentDTO
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public BoletoBatchDTO Batch { get; set; } = null!;
    public Guid? UnitId { get; set; }
    public UnitDTO? Unit { get; set; }
    public int PageNumber { get; set; }
    public BoletoMatchMethodEnum MatchMethod { get; set; }
    public bool Ignored { get; set; }
    public string StorageReference { get; set; } = string.Empty;
    public string ExtractedSnippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public enum BoletoBatchStatusEnum { Processing = 0, PendingReview = 1, Published = 2, Cancelled = 3 }
public enum BoletoMatchMethodEnum { Cpf = 0, UnitText = 1, Manual = 2, Unmatched = 3 }
