namespace CondotifyAPI.Data.Finance;

public sealed class BoletoBatchOut
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

public sealed class BoletoDocumentOut
{
    public Guid Id { get; set; }
    public int PageNumber { get; set; }
    public Guid? UnitId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public string MatchMethod { get; set; } = string.Empty;
    public bool Ignored { get; set; }
    public string ExtractedSnippet { get; set; } = string.Empty;
}

public sealed class BoletoBatchDetailOut
{
    public BoletoBatchOut Batch { get; set; } = new();
    public List<BoletoDocumentOut> Documents { get; set; } = [];
}

public sealed class BoletoBatchUploadForm
{
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class BoletoSingleUploadForm
{
    public Guid UnitId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
    public IFormFile File { get; set; } = null!;
}

public sealed class BoletoDocumentUpdateIn
{
    public Guid? UnitId { get; set; }
    public bool Ignored { get; set; }
}

public sealed class BoletoPublishResultOut
{
    public int PublishedCount { get; set; }
    public int IgnoredCount { get; set; }
}
