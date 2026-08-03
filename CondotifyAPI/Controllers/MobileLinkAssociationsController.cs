using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CondotifyAPI.Controllers;

[ApiController]
[AllowAnonymous]
[Route(".well-known")]
public sealed class MobileLinkAssociationsController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("assetlinks.json")]
    public IActionResult Android()
    {
        var packageName = configuration["MobileLinks:AndroidPackageName"];
        var fingerprints = configuration.GetSection("MobileLinks:AndroidSha256Fingerprints").Get<string[]>() ?? [];
        var payload = BuildAndroid(packageName, fingerprints);
        return payload is null ? NotFound() : new JsonResult(payload);
    }

    [HttpGet("apple-app-site-association")]
    public IActionResult Apple()
    {
        var payload = BuildApple(
            configuration["MobileLinks:AppleTeamId"],
            configuration["MobileLinks:AppleBundleId"]);
        return payload is null ? NotFound() : new JsonResult(payload);
    }

    internal static object[]? BuildAndroid(string? packageName, IEnumerable<string> fingerprints)
    {
        var values = fingerprints
            .Select(x => x?.Trim() ?? string.Empty)
            .Where(IsSha256Fingerprint)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!IsApplicationId(packageName) || values.Length == 0) return null;

        return
        [
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new
                {
                    @namespace = "android_app",
                    package_name = packageName!.Trim(),
                    sha256_cert_fingerprints = values
                }
            }
        ];
    }

    internal static object? BuildApple(string? teamId, string? bundleId)
    {
        if (!IsIdentifier(teamId) || !IsApplicationId(bundleId)) return null;
        return new
        {
            applinks = new
            {
                apps = Array.Empty<string>(),
                details = new[]
                {
                    new
                    {
                        appID = $"{teamId!.Trim()}.{bundleId!.Trim()}",
                        paths = new[] { "/app/*" }
                    }
                }
            }
        };
    }

    private static bool IsSha256Fingerprint(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 32 && parts.All(x => x.Length == 2 && x.All(Uri.IsHexDigit));
    }

    private static bool IsApplicationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 200 &&
        value.Contains('.') &&
        value.Split('.').All(IsIdentifier);

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 200 &&
        value.All(x => char.IsLetterOrDigit(x) || x is '_' or '-');
}
