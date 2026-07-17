using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Security;

public interface IPrivateMediaStore
{
    Task<string> StoreDataUriAsync(Guid licenseId, string dataUri, CancellationToken cancellationToken = default);
    Task<string?> ResolveDataUriAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default);
    Task<PrivateMediaFile?> ReadAsync(Guid licenseId, Guid mediaId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default);
}

public sealed record PrivateMediaFile(byte[] Content, string ContentType);

public sealed class PrivateMediaStore : IPrivateMediaStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _root;
    private readonly byte[] _key;

    public PrivateMediaStore(IConfiguration configuration)
    {
        _root = Environment.GetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH")
            ?? configuration["PrivateMedia:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "private-media");
        var secret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET")
            ?? Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")
            ?? throw new InvalidOperationException("Defina CONDOTIFY_MEDIA_SECRET para proteger as imagens privadas.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreDataUriAsync(Guid licenseId, string dataUri, CancellationToken cancellationToken = default)
    {
        var (contentType, plain) = Decode(dataUri);
        if (plain.Length is < 1 or > 5_000_000) throw new InvalidOperationException("A imagem deve ter no maximo 5 MB.");
        var mediaId = Guid.NewGuid();
        var directory = LicenseDirectory(licenseId);
        Directory.CreateDirectory(directory);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[plain.Length];
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);
        var typeBytes = Encoding.ASCII.GetBytes(contentType);
        var payload = new byte[1 + typeBytes.Length + NonceSize + TagSize + cipher.Length];
        payload[0] = checked((byte)typeBytes.Length);
        Buffer.BlockCopy(typeBytes, 0, payload, 1, typeBytes.Length);
        Buffer.BlockCopy(nonce, 0, payload, 1 + typeBytes.Length, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, 1 + typeBytes.Length + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, 1 + typeBytes.Length + NonceSize + TagSize, cipher.Length);
        CryptographicOperations.ZeroMemory(plain);
        await File.WriteAllBytesAsync(FilePath(licenseId, mediaId), payload, cancellationToken);
        return Reference(licenseId, mediaId);
    }

    public async Task<string?> ResolveDataUriAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        if (reference.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return reference;
        if (!TryMediaId(reference, out var mediaId)) return null;
        var file = await ReadAsync(licenseId, mediaId, cancellationToken);
        return file is null ? null : $"data:{file.ContentType};base64,{Convert.ToBase64String(file.Content)}";
    }

    public async Task<PrivateMediaFile?> ReadAsync(Guid licenseId, Guid mediaId, CancellationToken cancellationToken = default)
    {
        var path = FilePath(licenseId, mediaId);
        if (!File.Exists(path)) return null;
        var payload = await File.ReadAllBytesAsync(path, cancellationToken);
        if (payload.Length < 1 + NonceSize + TagSize) return null;
        var typeLength = payload[0];
        if (typeLength == 0 || payload.Length <= 1 + typeLength + NonceSize + TagSize) return null;
        var contentType = Encoding.ASCII.GetString(payload, 1, typeLength);
        var offset = 1 + typeLength;
        var plain = new byte[payload.Length - offset - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            payload.AsSpan(offset, NonceSize),
            payload.AsSpan(offset + NonceSize + TagSize),
            payload.AsSpan(offset + NonceSize, TagSize),
            plain);
        return new PrivateMediaFile(plain, contentType);
    }

    public Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default)
    {
        if (TryMediaId(reference, out var mediaId)) File.Delete(FilePath(licenseId, mediaId));
        return Task.CompletedTask;
    }

    private string LicenseDirectory(Guid licenseId) => Path.Combine(_root, licenseId.ToString("N"));
    private string FilePath(Guid licenseId, Guid mediaId) => Path.Combine(LicenseDirectory(licenseId), $"{mediaId:N}.bin");
    public static string Reference(Guid licenseId, Guid mediaId) => $"/private-media/{licenseId:D}/{mediaId:D}";
    public static bool TryMediaId(string? reference, out Guid mediaId)
    {
        mediaId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        return Guid.TryParse(reference.TrimEnd('/').Split('/').LastOrDefault(), out mediaId);
    }

    private static (string ContentType, byte[] Content) Decode(string dataUri)
    {
        var comma = dataUri.IndexOf(',');
        if (comma <= 5 || !dataUri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Formato de imagem invalido.");
        var contentType = dataUri[5..dataUri[..comma].IndexOf(';')].ToLowerInvariant();
        if (contentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new InvalidOperationException("Use uma imagem JPG, PNG ou WebP.");
        try { return (contentType, Convert.FromBase64String(dataUri[(comma + 1)..])); }
        catch (FormatException) { throw new InvalidOperationException("Os dados da imagem estao corrompidos."); }
    }
}
