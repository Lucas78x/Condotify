using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Backups;

public sealed class AutomaticBackupWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AutomaticBackupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(30);
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessDueAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessDueAsync(stoppingToken);
    }

    internal async Task ProcessDueAsync(CancellationToken cancellationToken)
    {
        try
        {
            List<Guid> due;
            using (var scope = scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
                var now = DateTime.UtcNow;
                due = await context.BackupAutomationPolicies.AsNoTracking()
                    .Where(x => x.Enabled &&
                                x.NextRunAt.HasValue &&
                                x.NextRunAt <= now &&
                                (!x.LeaseExpiresAt.HasValue || x.LeaseExpiresAt <= now))
                    .OrderBy(x => x.NextRunAt)
                    .Select(x => x.LicenseId)
                    .Take(20)
                    .ToListAsync(cancellationToken);
            }
            foreach (var licenseId in due)
                await ProcessPolicyAsync(licenseId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao consultar backups automaticos pendentes.");
        }
    }

    private async Task ProcessPolicyAsync(Guid licenseId, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var now = DateTime.UtcNow;
            var claimed = await context.BackupAutomationPolicies
                .Where(x => x.LicenseId == licenseId &&
                            x.Enabled &&
                            x.NextRunAt.HasValue &&
                            x.NextRunAt <= now &&
                            (!x.LeaseExpiresAt.HasValue || x.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LeaseOwner, WorkerId)
                    .SetProperty(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(x => x.LastRunAt, now)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
            if (claimed == 0) return;

            context.ChangeTracker.Clear();
            var policy = await context.BackupAutomationPolicies
                .FirstAsync(x => x.LicenseId == licenseId, cancellationToken);
            var archive = scope.ServiceProvider.GetRequiredService<IConfigurationBackupArchiveService>();
            if (policy.ExportEnabled && !archive.ExternalStorageReady)
                throw new InvalidOperationException("O destino externo de backups nao esta configurado.");
            var runner = scope.ServiceProvider.GetRequiredService<IAutomaticBackupRunner>();
            var result = await runner.RunAsync(
                licenseId,
                "Agendador de backups",
                policy.ExportEnabled,
                policy.ExternalRetentionDays,
                cancellationToken);
            var succeeded = !policy.ExportEnabled || result.Exported;
            policy.LastSuccessAt = succeeded ? DateTime.UtcNow : policy.LastSuccessAt;
            policy.LastError = succeeded ? string.Empty : result.ExportError;
            policy.NextRunAt = DateTime.UtcNow.AddHours(Math.Clamp(policy.IntervalHours, 6, 168));
            policy.LeaseOwner = string.Empty;
            policy.LeaseExpiresAt = null;
            policy.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no backup automatico da licenca {LicenseId}.", licenseId);
            await ReleaseWithErrorAsync(licenseId, exception.Message, cancellationToken);
        }
    }

    private async Task ReleaseWithErrorAsync(
        Guid licenseId,
        string error,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var now = DateTime.UtcNow;
            await context.BackupAutomationPolicies
                .Where(x => x.LicenseId == licenseId && x.LeaseOwner == WorkerId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastError, Short(error, 1000))
                    .SetProperty(x => x.NextRunAt, now.AddHours(1))
                    .SetProperty(x => x.LeaseOwner, string.Empty)
                    .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        }
        catch (Exception releaseException)
        {
            logger.LogError(releaseException, "Falha ao liberar o lease de backup da licenca {LicenseId}.", licenseId);
        }
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
