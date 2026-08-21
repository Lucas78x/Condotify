namespace Condotify.Models;

/// <summary>
/// Converts instants stored by the platform in UTC to the condominium's
/// configured wall-clock time. Server-side code must not use ToLocalTime(),
/// because production containers intentionally run in UTC.
/// </summary>
public static class CondotifyTime
{
    private const string DefaultTimeZone = "America/Bahia";
    private static readonly Lazy<TimeZoneInfo> DisplayZone = new(ResolveDisplayZone);

    /// <summary>The current wall-clock time in the condominium's configured zone.</summary>
    public static DateTime Now => ToCondotifyTime(DateTime.UtcNow);

    /// <summary>The current calendar date in the condominium's configured zone.</summary>
    public static DateTime Today => Now.Date;

    /// <summary>
    /// Normalizes a value supplied by a date-only UI control without applying a
    /// time-zone offset. The returned unspecified value represents a calendar day,
    /// not an instant on the UTC timeline.
    /// </summary>
    public static DateTime AsCalendarDate(this DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    public static DateTime ToCondotifyTime(this DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, DisplayZone.Value);
    }

    public static DateTimeOffset ToCondotifyTime(this DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, DisplayZone.Value);

    /// <summary>
    /// Converts a wall-clock value entered in the condominium's configured zone
    /// into a UTC instant. Unspecified values are expected from Blazor date/time
    /// controls; UTC values pass through unchanged.
    /// </summary>
    public static DateTime ToCondotifyUtc(this DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc) return value;
        if (value.Kind == DateTimeKind.Local) return value.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeToUtc(value, DisplayZone.Value);
    }

    private static TimeZoneInfo ResolveDisplayZone()
    {
        var configured = Environment.GetEnvironmentVariable("CONDOTIFY_TIME_ZONE");
        var candidates = new[]
        {
            configured,
            DefaultTimeZone,
            "America/Sao_Paulo",
            "E. South America Standard Time"
        };

        foreach (var id in candidates.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id!); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Condotify-UTC-03", TimeSpan.FromHours(-3), "Condotify UTC-03", "Condotify UTC-03");
    }
}
