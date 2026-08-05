using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public sealed class LprPollingService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<LprPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Lpr:PollIntervalSeconds", 2), 1, 60));
        using var timer = new PeriodicTimer(interval);
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var processor = scope.ServiceProvider.GetRequiredService<LprDeviceProcessor>();

            var devices = await context.Devices
                .Where(d => d.LprMode != null && d.LprCameraId != null)
                .ToListAsync(cancellationToken);

            foreach (var device in devices)
                await processor.ProcessAsync(context, device, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no ciclo de reconhecimento de placas (LPR).");
        }
    }
}
