using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Backup;
using CondotifyAPI.Infrastructure;

namespace CondotifyAPI.Services.Backups;

public interface IAutomaticBackupRunner
{
    Task<AutomaticBackupRunResult> RunAsync(
        Guid licenseId,
        string actor,
        bool exportEnabled,
        int externalRetentionDays,
        CancellationToken cancellationToken);
}

public sealed record AutomaticBackupRunResult(
    ConfigurationBackupDTO Backup,
    bool Exported,
    string ExportFileName,
    string ExportError,
    int RemovedExternalFiles);

public sealed class AutomaticBackupRunner(
    IConfigurationBackupService backupService,
    IConfigurationBackupArchiveService archiveService,
    DatabaseContext context) : IAutomaticBackupRunner
{
    public async Task<AutomaticBackupRunResult> RunAsync(
        Guid licenseId,
        string actor,
        bool exportEnabled,
        int externalRetentionDays,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var backup = await backupService.CreateAsync(
            licenseId,
            $"Automatico {now:dd/MM/yyyy HH:mm} UTC",
            "Snapshot gerado pela politica automatica de protecao.",
            actor,
            cancellationToken);
        if (!exportEnabled)
            return new AutomaticBackupRunResult(backup, false, string.Empty, string.Empty, 0);

        try
        {
            var archive = await archiveService.ExportAsync(backup, cancellationToken);
            var removed = 0;
            var cleanupWarning = string.Empty;
            try
            {
                removed = await archiveService.CleanupAsync(
                    licenseId,
                    externalRetentionDays,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                cleanupWarning = Short(exception.Message, 1000);
            }
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "ConfigurationBackup",
                EntityId = backup.Id,
                Action = "Exported",
                Status = string.IsNullOrWhiteSpace(cleanupWarning) ? "Success" : "Warning",
                Summary = string.IsNullOrWhiteSpace(cleanupWarning)
                    ? $"Backup v{backup.Version} exportado para o armazenamento externo."
                    : $"Backup v{backup.Version} exportado; a limpeza de arquivos antigos requer atencao.",
                DetailsJson = JsonSerializer.Serialize(new
                {
                    archive.FileName,
                    archive.Sha256,
                    RemovedExpiredFiles = removed,
                    CleanupWarning = cleanupWarning
                }),
                UserName = actor,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            return new AutomaticBackupRunResult(backup, true, archive.FileName, cleanupWarning, removed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var message = Short(exception.Message, 1000);
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "ConfigurationBackup",
                EntityId = backup.Id,
                Action = "ExportFailed",
                Status = "Failed",
                Summary = $"O backup v{backup.Version} foi criado, mas a copia externa falhou.",
                DetailsJson = JsonSerializer.Serialize(new { Error = message }),
                UserName = actor,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(cancellationToken);
            return new AutomaticBackupRunResult(backup, false, string.Empty, message, 0);
        }
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
