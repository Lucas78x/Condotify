namespace CondotifyAPI.Domain.Enums.AccessControl;

[Flags]
public enum AccessRouteAudienceEnum : long
{
    None = 0,
    OwnerResponsible = 1L << 0,
    TenantResponsible = 1L << 1,
    Responsible = 1L << 2,
    Resident = 1L << 3,
    Dependent = 1L << 4,
    Visitor = 1L << 5,
    ServiceProvider = 1L << 6,
    All = OwnerResponsible | TenantResponsible | Responsible | Resident | Dependent | Visitor | ServiceProvider
}

public enum AccessRouteDirectionEnum
{
    Entry = 0,
    Exit = 1,
    Bidirectional = 2
}

public enum AccessRouteOverrideModeEnum
{
    Include = 0,
    Exclude = 1
}

public enum CredentialSyncStatusEnum
{
    Pending = 0,
    Syncing = 1,
    Synced = 2,
    Failed = 3,
    RemovalPending = 4,
    Removed = 5
}

public enum AccessBatchStatusEnum
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
    Canceled = 5,
    DeadLetter = 6
}

public enum AccessOperationItemStatusEnum
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    WaitingDevice = 3,
    Failed = 4,
    Canceled = 5,
    DeadLetter = 6
}

public enum InventoryMatchStatusEnum
{
    Synced = 0,
    Divergent = 1,
    MissingRemote = 2,
    OrphanRemote = 3,
    Unknown = 4
}
