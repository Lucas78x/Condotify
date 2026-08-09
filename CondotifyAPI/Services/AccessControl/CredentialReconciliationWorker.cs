using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class CredentialReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
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
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var reconciliation = scope.ServiceProvider.GetRequiredService<ICredentialReconciliationService>();
            var routeResolver = scope.ServiceProvider.GetRequiredService<IAccessRouteResolver>();
            var batch = await TryClaimBatchAsync(context, cancellationToken);

            if (batch is not null)
                await ProcessBatchAsync(context, reconciliation, routeResolver, batch, cancellationToken);

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

    private static async Task<AccessBatchOperationDTO?> TryClaimBatchAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await context.AccessBatchOperations
            .Where(x => x.AttemptCount >= x.MaxAttempts &&
                        (x.Status == AccessBatchStatusEnum.Queued ||
                         (x.Status == AccessBatchStatusEnum.Running && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, AccessBatchStatusEnum.DeadLetter)
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.LeaseOwner, string.Empty)
                .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null)
                .SetProperty(x => x.Error, x => string.IsNullOrEmpty(x.Error) ? "Limite de tentativas excedido." : x.Error), cancellationToken);

        var candidateIds = await context.AccessBatchOperations.AsNoTracking()
            .Where(x => x.AttemptCount < x.MaxAttempts &&
                        (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                        (x.Status == AccessBatchStatusEnum.Queued ||
                         (x.Status == AccessBatchStatusEnum.Running && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(8)
            .ToListAsync(cancellationToken);

        foreach (var candidateId in candidateIds)
        {
            var claimed = await context.AccessBatchOperations
                .Where(x => x.Id == candidateId && x.AttemptCount < x.MaxAttempts &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                            (x.Status == AccessBatchStatusEnum.Queued ||
                             (x.Status == AccessBatchStatusEnum.Running && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccessBatchStatusEnum.Running)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LeaseOwner, WorkerId)
                    .SetProperty(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(x => x.LastHeartbeatAt, now)
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.StartedAt, x => x.StartedAt ?? now), cancellationToken);
            if (claimed == 0) continue;
            context.ChangeTracker.Clear();
            return await context.AccessBatchOperations.Include(x => x.Items)
                .FirstAsync(x => x.Id == candidateId, cancellationToken);
        }
        return null;
    }

    private static async Task ProcessBatchAsync(
        DatabaseContext context,
        ICredentialReconciliationService reconciliation,
        IAccessRouteResolver routeResolver,
        AccessBatchOperationDTO batch,
        CancellationToken cancellationToken)
    {
        try
        {
            var selectedIds = ReadCredentialIds(batch.FilterJson);
            var query = context.ResidentAccessCredentials.AsNoTracking()
                .Where(x => x.Resident.Unit.Block.LicenseId == batch.LicenseId);
            query = selectedIds.Count > 0
                ? query.Where(x => selectedIds.Contains(x.Id))
                : query.Where(x => x.IsActive);
            var credentialIds = await query.OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var credentialId in credentialIds)
                await EnsureItemsAsync(context, routeResolver, batch, credentialId, cancellationToken);
            batch.TotalItems = batch.Items.Count;
            await context.SaveChangesAsync(cancellationToken);

            foreach (var credentialId in credentialIds)
            {
                batch.LastHeartbeatAt = DateTime.UtcNow;
                batch.LeaseExpiresAt = DateTime.UtcNow.Add(LeaseDuration);
                var operationItems = batch.Items.Where(x => x.CredentialId == credentialId).ToList();
                foreach (var item in operationItems)
                {
                    item.Status = AccessOperationItemStatusEnum.Running;
                    item.StartedAt ??= DateTime.UtcNow;
                    item.AttemptCount++;
                    item.NextAttemptAt = null;
                }
                await context.SaveChangesAsync(cancellationToken);
                var result = await reconciliation.ReconcileCredentialAsync(credentialId, batch.RequestedBy, cancellationToken: cancellationToken);
                foreach (var item in operationItems)
                {
                    var binding = item.DeviceId.HasValue
                        ? await context.ResidentAccessDevices.AsNoTracking().FirstOrDefaultAsync(x => x.ResidentAccessCredentialId == credentialId && x.DeviceId == item.DeviceId, cancellationToken)
                        : null;
                    var completed = binding?.SyncStatus == CredentialSyncStatusEnum.Synced || (binding is null && result.FailedCount == 0);
                    item.Status = completed
                        ? AccessOperationItemStatusEnum.Completed
                        : item.AttemptCount >= batch.MaxAttempts ? AccessOperationItemStatusEnum.DeadLetter : AccessOperationItemStatusEnum.WaitingDevice;
                    item.Error = completed ? string.Empty : Short(result.Message, 1000);
                    item.NextAttemptAt = completed ? null : DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(6, item.AttemptCount))));
                    item.FinishedAt = completed ? DateTime.UtcNow : null;
                }
                batch.ProcessedItems = batch.Items.Count(x => x.Status is AccessOperationItemStatusEnum.Completed or AccessOperationItemStatusEnum.Failed or AccessOperationItemStatusEnum.WaitingDevice);
                batch.SuccessfulItems = batch.Items.Count(x => x.Status == AccessOperationItemStatusEnum.Completed);
                batch.FailedItems = batch.Items.Count(x => x.Status is AccessOperationItemStatusEnum.Failed or AccessOperationItemStatusEnum.WaitingDevice);
                await context.SaveChangesAsync(cancellationToken);
            }

            batch.Status = batch.FailedItems == 0 ? AccessBatchStatusEnum.Completed : AccessBatchStatusEnum.CompletedWithErrors;
            batch.FinishedAt = DateTime.UtcNow;
        }
        catch (Exception exception)
        {
            batch.Error = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
            if (batch.AttemptCount >= batch.MaxAttempts)
            {
                batch.Status = AccessBatchStatusEnum.DeadLetter;
                batch.FinishedAt = DateTime.UtcNow;
            }
            else
            {
                batch.Status = AccessBatchStatusEnum.Queued;
                batch.NextAttemptAt = DateTime.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, batch.AttemptCount)));
            }
        }
        finally
        {
            batch.LeaseOwner = string.Empty;
            batch.LeaseExpiresAt = null;
            batch.LastHeartbeatAt = DateTime.UtcNow;
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task EnsureItemsAsync(
        DatabaseContext context,
        IAccessRouteResolver routeResolver,
        AccessBatchOperationDTO batch,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var credential = await context.ResidentAccessCredentials.AsNoTracking()
            .Include(x => x.Resident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident).ThenInclude(x => x.UnitLinks)
            .Include(x => x.Devices)
            .FirstAsync(x => x.Id == credentialId, cancellationToken);
        var resolution = await routeResolver.ResolveAsync(batch.LicenseId, credential.Resident, credential.CredentialType);
        var deviceIds = resolution.Targets.Select(x => x.Device.Id).Concat(credential.Devices.Select(x => x.DeviceId)).Distinct().ToList();
        if (deviceIds.Count == 0) deviceIds.Add(Guid.Empty);
        foreach (var deviceId in deviceIds)
        {
            var normalizedDeviceId = deviceId == Guid.Empty ? (Guid?)null : deviceId;
            var key = $"{batch.Id:N}:{credentialId:N}:{normalizedDeviceId?.ToString("N") ?? "none"}:reconcile";
            if (batch.Items.Any(x => x.IdempotencyKey == key)) continue;
            var item = new AccessOperationItemDTO
            {
                Id = Guid.NewGuid(), BatchId = batch.Id, Batch = batch, CredentialId = credentialId,
                DeviceId = normalizedDeviceId, Action = "ReconcileCredential", Status = AccessOperationItemStatusEnum.Queued,
                IdempotencyKey = key, CreatedAt = DateTime.UtcNow
            };
            batch.Items.Add(item);
            context.AccessOperationItems.Add(item);
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

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
