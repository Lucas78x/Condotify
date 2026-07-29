using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.RecycleBin;

public sealed class RecycleBinCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<RecycleBinCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        await CleanupAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await CleanupAsync(stoppingToken);
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var recycleBin = scope.ServiceProvider.GetRequiredService<IRecycleBinService>();
            var now = DateTime.UtcNow;
            var expired = await context.RecycleBinItems.AsNoTracking()
                .Where(x => !x.RestoredAt.HasValue && x.ExpiresAt <= now)
                .OrderBy(x => x.ExpiresAt)
                .Select(x => new { x.Id, x.LicenseId })
                .Take(200)
                .ToListAsync(cancellationToken);

            foreach (var item in expired)
                await recycleBin.PurgeAsync(item.LicenseId, item.Id, cancellationToken);

            if (expired.Count > 0)
                logger.LogInformation("{Count} item(ns) expirado(s) removido(s) da lixeira.", expired.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao limpar itens expirados da lixeira.");
        }
    }
}
