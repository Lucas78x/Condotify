using System.Security.Cryptography;
using System.Text;

namespace Condotify.Models;

public enum OfflineDeviceStatus
{
    Pending = 0,
    Approved = 1,
    Revoked = 2
}

public enum OfflineOperationKind
{
    VisitCheckIn = 0,
    VisitCheckOut = 1
}

public enum OfflineOperationStatus
{
    Pending = 0,
    Applied = 1,
    Duplicate = 2,
    Conflict = 3,
    Rejected = 4
}

public class OfflineDeviceRegistrationViewModel
{
    public string InstallationId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
}

public sealed class OfflineDevicePolicyViewModel
{
    public OfflineDeviceStatus Status { get; set; }
    public int OfflineWindowMinutes { get; set; } = 480;
    public bool IsPrimaryValidator { get; set; }
}

public sealed class OfflineDeviceViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string InstallationId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public OfflineDeviceStatus Status { get; set; }
    public int OfflineWindowMinutes { get; set; }
    public bool IsPrimaryValidator { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime? LastBundleExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string RevokedBy { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
    public int PendingOperations { get; set; }
    public int ConflictOperations { get; set; }

    // Retornado apenas ao próprio usuário autenticado durante registro/sync.
    // Nunca é incluído nos endpoints administrativos.
    public string DeviceSecret { get; set; } = string.Empty;
}

public sealed class OfflineRouteWindowViewModel
{
    public Guid RouteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DaysOfWeekMask { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public sealed class OfflineVisitPermitViewModel
{
    public Guid VisitId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string Status { get; set; } = "Scheduled";
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public List<OfflineRouteWindowViewModel> Routes { get; set; } = [];
}

public sealed class OfflineAccessBundlePayloadViewModel
{
    public int SchemaVersion { get; set; } = 1;
    public Guid BundleId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime ServerTime { get; set; }
    public int UtcOffsetMinutes { get; set; } = -180;
    public bool IsPrimaryValidator { get; set; }
    public List<OfflineVisitPermitViewModel> Visits { get; set; } = [];
}

public sealed class OfflineAccessBundleEnvelopeViewModel
{
    public string Algorithm { get; set; } = OfflineBundleAuthenticator.Algorithm;
    public string KeyId { get; set; } = string.Empty;
    public string PayloadBase64 { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public sealed class OfflineOperationUploadViewModel
{
    public Guid ClientOperationId { get; set; }
    public Guid BundleId { get; set; }
    public OfflineOperationKind Kind { get; set; } = OfflineOperationKind.VisitCheckIn;
    public Guid VisitId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

public sealed class OfflineSyncRequestViewModel : OfflineDeviceRegistrationViewModel
{
    public List<OfflineOperationUploadViewModel> Operations { get; set; } = [];
}

public sealed class OfflineOperationResultViewModel
{
    public Guid Id { get; set; }
    public Guid ClientOperationId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid VisitId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public OfflineOperationKind Kind { get; set; }
    public OfflineOperationStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime ReceivedAt { get; set; }
}

public sealed class OfflineSyncResultViewModel
{
    public OfflineDeviceViewModel Device { get; set; } = new();
    public OfflineAccessBundleEnvelopeViewModel? Bundle { get; set; }
    public List<OfflineOperationResultViewModel> Operations { get; set; } = [];
    public DateTime ServerTime { get; set; }
}

public sealed class OfflineOperationPageViewModel
{
    public List<OfflineOperationResultViewModel> Items { get; set; } = [];
    public int Total { get; set; }
    public int Applied { get; set; }
    public int Conflicts { get; set; }
    public int Rejected { get; set; }
}

public static class OfflineAccessCode
{
    public static string Normalize(string? value)
    {
        var raw = value?.Trim() ?? string.Empty;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            var code = ReadQueryValue(uri.Query, "code");
            raw = string.IsNullOrWhiteSpace(code)
                ? uri.Segments.LastOrDefault()?.Trim('/') ?? raw
                : code;
        }

        return raw.Trim().ToUpperInvariant();
    }

    public static string Hash(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string ReadQueryValue(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var name = separator < 0 ? pair : pair[..separator];
            if (!Uri.UnescapeDataString(name).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }

        return string.Empty;
    }
}

public static class OfflineBundleAuthenticator
{
    public const string Algorithm = "HMAC-SHA256";

    public static string Sign(string payloadBase64, string secretBase64)
    {
        var key = Convert.FromBase64String(secretBase64);
        try
        {
            return Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payloadBase64)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public static bool Verify(OfflineAccessBundleEnvelopeViewModel envelope, string secretBase64)
    {
        if (!envelope.Algorithm.Equals(Algorithm, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(envelope.PayloadBase64) ||
            string.IsNullOrWhiteSpace(envelope.Signature) ||
            string.IsNullOrWhiteSpace(secretBase64)) return false;

        try
        {
            var expected = Convert.FromBase64String(Sign(envelope.PayloadBase64, secretBase64));
            var actual = Convert.FromBase64String(envelope.Signature);
            try { return CryptographicOperations.FixedTimeEquals(expected, actual); }
            finally
            {
                CryptographicOperations.ZeroMemory(expected);
                CryptographicOperations.ZeroMemory(actual);
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class OfflineRouteSchedule
{
    public static bool IsAllowed(IReadOnlyCollection<OfflineRouteWindowViewModel> routes, DateTime trustedUtc, int utcOffsetMinutes)
    {
        if (routes.Count == 0) return false;
        var utc = trustedUtc.Kind == DateTimeKind.Utc ? trustedUtc : trustedUtc.ToUniversalTime();
        var local = utc.AddMinutes(utcOffsetMinutes);
        var dayBit = 1 << (int)local.DayOfWeek;
        var time = local.TimeOfDay;
        return routes.Any(route =>
            (route.DaysOfWeekMask & dayBit) != 0 &&
            IsWithinWindow(time, route.StartTime, route.EndTime));
    }

    private static bool IsWithinWindow(TimeSpan value, TimeSpan start, TimeSpan end) =>
        start <= end ? value >= start && value <= end : value >= start || value <= end;
}
