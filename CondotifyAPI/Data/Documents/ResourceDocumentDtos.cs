namespace CondotifyAPI.Data.Documents;

public sealed class ResourceDocumentOut
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

public sealed class ResourceDocumentUploadForm
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
