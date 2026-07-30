using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Backup;

public sealed class ConfigurationBackupDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public string Checksum { get; set; } = string.Empty;
    public int DeviceCount { get; set; }
    public int RouteCount { get; set; }
    public int CredentialCount { get; set; }
    public int BindingCount { get; set; }
    public int OverrideCount { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastRestoredAt { get; set; }
    public string LastRestoredBy { get; set; } = string.Empty;
}

public sealed class BackupAutomationPolicyDTO
{
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public bool ExportEnabled { get; set; } = true;
    public int ExternalRetentionDays { get; set; } = 90;
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string LeaseOwner { get; set; } = string.Empty;
    public DateTime? LeaseExpiresAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
