using System.Globalization;
using System.Text;

namespace CondotifyAPI.Domain.Utilities;

public static class UrlKeyGenerator
{
    public static string Create(string? preferredValue, string? fallbackValue = null)
    {
        var source = string.IsNullOrWhiteSpace(preferredValue) ? fallbackValue : preferredValue;
        if (string.IsNullOrWhiteSpace(source)) return "condominio";

        var normalized = source.Trim().Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        var separatorPending = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0) result.Append('-');
                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = result.Length > 0;
            }
        }

        var key = result.ToString().Trim('-');
        if (key.Length > 100) key = key[..100].TrimEnd('-');
        return string.IsNullOrWhiteSpace(key) ? "condominio" : key;
    }
}
