using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public sealed class LprPollingService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<LprPollingService> logger) : BackgroundService
{
    // Bounds how long a single device's snapshot+OCR round trip may take
    // before it's abandoned. CftvSnapshotService and the OCR client have
    // their own per-call timeouts (8s/10s), but without this a handful of
    // offline cameras processed in sequence could still delay every device
    // behind them by tens of seconds each poll cycle.
    private static readonly TimeSpan PerDeviceTimeout = TimeSpan.FromSeconds(15);

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
                await ProcessDeviceSafelyAsync(processor, context, device, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no ciclo de reconhecimento de placas (LPR).");
        }
    }

    // A broken/offline device or an unexpected exception (e.g. a missing
    // license row inside alert-raising) must not abort the rest of the poll
    // cycle - every other device still needs its chance to run.
    private async Task ProcessDeviceSafelyAsync(LprDeviceProcessor processor, DatabaseContext context, AccessControlDeviceDTO device, CancellationToken cancellationToken)
    {
        using var deviceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deviceCts.CancelAfter(PerDeviceTimeout);

        try
        {
            await processor.ProcessAsync(context, device, deviceCts.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // Service is shutting down - let it propagate and stop the loop.
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Processamento LPR do dispositivo {DeviceId} excedeu o tempo limite de {TimeoutSeconds}s e foi cancelado.", device.Id, PerDeviceTimeout.TotalSeconds);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao processar LPR para o dispositivo {DeviceId}.", device.Id);
        }
    }
}
