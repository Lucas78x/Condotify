using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Security;

internal static class ApiKeySecurity
{
    internal static string? GetConfiguredKey() =>
        Environment.GetEnvironmentVariable("CONDOTIFY_API_KEY") ??
        Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY") ??
        Environment.GetEnvironmentVariable("MY_API_KEY");

    internal static bool IsValid(string? configuredKey, string? suppliedKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(suppliedKey))
            return false;

        var expected = Encoding.UTF8.GetBytes(configuredKey);
        var supplied = Encoding.UTF8.GetBytes(suppliedKey);
        return expected.Length == supplied.Length &&
               CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
