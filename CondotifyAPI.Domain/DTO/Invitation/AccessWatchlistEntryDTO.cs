using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Invitation;

public sealed class AccessWatchlistEntryDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public int Severity { get; set; } = 2;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
