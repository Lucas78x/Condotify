using CondotifyAPI.Domain.Enums.Mobile;

namespace CondotifyAPI.Data.Mobile;

public sealed class MobileInstallationUpsertIn
{
    public string PushToken { get; set; } = string.Empty;
    public MobilePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Locale { get; set; } = "pt-BR";
    public string TimeZone { get; set; } = "America/Bahia";
}

public sealed class MobileInstallationOut
{
    public Guid Id { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public MobilePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class MobileNotificationPreferenceIn
{
    public MobileNotificationCategory Category { get; set; }
    public bool Enabled { get; set; }
}

public sealed class MobileNotificationPreferencesUpdateIn
{
    public List<MobileNotificationPreferenceIn> Preferences { get; set; } = [];
}

public sealed class MobileNotificationPreferenceOut
{
    public MobileNotificationCategory Category { get; set; }
    public bool Enabled { get; set; }
    public bool IsEssential { get; set; }
}

public sealed class MobileNotificationOut
{
    public Guid Id { get; set; }
    public MobileNotificationCategory Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string DeepLink { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public sealed class MobileNotificationPageOut
{
    public List<MobileNotificationOut> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int Unread { get; set; }
}
