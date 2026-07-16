namespace CondotifyAPI.Domain.Enums.Invitation;

public enum RegistrationInviteChannelEnum
{
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    Link = 4
}

public enum RegistrationInviteStatusEnum
{
    Pending = 1,
    Opened = 2,
    Completed = 3,
    Expired = 4,
    Canceled = 5
}
