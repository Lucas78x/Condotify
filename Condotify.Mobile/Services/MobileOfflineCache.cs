using System.Text.Json;
using Microsoft.Maui.Storage;

namespace Condotify.Mobile.Services;

public sealed class MobileOfflineCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync<T>(string key, T value)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new CacheEnvelope<T>(DateTimeOffset.UtcNow, value), JsonOptions);
            await SecureStorage.Default.SetAsync(CacheKey(key), payload);
        }
        catch { }
    }

    public async Task<MobileCachedValue<T>?> LoadAsync<T>(string key, TimeSpan maxAge)
    {
        try
        {
            var payload = await SecureStorage.Default.GetAsync(CacheKey(key));
            if (string.IsNullOrWhiteSpace(payload)) return null;
            var envelope = JsonSerializer.Deserialize<CacheEnvelope<T>>(payload, JsonOptions);
            if (envelope is null || DateTimeOffset.UtcNow - envelope.StoredAt > maxAge) return null;
            return new MobileCachedValue<T>(envelope.Value, envelope.StoredAt);
        }
        catch { return null; }
    }

    private static string CacheKey(string key) => $"condotify.cache.{key}";
    private sealed record CacheEnvelope<T>(DateTimeOffset StoredAt, T Value);
}

public sealed record MobileCachedValue<T>(T Value, DateTimeOffset StoredAt);
