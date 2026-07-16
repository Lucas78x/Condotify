using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Invitation;

namespace CondotifyAPI.Domain.DTO.Invitation;

public class RegistrationInviteDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public string Contact { get; set; } = string.Empty;
    public RegistrationInviteChannelEnum Channel { get; set; }
    public RegistrationInviteStatusEnum Status { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public int SendCount { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
