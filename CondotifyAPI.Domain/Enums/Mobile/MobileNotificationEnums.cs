namespace CondotifyAPI.Domain.Enums.Mobile;

public enum MobilePlatform
{
    Unknown = 0,
    Android = 1,
    Ios = 2,
    Windows = 3
}

public enum MobileNotificationCategory
{
    Access = 1,
    Visitor = 2,
    Delivery = 3,
    Booking = 4,
    Security = 5,
    Operational = 6,
    System = 7,
    Financial = 8,
    Announcement = 9
}

public enum PushDeliveryStatus
{
    Queued = 0,
    Sending = 1,
    Delivered = 2,
    Failed = 3,
    DeadLetter = 4,
    Cancelled = 5
}
