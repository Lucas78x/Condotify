using CondotifyAPI.Domain.Enums.Mobile;

namespace CondotifyAPI.Domain.DTO.Mobile;

public sealed class PushPreferenceDTO
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public MobileNotificationCategory Category { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
