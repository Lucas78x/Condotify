namespace CondotifyAPI.Data.Backups;

public sealed class CreateConfigurationBackupIn
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ConfigurationBackupOut
{
    public Guid Id { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
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

public class PreviewConfigurationRestoreIn
{
    public string Mode { get; set; } = "Merge";
    public bool IncludeDevices { get; set; } = true;
    public bool IncludeRoutes { get; set; } = true;
    public bool IncludeCredentials { get; set; } = true;
}

public sealed class ExecuteConfigurationRestoreIn : PreviewConfigurationRestoreIn
{
    public string Confirmation { get; set; } = string.Empty;
}

public sealed class ConfigurationRestorePreviewOut
{
    public Guid BackupId { get; set; }
    public int Version { get; set; }
    public string Mode { get; set; } = string.Empty;
    public bool CanRestore { get; set; }
    public int CreateCount { get; set; }
    public int UpdateCount { get; set; }
    public int DeactivateCount { get; set; }
    public int ConflictCount { get; set; }
    public List<ConfigurationRestoreSectionOut> Sections { get; set; } = [];
    public List<string> Conflicts { get; set; } = [];
}

public sealed class ConfigurationRestoreSectionOut
{
    public string Section { get; set; } = string.Empty;
    public int CreateCount { get; set; }
    public int UpdateCount { get; set; }
    public int DeactivateCount { get; set; }
    public int ConflictCount { get; set; }
}

public sealed class ConfigurationRestoreExecutionOut
{
    public Guid BackupId { get; set; }
    public int Version { get; set; }
    public string Mode { get; set; } = string.Empty;
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int DeactivatedCount { get; set; }
    public int CredentialsQueued { get; set; }
    public Guid? ReconciliationBatchId { get; set; }
    public DateTime RestoredAt { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class BackupAutomationPolicyOut
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public bool ExportEnabled { get; set; } = true;
    public int ExternalRetentionDays { get; set; } = 90;
    public DateTime? LastRunAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? NextRunAt { get; set; }
    public string LastError { get; set; } = string.Empty;
    public bool ExternalStorageReady { get; set; }
    public string ExternalStorageLabel { get; set; } = string.Empty;
}

public sealed class UpdateBackupAutomationPolicyIn
{
    public bool Enabled { get; set; }
    public int IntervalHours { get; set; } = 24;
    public bool ExportEnabled { get; set; } = true;
    public int ExternalRetentionDays { get; set; } = 90;
}

public sealed class BackupRunOut
{
    public ConfigurationBackupOut Backup { get; set; } = new();
    public bool Exported { get; set; }
    public string ExportFileName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class BackupArchiveOut
{
    public string FileName { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ImportBackupArchiveIn
{
    public string FileName { get; set; } = string.Empty;
    public string ContentBase64 { get; set; } = string.Empty;
}
