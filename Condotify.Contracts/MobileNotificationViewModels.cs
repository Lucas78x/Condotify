namespace Condotify.Models;

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

public sealed class MobileInstallationUpsertViewModel
{
    public string PushToken { get; set; } = string.Empty;
    public MobilePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string Locale { get; set; } = "pt-BR";
    public string TimeZone { get; set; } = "America/Bahia";
}

public sealed class MobileInstallationViewModel
{
    public Guid Id { get; set; }
    public string InstallationId { get; set; } = string.Empty;
    public MobilePlatform Platform { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public sealed class MobileNotificationPreferenceViewModel
{
    public MobileNotificationCategory Category { get; set; }
    public bool Enabled { get; set; }
    public bool IsEssential { get; set; }
}

public sealed class MobileNotificationPreferencesUpdateViewModel
{
    public List<MobileNotificationPreferenceViewModel> Preferences { get; set; } = [];
}

public sealed class MobileNotificationViewModel
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

public sealed class MobileNotificationPageViewModel
{
    public List<MobileNotificationViewModel> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int Unread { get; set; }
}

public static class MobileDeepLinks
{
    public const string CanonicalHost = "app.condotify.com.br";

    private static readonly string[] StaticRoutes = ["/home", "/profile", "/boletos", "/documentos"];
    private static readonly HashSet<string> EntityRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "visitors", "deliveries", "bookings", "alerts", "cameras"
    };

    public static bool TryNormalize(string? value, out string route)
    {
        route = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500)
            return false;

        var candidate = value.Trim();
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                if (!absolute.Host.Equals(CanonicalHost, StringComparison.OrdinalIgnoreCase)
                    || !absolute.IsDefaultPort
                    || !string.IsNullOrEmpty(absolute.UserInfo)
                    || !absolute.AbsolutePath.StartsWith("/app/", StringComparison.OrdinalIgnoreCase))
                    return false;

                candidate = absolute.AbsolutePath[4..];
            }
            else if (absolute.Scheme.Equals("condotify", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(absolute.UserInfo)
                    || (!string.IsNullOrEmpty(absolute.Host) && !absolute.Host.Equals("app", StringComparison.OrdinalIgnoreCase)))
                    return false;

                candidate = absolute.AbsolutePath;
            }
            else
            {
                return false;
            }

            if (!string.IsNullOrEmpty(absolute.Query) || !string.IsNullOrEmpty(absolute.Fragment))
                return false;
        }

        if (!candidate.StartsWith('/'))
            candidate = "/" + candidate;

        if (candidate.Contains('\\') || candidate.Contains("..", StringComparison.Ordinal))
            return false;

        try
        {
            candidate = Uri.UnescapeDataString(candidate);
        }
        catch (UriFormatException)
        {
            return false;
        }

        candidate = "/" + string.Join('/', candidate.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (StaticRoutes.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            route = candidate.ToLowerInvariant();
            return true;
        }

        var parts = candidate.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && EntityRoutes.Contains(parts[0]) && Guid.TryParse(parts[1], out var id))
        {
            route = $"/{parts[0].ToLowerInvariant()}/{id:D}";
            return true;
        }

        if (parts.Length == 3
            && parts[0].Equals("access", StringComparison.OrdinalIgnoreCase)
            && parts[1].Equals("events", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(parts[2], out id))
        {
            route = $"/access/events/{id:D}";
            return true;
        }

        return false;
    }

    public static string ToCanonicalUrl(string route)
    {
        if (!TryNormalize(route, out var normalized))
            throw new ArgumentException("Rota mobile invalida.", nameof(route));

        return $"https://{CanonicalHost}/app{normalized}";
    }
}
