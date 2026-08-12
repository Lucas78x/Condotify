namespace CondotifyAPI.Domain.Enums.Invitation;

public enum AccessVisitStatusEnum
{
    Scheduled = 0,
    CheckedIn = 1,
    CheckedOut = 2,
    Canceled = 3,
    Expired = 4,
    Denied = 5,
    PendingApproval = 6,
    PendingEnrollment = 7
}

public enum VisitFacialInviteStatusEnum
{
    Pending = 0,
    Opened = 1,
    Completed = 2,
    Expired = 3,
    Canceled = 4
}
