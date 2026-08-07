using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Finance;

public interface IBoletoDocumentStore
{
    Task<string> StoreAsync(Guid licenseId, byte[] pdfBytes, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(Guid licenseId, string reference, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default);
}

public sealed class BoletoDocumentStore : IBoletoDocumentStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxPageBytes = 2_000_000;
    private readonly string _root;
    private readonly byte[] _key;

    public BoletoDocumentStore(IConfiguration configuration)
    {
        _root = Environment.GetEnvironmentVariable("CONDOTIFY_BOLETO_STORAGE_PATH")
            ?? configuration["BoletoStorage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "boletos");
        var secret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET")
            ?? Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")
            ?? throw new InvalidOperationException("Defina CONDOTIFY_MEDIA_SECRET para proteger os boletos.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreAsync(Guid licenseId, byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        if (pdfBytes.Length is < 1 or > MaxPageBytes) throw new InvalidOperationException("A pagina do boleto deve ter no maximo 2 MB.");
        var documentId = Guid.NewGuid();
        var directory = LicenseDirectory(licenseId);
        Directory.CreateDirectory(directory);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[pdfBytes.Length];
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, pdfBytes, cipher, tag);
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        await File.WriteAllBytesAsync(FilePath(licenseId, documentId), payload, cancellationToken);
        return Reference(licenseId, documentId);
    }

    public async Task<byte[]?> ReadAsync(Guid licenseId, string reference, CancellationToken cancellationToken = default)
    {
        if (!TryDocumentId(reference, out var documentId)) return null;
        var path = FilePath(licenseId, documentId);
        if (!File.Exists(path)) return null;
        var payload = await File.ReadAllBytesAsync(path, cancellationToken);
        if (payload.Length < NonceSize + TagSize) return null;
        var plain = new byte[payload.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                plain);
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            return null;
        }
    }

    public Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default)
    {
        if (TryDocumentId(reference, out var documentId))
        {
            var path = FilePath(licenseId, documentId);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string LicenseDirectory(Guid licenseId) => Path.Combine(_root, licenseId.ToString("N"));
    private string FilePath(Guid licenseId, Guid documentId) => Path.Combine(LicenseDirectory(licenseId), $"{documentId:N}.bin");
    private static string Reference(Guid licenseId, Guid documentId) => $"/boletos-media/{licenseId:D}/{documentId:D}";

    private static bool TryDocumentId(string? reference, out Guid documentId)
    {
        documentId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        return Guid.TryParse(reference.TrimEnd('/').Split('/').LastOrDefault(), out documentId);
    }
}
