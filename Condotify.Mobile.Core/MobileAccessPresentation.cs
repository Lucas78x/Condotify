using Condotify.Models;

namespace Condotify.Mobile.Core;

public enum MobileVisitorView
{
    Today,
    Upcoming,
    History
}

public static class MobileAccessPresentation
{
    private static readonly HashSet<string> HistoricalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CheckedOut", "Denied", "Canceled", "Cancelled", "Expired"
    };

    public static bool MatchesVisitorView(ConciergeVisitViewModel visit, MobileVisitorView view, DateTime now)
    {
        var from = ToLocal(visit.ValidFrom);
        var to = ToLocal(visit.ValidTo);
        var historical = HistoricalStatuses.Contains(visit.Status) || to < now;

        return view switch
        {
            MobileVisitorView.Today => !historical && from.Date <= now.Date && to.Date >= now.Date,
            MobileVisitorView.Upcoming => !historical && from.Date > now.Date,
            MobileVisitorView.History => historical,
            _ => false
        };
    }

    public static string NormalizeTagHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = new string(value.Where(character => !char.IsWhiteSpace(character) && character is not '-' and not ':').ToArray());
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        return normalized.ToUpperInvariant();
    }

    public static string ValidateTagHex(string? value)
    {
        var normalized = NormalizeTagHex(value);
        if (normalized.Length == 0) return string.Empty;
        if (normalized.Length is < 6 or > 64) return "O código hexadecimal deve ter entre 6 e 64 caracteres.";
        if (normalized.Length % 2 != 0) return "O código hexadecimal precisa ter pares completos de caracteres.";
        if (normalized.Any(character => !Uri.IsHexDigit(character))) return "Use somente números de 0 a 9 e letras de A a F.";
        return string.Empty;
    }

    private static DateTime ToLocal(DateTime value) => value.Kind == DateTimeKind.Local ? value : value.ToLocalTime();
}
