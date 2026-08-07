namespace Condotify.Models;

public sealed class BoletoBatchViewModel
{
    public Guid Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalPages { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class BoletoDocumentViewModel
{
    public Guid Id { get; set; }
    public int PageNumber { get; set; }
    public Guid? UnitId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public string MatchMethod { get; set; } = string.Empty;
    public bool Ignored { get; set; }
    public string ExtractedSnippet { get; set; } = string.Empty;
}

public sealed class BoletoBatchDetailViewModel
{
    public BoletoBatchViewModel Batch { get; set; } = new();
    public List<BoletoDocumentViewModel> Documents { get; set; } = [];
}

public sealed class BoletoPublishResultViewModel
{
    public int PublishedCount { get; set; }
    public int IgnoredCount { get; set; }
}

public sealed class ResidentBoletoViewModel
{
    public Guid DocumentId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
