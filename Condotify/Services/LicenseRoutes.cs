using Condotify.Models;

namespace Condotify.Services;

public static class LicenseRoutes
{
    public static string Workspace(string urlKey, string? section = null)
    {
        var key = Uri.EscapeDataString(urlKey.Trim().ToLowerInvariant());
        return string.IsNullOrWhiteSpace(section)
            ? $"/condominios/{key}"
            : $"/condominios/{key}/{Uri.EscapeDataString(section.Trim().ToLowerInvariant())}";
    }

    public static string Workspace(LicenseViewModel license, string? section = null) =>
        !string.IsNullOrWhiteSpace(license.UrlKey)
            ? Workspace(license.UrlKey, section)
            : Legacy(license.Id, section);

    public static string Legacy(Guid licenseId, string? section = null) =>
        string.IsNullOrWhiteSpace(section)
            ? $"/licencas/{licenseId}"
            : $"/licencas/{licenseId}/{section.Trim().ToLowerInvariant()}";

    public static string CanonicalizeLegacy(string? targetUrl, Guid licenseId, string urlKey)
    {
        if (string.IsNullOrWhiteSpace(targetUrl)) return Workspace(urlKey);

        var prefix = $"/licencas/{licenseId}";
        if (!targetUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return targetUrl;

        var remainder = targetUrl[prefix.Length..].Trim('/');
        return Workspace(urlKey, string.IsNullOrWhiteSpace(remainder) ? null : remainder);
    }

    private static string Legacy(string licenseId, string? section = null) =>
        string.IsNullOrWhiteSpace(section)
            ? $"/licencas/{licenseId}"
            : $"/licencas/{licenseId}/{section.Trim().ToLowerInvariant()}";
}
