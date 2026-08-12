namespace CondotifyAPI.Domain.Enums.AccessControl;

public enum OfflineDeviceStatusEnum
{
    Pending = 0,
    Approved = 1,
    Revoked = 2
}

public enum OfflineOperationKindEnum
{
    VisitCheckIn = 0,
    VisitCheckOut = 1
}

public enum OfflineOperationStatusEnum
{
    Pending = 0,
    Applied = 1,
    Duplicate = 2,
    Conflict = 3,
    Rejected = 4
}
