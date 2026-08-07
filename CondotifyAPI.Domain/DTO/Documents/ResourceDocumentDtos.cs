using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Documents;

public sealed class ResourceDocumentDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public ResourceDocumentCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StorageReference { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ResourceDocumentCategoryEnum { Minutes = 0, ByLaws = 1, Covenant = 2, Announcement = 3, FinancialStatement = 4, Other = 5 }
