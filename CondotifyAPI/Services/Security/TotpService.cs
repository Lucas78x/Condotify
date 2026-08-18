using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Security;

public interface ITotpService
{
    string GenerateSecret();
    bool Verify(string secret, string code, DateTime? utcNow = null);
    string BuildUri(string secret, string email);
}

public sealed class TotpService : ITotpService
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public bool Verify(string secret, string code, DateTime? utcNow = null)
    {
        var normalized = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length != 6 || string.IsNullOrWhiteSpace(secret)) return false;
        var timestamp = new DateTimeOffset(utcNow ?? DateTime.UtcNow).ToUnixTimeSeconds() / 30;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = Encoding.ASCII.GetBytes(Code(secret, timestamp + offset));
            var supplied = Encoding.ASCII.GetBytes(normalized);
            if (CryptographicOperations.FixedTimeEquals(expected, supplied)) return true;
        }
        return false;
    }

    public string BuildUri(string secret, string email) =>
        $"otpauth://totp/F%26F%20Access:{Uri.EscapeDataString(email)}?secret={secret}&issuer=F%26F%20Access&digits=6&period=30";

    private static string Code(string secret, long counter)
    {
        var key = Base32Decode(secret);
        Span<byte> counterBytes = stackalloc byte[8];
        for (var index = 7; index >= 0; index--) { counterBytes[index] = (byte)(counter & 0xff); counter >>= 8; }
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static string Base32Encode(byte[] data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0; var bits = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value; bits += 8;
            while (bits >= 5) { output.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) output.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var output = new List<byte>(); var buffer = 0; var bits = 0;
        foreach (var character in value.ToUpperInvariant().Where(x => !char.IsWhiteSpace(x) && x != '-'))
        {
            var index = Alphabet.IndexOf(character); if (index < 0) continue;
            buffer = (buffer << 5) | index; bits += 5;
            if (bits < 8) continue;
            output.Add((byte)(buffer >> (bits - 8))); bits -= 8;
        }
        return output.ToArray();
    }
}
