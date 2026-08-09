using CondotifyAPI.Domain.DTO.Users;

namespace CondotifyAPI.Domain.DTO.License;

public class LicenseUserAccessDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid UserId { get; set; }
    public UserAccessDTO User { get; set; } = null!;
    public LicenseAccessRoleEnum Role { get; set; }
    public LicensePermissionEnum Permissions { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
