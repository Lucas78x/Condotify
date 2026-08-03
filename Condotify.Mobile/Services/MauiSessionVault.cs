using System.Text.Json;
using Condotify.Mobile.Core;

namespace Condotify.Mobile.Services;

public sealed class MauiSessionVault : IMobileSessionVault
{
    private const string SessionKey = "condotify.mobile.session.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MobileSession?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await SecureStorage.Default.GetAsync(SessionKey);
            return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<MobileSession>(json, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            SecureStorage.Default.Remove(SessionKey);
            return null;
        }
    }

    public Task SaveAsync(MobileSession session, CancellationToken cancellationToken = default) =>
        SecureStorage.Default.SetAsync(SessionKey, JsonSerializer.Serialize(session, JsonOptions));

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        SecureStorage.Default.Remove(SessionKey);
        return Task.CompletedTask;
    }
}
