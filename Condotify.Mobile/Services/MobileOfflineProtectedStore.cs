using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Condotify.Models;

namespace Condotify.Mobile.Services;

public sealed class MobileOfflineDatabase
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, MobileOfflineLicenseState> Licenses { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class MobileOfflineLicenseState
{
    public Guid LicenseId { get; set; }
    public Guid UserId { get; set; }
    public OfflineDeviceViewModel? Device { get; set; }
    public string DeviceSecret { get; set; } = string.Empty;
    public OfflineAccessBundleEnvelopeViewModel? BundleEnvelope { get; set; }
    public OfflineAccessBundlePayloadViewModel? Bundle { get; set; }
    public DateTime BundleReceivedAtLocalUtc { get; set; }
    public DateTime LastTrustedUtc { get; set; }
    public List<OfflineOperationUploadViewModel> Outbox { get; set; } = [];
    public List<OfflineOperationResultViewModel> RecentResults { get; set; } = [];
}

public sealed class MobileOfflineProtectedStore
{
    private const string KeyName = "condotify.offline-store-key.v1";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Aad = Encoding.UTF8.GetBytes("CondotifyOffline:v1");
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string StorePath => Path.Combine(FileSystem.Current.AppDataDirectory, "condotify-offline-v1.dat");

    public async Task<MobileOfflineDatabase> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(StorePath)) return new MobileOfflineDatabase();
            var payload = await File.ReadAllBytesAsync(StorePath, cancellationToken);
            if (payload.Length <= NonceSize + TagSize) return new MobileOfflineDatabase();
            var key = await GetKeyAsync();
            var plain = new byte[payload.Length - NonceSize - TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    payload.AsSpan(0, NonceSize),
                    payload.AsSpan(NonceSize + TagSize),
                    payload.AsSpan(NonceSize, TagSize),
                    plain,
                    Aad);
                return JsonSerializer.Deserialize<MobileOfflineDatabase>(plain, JsonOptions) ?? new MobileOfflineDatabase();
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException)
            {
                TryDelete(StorePath);
                return new MobileOfflineDatabase();
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(MobileOfflineDatabase database, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(database, JsonOptions);
            var key = await GetKeyAsync();
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            var cipher = new byte[plain.Length];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plain, cipher, tag, Aad);
                var payload = new byte[NonceSize + TagSize + cipher.Length];
                Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
                Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
                Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                var temporary = StorePath + ".tmp";
                await File.WriteAllBytesAsync(temporary, payload, cancellationToken);
                File.Move(temporary, StorePath, true);
                CryptographicOperations.ZeroMemory(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(cipher);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(tag);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            TryDelete(StorePath);
            TryDelete(StorePath + ".tmp");
            SecureStorage.Default.Remove(KeyName);
        }
        finally { _gate.Release(); }
    }

    private static async Task<byte[]> GetKeyAsync()
    {
        var stored = await SecureStorage.Default.GetAsync(KeyName);
        if (!string.IsNullOrWhiteSpace(stored))
        {
            try
            {
                var existing = Convert.FromBase64String(stored);
                if (existing.Length == 32) return existing;
                CryptographicOperations.ZeroMemory(existing);
            }
            catch (FormatException) { }
        }

        var key = RandomNumberGenerator.GetBytes(32);
        await SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(key));
        return key;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
