namespace Condotify.Models;

public sealed class ResourceDocumentViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

public sealed class ResidentResourceDocumentViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
