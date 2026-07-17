using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.Security;

public sealed class EquipmentSecretConverter : ValueConverter<string, string>
{
    public EquipmentSecretConverter()
        : base(value => EquipmentSecretCryptography.Protect(value), value => EquipmentSecretCryptography.Unprotect(value))
    {
    }
}

internal static class EquipmentSecretCryptography
{
    private const string Prefix = "enc:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Key = CreateKey();

    public static string Protect(string value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        var plainText = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherText = new byte[plainText.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(Key, TagSize);
        aes.Encrypt(nonce, plainText, cipherText, tag);
        var payload = new byte[NonceSize + TagSize + cipherText.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipherText, 0, payload, NonceSize + TagSize, cipherText.Length);
        CryptographicOperations.ZeroMemory(plainText);
        return Prefix + Convert.ToBase64String(payload);
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);
            if (payload.Length <= NonceSize + TagSize) return string.Empty;
            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(NonceSize, TagSize);
            var cipherText = payload.AsSpan(NonceSize + TagSize);
            var plainText = new byte[cipherText.Length];
            using var aes = new AesGcm(Key, TagSize);
            aes.Decrypt(nonce, cipherText, tag, plainText);
            var result = Encoding.UTF8.GetString(plainText);
            CryptographicOperations.ZeroMemory(plainText);
            return result;
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Nao foi possivel descriptografar a credencial do equipamento. Verifique CONDOTIFY_EQUIPMENT_SECRET.");
        }
    }

    private static byte[] CreateKey()
    {
        var secret = Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")
            ?? Environment.GetEnvironmentVariable("JWTCondotify_Secret")
            ?? throw new InvalidOperationException("Defina CONDOTIFY_EQUIPMENT_SECRET para proteger as senhas dos equipamentos.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}
