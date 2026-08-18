using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.Backup;

namespace CondotifyAPI.Services.Backups;

public interface IConfigurationBackupArchiveService
{
    bool ExternalStorageReady { get; }
    string ExternalStorageLabel { get; }
    Task<BackupArchiveFile> BuildAsync(ConfigurationBackupDTO backup, CancellationToken cancellationToken);
    Task<BackupArchiveFile> ExportAsync(ConfigurationBackupDTO backup, CancellationToken cancellationToken);
    Task<ImportedConfigurationBackup> ReadAsync(
        Guid expectedLicenseId,
        byte[] content,
        CancellationToken cancellationToken);
    Task<int> CleanupAsync(Guid licenseId, int retentionDays, CancellationToken cancellationToken);
}

public sealed record BackupArchiveFile(string FileName, byte[] Content, string Sha256);

public sealed record ImportedConfigurationBackup(
    Guid LicenseId,
    int SourceVersion,
    string Name,
    string Description,
    string PayloadJson,
    string PayloadChecksum,
    DateTime ExportedAt);

public sealed class ConfigurationBackupArchiveService : IConfigurationBackupArchiveService
{
    public const int MaxArchiveBytes = 15_000_000;
    private const int ArchiveFormatVersion = 1;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 150_000;
    private const int HeaderSize = 8 + SaltSize + NonceSize + TagSize;
    private static readonly byte[] Magic = "CNDTBKP1"u8.ToArray();
    private readonly string? _root;
    private readonly string _label;
    private readonly string _secret;

    public ConfigurationBackupArchiveService(IConfiguration configuration)
    {
        _root = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH")
            ?? configuration["BackupExport:Path"];
        _label = configuration["BackupExport:Label"] ?? "Armazenamento externo";
        _secret = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET")
            ?? configuration["BackupExport:Secret"]
            ?? Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")
            ?? throw new InvalidOperationException("Defina CONDOTIFY_BACKUP_EXPORT_SECRET para proteger os arquivos de backup.");
    }

    public bool ExternalStorageReady => !string.IsNullOrWhiteSpace(_root);
    public string ExternalStorageLabel => ExternalStorageReady ? _label : "Destino externo nao configurado";

    public Task<BackupArchiveFile> BuildAsync(
        ConfigurationBackupDTO backup,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var envelope = new ArchiveEnvelope(
            ArchiveFormatVersion,
            backup.LicenseId,
            backup.Version,
            backup.Name,
            backup.Description,
            backup.PayloadJson,
            backup.Checksum,
            DateTime.UtcNow);
        var plain = JsonSerializer.SerializeToUtf8Bytes(envelope);
        if (plain.Length > MaxArchiveBytes - HeaderSize)
            throw new InvalidOperationException("O snapshot excede o limite de 15 MB para exportacao.");
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var key = DeriveKey(salt);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plain, cipher, tag, Magic);
            var content = new byte[Magic.Length + SaltSize + NonceSize + TagSize + cipher.Length];
            var offset = 0;
            Buffer.BlockCopy(Magic, 0, content, offset, Magic.Length);
            offset += Magic.Length;
            Buffer.BlockCopy(salt, 0, content, offset, SaltSize);
            offset += SaltSize;
            Buffer.BlockCopy(nonce, 0, content, offset, NonceSize);
            offset += NonceSize;
            Buffer.BlockCopy(tag, 0, content, offset, TagSize);
            offset += TagSize;
            Buffer.BlockCopy(cipher, 0, content, offset, cipher.Length);
            var fileName = $"condotify-{backup.LicenseId:N}-v{backup.Version}-{DateTime.UtcNow:yyyyMMddHHmmss}.cnbak";
            return Task.FromResult(new BackupArchiveFile(fileName, content, Sha256(content)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public async Task<BackupArchiveFile> ExportAsync(
        ConfigurationBackupDTO backup,
        CancellationToken cancellationToken)
    {
        if (!ExternalStorageReady)
            throw new InvalidOperationException("Configure CONDOTIFY_BACKUP_EXPORT_PATH antes de habilitar a copia externa.");
        var archive = await BuildAsync(backup, cancellationToken);
        var directory = LicenseDirectory(backup.LicenseId);
        Directory.CreateDirectory(directory);
        var destination = SafePath(directory, archive.FileName);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(archive.Content, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, destination, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return archive;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public Task<ImportedConfigurationBackup> ReadAsync(
        Guid expectedLicenseId,
        byte[] content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (content.Length is < HeaderSize + 1 or > MaxArchiveBytes)
            throw new InvalidDataException("O arquivo de backup esta vazio ou excede 15 MB.");
        if (!content.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("O arquivo não possui o formato de backup F&F Access.");
        var offset = Magic.Length;
        var salt = content.AsSpan(offset, SaltSize);
        offset += SaltSize;
        var nonce = content.AsSpan(offset, NonceSize);
        offset += NonceSize;
        var tag = content.AsSpan(offset, TagSize);
        offset += TagSize;
        var cipher = content.AsSpan(offset);
        var plain = new byte[cipher.Length];
        var key = DeriveKey(salt);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
            var envelope = JsonSerializer.Deserialize<ArchiveEnvelope>(plain)
                ?? throw new InvalidDataException("O conteudo do backup esta incompleto.");
            if (envelope.FormatVersion != ArchiveFormatVersion)
                throw new InvalidDataException("A versao deste arquivo de backup nao e suportada.");
            if (envelope.LicenseId != expectedLicenseId)
                throw new InvalidDataException("O arquivo pertence a outra licenca.");
            if (!string.Equals(StringSha256(envelope.PayloadJson), envelope.PayloadChecksum, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificacao interna do snapshot falhou.");
            return Task.FromResult(new ImportedConfigurationBackup(
                envelope.LicenseId,
                envelope.SourceVersion,
                envelope.Name,
                envelope.Description,
                envelope.PayloadJson,
                envelope.PayloadChecksum,
                envelope.ExportedAt));
        }
        catch (CryptographicException)
        {
            throw new InvalidDataException("Nao foi possivel abrir o backup. Verifique o segredo de exportacao.");
        }
        catch (JsonException)
        {
            throw new InvalidDataException("O conteudo descriptografado do backup esta corrompido.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public Task<int> CleanupAsync(
        Guid licenseId,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        if (!ExternalStorageReady) return Task.FromResult(0);
        var directory = LicenseDirectory(licenseId);
        if (!Directory.Exists(directory)) return Task.FromResult(0);
        var threshold = DateTime.UtcNow.AddDays(-Math.Clamp(retentionDays, 7, 3650));
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(directory, "*.cnbak", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(path) >= threshold) continue;
            File.Delete(path);
            removed++;
        }
        return Task.FromResult(removed);
    }

    private byte[] DeriveKey(ReadOnlySpan<byte> salt)
    {
        var secret = Encoding.UTF8.GetBytes(_secret);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(
                secret,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private string LicenseDirectory(Guid licenseId) =>
        SafePath(Path.GetFullPath(_root!), licenseId.ToString("N"));

    private static string SafePath(string root, string child)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var result = Path.GetFullPath(Path.Combine(normalizedRoot, child));
        if (!result.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O caminho de exportacao e invalido.");
        return result;
    }

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string StringSha256(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try { return Sha256(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private sealed record ArchiveEnvelope(
        int FormatVersion,
        Guid LicenseId,
        int SourceVersion,
        string Name,
        string Description,
        string PayloadJson,
        string PayloadChecksum,
        DateTime ExportedAt);
}
