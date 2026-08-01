using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.CFTV;

public sealed record MediaAccessGrant(
    Guid LicenseId,
    Guid DeviceId,
    int Channel,
    Guid UserId,
    DateTime ExpiresAt,
    StreamQuality Quality = StreamQuality.Main);

public interface IMediaAccessTokenService
{
    string Issue(MediaAccessGrant grant);
    MediaAccessGrant? Validate(string token, string expectedPath);
}

/// <summary>
/// Emite tokens de curta duracao que autorizam a leitura de UM caminho de
/// midia. Mesmo esquema AES-GCM usado por PrivateMediaStore. Nao substitui o
/// JWT: o plano de controle continua exigindo autenticacao normal.
/// </summary>
public sealed class MediaAccessTokenService : IMediaAccessTokenService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int PayloadSize = 16 + 16 + 4 + 16 + 8 + 1; // license + device + channel + user + expiry + quality

    private readonly byte[] _key;

    public MediaAccessTokenService(string secret) =>
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public MediaAccessTokenService(IConfiguration configuration)
        : this(Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET")
            ?? configuration["Media:Secret"]
            ?? throw new InvalidOperationException(
                "Defina CONDOTIFY_MEDIA_SECRET para emitir tokens de video."))
    {
    }

    public static string PathFor(Guid licenseId, Guid deviceId, int channel, StreamQuality quality) =>
        $"l{licenseId:N}_d{deviceId:N}_c{channel}_{(quality == StreamQuality.Secondary ? "s" : "m")}";

    public string Issue(MediaAccessGrant grant)
    {
        var plain = new byte[PayloadSize];
        grant.LicenseId.TryWriteBytes(plain.AsSpan(0, 16));
        grant.DeviceId.TryWriteBytes(plain.AsSpan(16, 16));
        BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(32, 4), grant.Channel);
        grant.UserId.TryWriteBytes(plain.AsSpan(36, 16));
        BinaryPrimitives.WriteInt64LittleEndian(
            plain.AsSpan(52, 8),
            new DateTimeOffset(DateTime.SpecifyKind(grant.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds());
        plain[60] = (byte)grant.Quality;

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        CryptographicOperations.ZeroMemory(plain);

        return Base64UrlEncode(payload);
    }

    public MediaAccessGrant? Validate(string token, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedPath)) return null;

        byte[] payload;
        try
        {
            payload = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (payload.Length != NonceSize + TagSize + PayloadSize) return null;

        var plain = new byte[PayloadSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                plain);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var grant = new MediaAccessGrant(
            new Guid(plain.AsSpan(0, 16)),
            new Guid(plain.AsSpan(16, 16)),
            BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(32, 4)),
            new Guid(plain.AsSpan(36, 16)),
            DateTimeOffset.FromUnixTimeSeconds(
                BinaryPrimitives.ReadInt64LittleEndian(plain.AsSpan(52, 8))).UtcDateTime,
            (StreamQuality)plain[60]);

        if (grant.ExpiresAt <= DateTime.UtcNow) return null;

        var boundPath = PathFor(grant.LicenseId, grant.DeviceId, grant.Channel, grant.Quality);
        return string.Equals(boundPath, expectedPath, StringComparison.Ordinal) ? grant : null;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '='));
    }
}
