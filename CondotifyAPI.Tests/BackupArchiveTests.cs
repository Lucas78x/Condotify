using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Services.Backups;
using Microsoft.Extensions.Configuration;

namespace CondotifyAPI.Tests;

public sealed class BackupArchiveTests
{
    [Fact]
    public async Task Archive_ShouldEncryptAndRoundTrip()
    {
        var root = TempDirectory();
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", "portable-backup-test-secret-with-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", root);
            var service = CreateService(root);
            var backup = CreateBackup();

            var archive = await service.BuildAsync(backup, CancellationToken.None);
            var imported = await service.ReadAsync(backup.LicenseId, archive.Content, CancellationToken.None);

            Assert.EndsWith(".cnbak", archive.FileName, StringComparison.Ordinal);
            Assert.DoesNotContain("terminal-password", Encoding.Latin1.GetString(archive.Content), StringComparison.Ordinal);
            Assert.Equal(Sha256(archive.Content), archive.Sha256);
            Assert.Equal(backup.PayloadJson, imported.PayloadJson);
            Assert.Equal(backup.Checksum, imported.PayloadChecksum);
            Assert.Equal(backup.Version, imported.SourceVersion);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", previousPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Archive_ShouldRejectTamperingAndWrongLicense()
    {
        var root = TempDirectory();
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", "portable-backup-test-secret-with-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", root);
            var service = CreateService(root);
            var backup = CreateBackup();
            var archive = await service.BuildAsync(backup, CancellationToken.None);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ReadAsync(Guid.NewGuid(), archive.Content, CancellationToken.None));
            archive.Content[^1] ^= 0xFF;
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.ReadAsync(backup.LicenseId, archive.Content, CancellationToken.None));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", previousPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExternalStore_ShouldWriteAtomicallyAndApplyRetention()
    {
        var root = TempDirectory();
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", "portable-backup-test-secret-with-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", root);
            var service = CreateService(root);
            var backup = CreateBackup();

            var archive = await service.ExportAsync(backup, CancellationToken.None);
            var path = Directory.EnumerateFiles(root, "*.cnbak", SearchOption.AllDirectories).Single();
            Assert.Equal(archive.Content, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories));

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-31));
            var removed = await service.CleanupAsync(backup.LicenseId, 30, CancellationToken.None);

            Assert.Equal(1, removed);
            Assert.False(File.Exists(path));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_BACKUP_EXPORT_PATH", previousPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ConfigurationBackupArchiveService CreateService(string root) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BackupExport:Path"] = root,
                ["BackupExport:Label"] = "Teste externo"
            })
            .Build());

    private static ConfigurationBackupDTO CreateBackup()
    {
        const string payload = """{"formatVersion":1,"devices":[{"password":"terminal-password"}]}""";
        return new ConfigurationBackupDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = Guid.NewGuid(),
            Version = 7,
            Name = "Antes da manutencao",
            Description = "Snapshot de teste",
            PayloadJson = payload,
            Checksum = Sha256(Encoding.UTF8.GetBytes(payload)),
            CreatedBy = "Teste",
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), $"condotify-backup-{Guid.NewGuid():N}");

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
