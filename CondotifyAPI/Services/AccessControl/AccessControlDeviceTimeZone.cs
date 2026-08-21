using System.Globalization;

namespace CondotifyAPI.Services.AccessControl;

internal static class AccessControlDeviceTimeZone
{
    private static readonly TimeZoneInfo Zone = Resolve(
        Environment.GetEnvironmentVariable("CONDOTIFY_TIME_ZONE"));

    public static DateTime ToLocal(DateTime value) => ToLocal(value, Zone);

    public static string FormatLocal(DateTime value, string format) =>
        ToLocal(value).ToString(format, CultureInfo.InvariantCulture);

    public static string ControlIdNtpTimeZone() => ControlIdNtpTimeZone(DateTime.UtcNow, Zone);

    public static long ToControlIdUnix(DateTime value) => ToControlIdUnix(value, Zone);

    public static DateTime FromControlIdUnix(long value) => FromControlIdUnix(value, Zone);

    public static int OffsetMinutes(DateTime utc) =>
        (int)Zone.GetUtcOffset(NormalizeUtc(utc)).TotalMinutes;

    public static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    internal static DateTime ToLocal(DateTime value, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(NormalizeUtc(value), zone);

    internal static long ToControlIdUnix(DateTime value, TimeZoneInfo zone)
    {
        // A linha de acesso da Control iD representa o relogio local em campos
        // Unix. Ex.: com UTC-3, system_information retorna 15:27 como epoch
        // 15:27 UTC, e begin_time/end_time precisam seguir o mesmo convenio.
        var deviceWallClock = ToLocal(value, zone);
        var shiftedUtc = DateTime.SpecifyKind(deviceWallClock, DateTimeKind.Utc);
        return new DateTimeOffset(shiftedUtc).ToUnixTimeSeconds();
    }

    internal static DateTime FromControlIdUnix(long value, TimeZoneInfo zone)
    {
        var shiftedUtc = DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime;
        var deviceWallClock = DateTime.SpecifyKind(shiftedUtc, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(deviceWallClock, zone);
    }

    internal static string ControlIdNtpTimeZone(DateTime utcNow, TimeZoneInfo zone)
    {
        var offset = zone.GetUtcOffset(NormalizeUtc(utcNow));
        if (offset.Ticks % TimeSpan.TicksPerHour != 0)
            throw new InvalidOperationException("O fuso dos equipamentos Control iD deve usar horas inteiras.");

        var hours = (int)offset.TotalHours;
        return $"UTC{(hours >= 0 ? "+" : string.Empty)}{hours}";
    }

    internal static TimeZoneInfo Resolve(string? configured)
    {
        foreach (var id in new[]
                 {
                     configured,
                     "America/Bahia",
                     "Bahia Standard Time",
                     "America/Sao_Paulo",
                     "E. South America Standard Time"
                 })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
