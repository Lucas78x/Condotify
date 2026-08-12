namespace Condotify.Mobile.Core;

public static class MobileOfflineOwnership
{
    public static bool BelongsToCurrentSession(Guid storedUserId, Guid? currentUserId) =>
        storedUserId != Guid.Empty &&
        currentUserId.HasValue &&
        currentUserId.Value != Guid.Empty &&
        storedUserId == currentUserId.Value;
}
