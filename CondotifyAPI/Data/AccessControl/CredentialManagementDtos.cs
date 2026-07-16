using CondotifyAPI.Domain.Enums.Resident;
using System.ComponentModel.DataAnnotations;

namespace CondotifyAPI.Data.AccessControl;

public sealed class CreateCredentialIn
{
    public Guid ResidentId { get; set; }
    public Guid DeviceId { get; set; }
    public AccessCredentialTypeEnum Type { get; set; }

    [MaxLength(200)]
    public string Identifier { get; set; } = string.Empty;

    public string? ImageBase64 { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsTemporary { get; set; }
    public int? MaxRenewals { get; set; }
    public int? MaxUses { get; set; }
}

public sealed class RestoreCredentialIn
{
    public Guid DeviceId { get; set; }
    public string? ImageBase64 { get; set; }
}

public sealed class SetCredentialStatusIn
{
    public bool IsActive { get; set; }
}

public sealed class CredentialOut
{
    public Guid Id { get; set; }
    public Guid ResidentId { get; set; }
    public string ResidentName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsTemporary { get; set; }
    public int RenewalCount { get; set; }
    public int MaxRenewals { get; set; }
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public bool CanRenew { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public List<CredentialDeviceOut> Devices { get; set; } = [];
}

public sealed class CredentialDeviceOut
{
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string ExternalUserId { get; set; } = string.Empty;
    public string ExternalCredentialId { get; set; } = string.Empty;
    public bool IsSynced { get; set; }
    public DateTime LastSyncAt { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string RouteNames { get; set; } = string.Empty;
    public string PortalNumbers { get; set; } = string.Empty;
}

public sealed class CreateReconciliationBatchIn
{
    public bool DryRun { get; set; }
    public List<Guid> CredentialIds { get; set; } = [];
}

public sealed class AccessBatchOperationOut
{
    public Guid Id { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int SuccessfulItems { get; set; }
    public int FailedItems { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}

public sealed class AccessAuditOut
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class CredentialBackupOut
{
    public int SchemaVersion { get; set; } = 1;
    public Guid LicenseId { get; set; }
    public DateTime ExportedAt { get; set; }
    public List<CredentialBackupItem> Credentials { get; set; } = [];
}

public sealed class CredentialBackupIn
{
    public int SchemaVersion { get; set; }
    public List<CredentialBackupItem> Credentials { get; set; } = [];
}

public sealed class CredentialBackupItem
{
    public Guid ResidentId { get; set; }
    public AccessCredentialTypeEnum Type { get; set; }
    public string Identifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsTemporary { get; set; }
    public int RenewalCount { get; set; }
    public int MaxRenewals { get; set; }
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
}

public sealed class ReconciliationPreviewOut
{
    public int CredentialCount { get; set; }
    public int ResidentCount { get; set; }
    public int TargetCount { get; set; }
    public int PendingCount { get; set; }
    public List<string> Warnings { get; set; } = [];
}

public sealed class CredentialOperationOut
{
    public bool Success { get; set; }
    public bool Synced { get; set; }
    public string Message { get; set; } = string.Empty;
    public CredentialOut? Credential { get; set; }
}

public sealed class AccessEventOut
{
    public string Id { get; set; } = string.Empty;
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public bool Authorized { get; set; }
    public DateTime OccurredAt { get; set; }
    public string ExternalUserId { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public string Portal { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}
