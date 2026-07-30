namespace CondotifyAPI.Services.Observability;

public sealed class OperationalAlertWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<OperationalAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EvaluateAsync(stoppingToken);
        var seconds = Math.Clamp(
            configuration.GetValue("Observability:EvaluationIntervalSeconds", 60),
            30,
            3600);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await EvaluateAsync(stoppingToken);
    }

    private async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var evaluator = scope.ServiceProvider.GetRequiredService<IOperationalAlertEvaluationService>();
            await evaluator.EvaluateAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao avaliar alertas operacionais");
        }
    }
}
