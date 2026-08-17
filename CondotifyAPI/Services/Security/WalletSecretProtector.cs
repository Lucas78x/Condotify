using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Security;

public interface IWalletSecretProtector
{
    bool IsConfigured { get; }
    string Protect(string value, Guid enterpriseId, string purpose);
    string Unprotect(string value, Guid enterpriseId, string purpose);
}

public sealed class WalletSecretProtector(IConfiguration configuration) : IWalletSecretProtector
{
    private const string Prefix = "wallet:v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string? _masterSecret = FirstNonBlank(
        Environment.GetEnvironmentVariable("CONDOTIFY_WALLET_SECRET"),
        configuration["WalletEncryption:Secret"]);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_masterSecret) && _masterSecret.Length >= 32;

    public string Protect(string value, Guid enterpriseId, string purpose)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        EnsureConfigured();
        var plain = Encoding.UTF8.GetBytes(value);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        var aad = AssociatedData(enterpriseId, purpose);
        var key = Key();
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, aad);
            var payload = new byte[NonceSize + TagSize + cipher.Length];
            Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
            Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
            return Prefix + Convert.ToBase64String(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public string Unprotect(string value, Guid enterpriseId, string purpose)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("A credencial da carteira nao esta no formato protegido esperado.");
        EnsureConfigured();
        try
        {
            var payload = Convert.FromBase64String(value[Prefix.Length..]);
            if (payload.Length <= NonceSize + TagSize) throw new CryptographicException();
            var plain = new byte[payload.Length - NonceSize - TagSize];
            var key = Key();
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    payload.AsSpan(0, NonceSize),
                    payload.AsSpan(NonceSize + TagSize),
                    payload.AsSpan(NonceSize, TagSize),
                    plain,
                    AssociatedData(enterpriseId, purpose));
                return Encoding.UTF8.GetString(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            throw new InvalidOperationException("Nao foi possivel abrir a credencial da carteira. Verifique CONDOTIFY_WALLET_SECRET.", exception);
        }
    }

    private byte[] Key() => SHA256.HashData(Encoding.UTF8.GetBytes(_masterSecret!));
    private static byte[] AssociatedData(Guid enterpriseId, string purpose) =>
        Encoding.UTF8.GetBytes($"condotify-wallet|{enterpriseId:N}|{purpose}");
    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Defina CONDOTIFY_WALLET_SECRET com pelo menos 32 caracteres antes de salvar credenciais de carteira.");
    }
    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
