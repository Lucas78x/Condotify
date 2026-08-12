namespace CondotifyAPI.Services.Operations;

public sealed class PreventiveMaintenanceWorker(IServiceScopeFactory scopeFactory, ILogger<PreventiveMaintenanceWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunAsync(stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await RunAsync(stoppingToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var generated = await scope.ServiceProvider.GetRequiredService<IMaintenanceService>()
                .GenerateDuePreventiveOrdersAsync(DateTime.UtcNow, cancellationToken);
            if (generated > 0) logger.LogInformation("Manutenção preventiva gerou {Count} ordem(ns) de serviço.", generated);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao gerar ordens de manutenção preventiva.");
        }
    }
}
