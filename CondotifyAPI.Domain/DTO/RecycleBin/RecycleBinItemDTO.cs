using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.RecycleBin;

public sealed class RecycleBinItemDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string SnapshotJson { get; set; } = "{}";
    public string DeletedBy { get; set; } = string.Empty;
    public DateTime DeletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RestoredAt { get; set; }
    public string RestoredBy { get; set; } = string.Empty;
}
