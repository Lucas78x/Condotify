namespace CondotifyAPI.Domain.Services;

public static class DeviceHealthPolicy
{
    public static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(5);

    public static DateTime OnlineSince(DateTime utcNow) => utcNow - FreshnessWindow;

    public static bool IsOnline(bool active, DateTime? lastSeenAt, DateTime utcNow) =>
        active && lastSeenAt.HasValue && lastSeenAt.Value >= OnlineSince(utcNow);
}
