using System.Text.RegularExpressions;

namespace CondotifyAPI.Services.Lpr;

internal static partial class PlateNormalizer
{
    // 3 letters, 1 digit, 1 alphanumeric, 2 digits: covers both the old
    // format (LLLNNNN, 4th-7th chars all digits) and Mercosul (LLLNLNN).
    [GeneratedRegex("^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$")]
    private static partial Regex PlatePattern();

    internal static string? Normalize(string? rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return null;

        var upper = rawPlate.ToUpperInvariant();
        var alphanumeric = new string(upper.Where(char.IsLetterOrDigit).ToArray());

        return PlatePattern().IsMatch(alphanumeric) ? alphanumeric : null;
    }
}
