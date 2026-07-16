using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class CredentialReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CredentialReconciliationWorker> _logger;

    public CredentialReconciliationWorker(IServiceScopeFactory scopeFactory, ILogger<CredentialReconciliationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessAsync(stoppingToken);
            await timer.WaitForNextTickAsync(stoppingToken);
        }
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var reconciliation = scope.ServiceProvider.GetRequiredService<ICredentialReconciliationService>();
            var batch = await context.AccessBatchOperations
                .Where(x => x.Status == AccessBatchStatusEnum.Queued)
                .OrderBy(x => x.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (batch is not null)
                await ProcessBatchAsync(context, reconciliation, batch, cancellationToken);

            var retryIds = await context.ResidentAccessDevices.AsNoTracking()
                .Where(x => (x.SyncStatus == CredentialSyncStatusEnum.Failed || x.SyncStatus == CredentialSyncStatusEnum.RemovalPending) &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.UtcNow))
                .OrderBy(x => x.NextAttemptAt)
                .Select(x => x.ResidentAccessCredentialId)
                .Distinct()
                .Take(20)
                .ToListAsync(cancellationToken);
            foreach (var credentialId in retryIds)
                await reconciliation.ReconcileCredentialAsync(credentialId, "Reconciliacao automatica", cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Falha no processamento automatico de credenciais");
        }
    }

    private static async Task ProcessBatchAsync(
        DatabaseContext context,
        ICredentialReconciliationService reconciliation,
        AccessBatchOperationDTO batch,
        CancellationToken cancellationToken)
    {
        batch.Status = AccessBatchStatusEnum.Running;
        batch.StartedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        try
        {
            var selectedIds = ReadCredentialIds(batch.FilterJson);
            var query = context.ResidentAccessCredentials.AsNoTracking()
                .Where(x => x.Resident.Unit.Block.LicenseId == batch.LicenseId);
            query = selectedIds.Count > 0
                ? query.Where(x => selectedIds.Contains(x.Id))
                : query.Where(x => x.IsActive);
            var credentialIds = await query.OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(cancellationToken);
            batch.TotalItems = credentialIds.Count;
            await context.SaveChangesAsync(cancellationToken);

            foreach (var credentialId in credentialIds)
            {
                var result = await reconciliation.ReconcileCredentialAsync(credentialId, batch.RequestedBy, cancellationToken: cancellationToken);
                batch.ProcessedItems++;
                if (result.Success) batch.SuccessfulItems++; else batch.FailedItems++;
                await context.SaveChangesAsync(cancellationToken);
            }

            batch.Status = batch.FailedItems == 0 ? AccessBatchStatusEnum.Completed : AccessBatchStatusEnum.CompletedWithErrors;
        }
        catch (Exception exception)
        {
            batch.Status = AccessBatchStatusEnum.Failed;
            batch.Error = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
        }
        finally
        {
            batch.FinishedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static List<Guid> ReadCredentialIds(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("credentialIds", out var ids)
                ? ids.EnumerateArray().Select(x => x.GetGuid()).ToList()
                : [];
        }
        catch { return []; }
    }
}
