using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Announcements;

public sealed class AnnouncementDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
