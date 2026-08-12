using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public sealed class CredentialReconciliationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
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

            var retries = await context.ResidentAccessDevices.AsNoTracking()
                .Where(x => (x.SyncStatus == CredentialSyncStatusEnum.Failed || x.SyncStatus == CredentialSyncStatusEnum.RemovalPending) &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.UtcNow) &&
                            !context.AccessOperationItems.Any(item =>
                                item.CredentialId == x.ResidentAccessCredentialId &&
                                (item.Batch.Status == AccessBatchStatusEnum.Queued || item.Batch.Status == AccessBatchStatusEnum.Running)))
                .OrderBy(x => x.NextAttemptAt)
                .Select(x => new { CredentialId = x.ResidentAccessCredentialId, x.Device.LicenseId })
                .Distinct()
                .Take(20)
                .ToListAsync(cancellationToken);
            foreach (var retry in retries)
                await reconciliation.ReconcileCredentialAsync(retry.CredentialId, "Reconciliacao automatica", licenseId: retry.LicenseId, cancellationToken: cancellationToken);
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
        var exhaustedIds = await context.AccessBatchOperations.AsNoTracking()
            .Where(x => x.AttemptCount >= x.MaxAttempts &&
                        (x.Status == AccessBatchStatusEnum.Queued ||
                         (x.Status == AccessBatchStatusEnum.Running && (x.LeaseExpiresAt == null || x.LeaseExpiresAt <= now))))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        if (exhaustedIds.Count > 0)
        {
            await context.AccessOperationItems
                .Where(x => exhaustedIds.Contains(x.BatchId) &&
                            (x.Status == AccessOperationItemStatusEnum.Queued ||
                             x.Status == AccessOperationItemStatusEnum.Running ||
                             x.Status == AccessOperationItemStatusEnum.WaitingDevice ||
                             x.Status == AccessOperationItemStatusEnum.Failed))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccessOperationItemStatusEnum.DeadLetter)
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.FinishedAt, now)
                    .SetProperty(x => x.Error, x => string.IsNullOrEmpty(x.Error) ? "Limite de tentativas excedido." : x.Error), cancellationToken);
        }

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

        if (exhaustedIds.Count > 0)
        {
            context.ChangeTracker.Clear();
            var exhaustedBatches = await context.AccessBatchOperations
                .Include(x => x.Items)
                .Where(x => exhaustedIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            foreach (var exhaustedBatch in exhaustedBatches)
            {
                AccessOperationPolicy.RefreshCounts(exhaustedBatch);
                AddBatchAudit(context, exhaustedBatch);
            }
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
        }

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
            var query = context.ResidentAccessCredentials.AsNoTracking().ForLicense(batch.LicenseId);
            query = selectedIds.Count > 0
                ? query.Where(x => selectedIds.Contains(x.Id))
                : query.Where(x => x.IsActive);
            var credentialIds = await query.OrderBy(x => x.CreatedAt).Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var credentialId in credentialIds)
                await EnsureItemsAsync(context, routeResolver, batch, credentialId, cancellationToken);
            AccessOperationPolicy.RefreshCounts(batch);
            await context.SaveChangesAsync(cancellationToken);

            var pendingCredentialIds = batch.Items
                .Where(x => x.CredentialId.HasValue &&
                            AccessOperationPolicy.IsPending(x.Status) &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.UtcNow))
                .Select(x => x.CredentialId!.Value)
                .Distinct()
                .ToList();

            foreach (var credentialId in pendingCredentialIds)
            {
                await context.Entry(batch).ReloadAsync(cancellationToken);
                if (batch.Status == AccessBatchStatusEnum.Canceled)
                    break;

                foreach (var trackedItem in batch.Items.Where(x => x.CredentialId == credentialId))
                    await context.Entry(trackedItem).ReloadAsync(cancellationToken);

                batch.LastHeartbeatAt = DateTime.UtcNow;
                batch.LeaseExpiresAt = DateTime.UtcNow.Add(LeaseDuration);
                var operationItems = batch.Items
                    .Where(x => x.CredentialId == credentialId && x.Status is
                        AccessOperationItemStatusEnum.Queued or
                        AccessOperationItemStatusEnum.Running or
                        AccessOperationItemStatusEnum.WaitingDevice or
                        AccessOperationItemStatusEnum.Failed)
                    .Where(x => x.NextAttemptAt == null || x.NextAttemptAt <= DateTime.UtcNow)
                    .ToList();
                if (operationItems.Count == 0)
                    continue;

                foreach (var item in operationItems)
                {
                    item.Status = AccessOperationItemStatusEnum.Running;
                    item.StartedAt ??= DateTime.UtcNow;
                    item.AttemptCount++;
                    item.NextAttemptAt = null;
                }
                await context.SaveChangesAsync(cancellationToken);
                var result = await reconciliation.ReconcileCredentialAsync(credentialId, batch.RequestedBy, licenseId: batch.LicenseId, cancellationToken: cancellationToken);
                foreach (var item in operationItems)
                {
                    var binding = item.DeviceId.HasValue
                        ? await context.ResidentAccessDevices.AsNoTracking().FirstOrDefaultAsync(x => x.ResidentAccessCredentialId == credentialId && x.DeviceId == item.DeviceId, cancellationToken)
                        : null;
                    var completed = binding?.SyncStatus is CredentialSyncStatusEnum.Synced or CredentialSyncStatusEnum.Removed ||
                                    (binding is null && result.FailedCount == 0);
                    item.Status = completed
                        ? AccessOperationItemStatusEnum.Completed
                        : item.AttemptCount >= batch.MaxAttempts ? AccessOperationItemStatusEnum.DeadLetter : AccessOperationItemStatusEnum.WaitingDevice;
                    item.Error = completed ? string.Empty : Short(result.Message, 1000);
                    item.NextAttemptAt = completed || item.Status == AccessOperationItemStatusEnum.DeadLetter
                        ? null
                        : DateTime.UtcNow.Add(AccessOperationPolicy.RetryDelay(item.AttemptCount));
                    item.FinishedAt = completed || item.Status == AccessOperationItemStatusEnum.DeadLetter
                        ? DateTime.UtcNow
                        : null;
                }
                AccessOperationPolicy.RefreshCounts(batch);
                await context.SaveChangesAsync(cancellationToken);
            }

            await context.Entry(batch).ReloadAsync(cancellationToken);
            if (batch.Status == AccessBatchStatusEnum.Canceled)
            {
                foreach (var item in batch.Items)
                    await context.Entry(item).ReloadAsync(cancellationToken);
                AccessOperationPolicy.RefreshCounts(batch);
                batch.NextAttemptAt = null;
                batch.FinishedAt ??= DateTime.UtcNow;
            }
            else
            {
                var retryableItems = batch.Items.Where(x => AccessOperationPolicy.IsPending(x.Status)).ToList();
                if (retryableItems.Count > 0 && batch.AttemptCount < batch.MaxAttempts)
                {
                    batch.Status = AccessBatchStatusEnum.Queued;
                    batch.NextAttemptAt = retryableItems
                        .Select(x => x.NextAttemptAt ?? DateTime.UtcNow.Add(AccessOperationPolicy.RetryDelay(batch.AttemptCount)))
                        .Min();
                    batch.FinishedAt = null;
                }
                else
                {
                    foreach (var item in retryableItems)
                    {
                        item.Status = AccessOperationItemStatusEnum.DeadLetter;
                        item.NextAttemptAt = null;
                        item.FinishedAt = DateTime.UtcNow;
                        if (string.IsNullOrWhiteSpace(item.Error))
                            item.Error = "Limite de tentativas excedido.";
                    }

                    AccessOperationPolicy.RefreshCounts(batch);
                    var canceledItems = batch.Items.Count(x => x.Status == AccessOperationItemStatusEnum.Canceled);
                    batch.Status = batch.FailedItems > 0
                        ? batch.SuccessfulItems > 0 ? AccessBatchStatusEnum.CompletedWithErrors : AccessBatchStatusEnum.DeadLetter
                        : canceledItems > 0
                            ? AccessBatchStatusEnum.Canceled
                            : AccessBatchStatusEnum.Completed;
                    batch.NextAttemptAt = null;
                    batch.FinishedAt = DateTime.UtcNow;
                    AddBatchAudit(context, batch);
                }
            }
        }
        catch (Exception exception)
        {
            batch.Error = exception.Message.Length <= 1000 ? exception.Message : exception.Message[..1000];
            if (batch.AttemptCount >= batch.MaxAttempts)
            {
                foreach (var item in batch.Items.Where(x => AccessOperationPolicy.IsPending(x.Status)))
                {
                    item.Status = AccessOperationItemStatusEnum.DeadLetter;
                    item.NextAttemptAt = null;
                    item.FinishedAt = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(item.Error))
                        item.Error = batch.Error;
                }
                AccessOperationPolicy.RefreshCounts(batch);
                batch.Status = AccessBatchStatusEnum.DeadLetter;
                batch.FinishedAt = DateTime.UtcNow;
                AddBatchAudit(context, batch);
            }
            else
            {
                batch.Status = AccessBatchStatusEnum.Queued;
                batch.NextAttemptAt = DateTime.UtcNow.Add(AccessOperationPolicy.RetryDelay(batch.AttemptCount));
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

    private static void AddBatchAudit(DatabaseContext context, AccessBatchOperationDTO batch)
    {
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = batch.LicenseId,
            EntityType = "AccessBatch",
            EntityId = batch.Id,
            Action = "Completed",
            Status = batch.Status.ToString(),
            Summary = batch.Status switch
            {
                AccessBatchStatusEnum.Completed => $"Operação concluída com {batch.SuccessfulItems} item(ns) processado(s).",
                AccessBatchStatusEnum.CompletedWithErrors => $"Operação concluída com {batch.FailedItems} pendência(s).",
                AccessBatchStatusEnum.DeadLetter => "Operação interrompida após exceder o limite de tentativas.",
                AccessBatchStatusEnum.Canceled => "Operação cancelada antes da conclusão.",
                _ => "Operação finalizada."
            },
            DetailsJson = JsonSerializer.Serialize(new
            {
                batch.TotalItems,
                batch.ProcessedItems,
                batch.SuccessfulItems,
                batch.FailedItems,
                batch.AttemptCount,
                batch.MaxAttempts
            }),
            UserName = batch.RequestedBy,
            CreatedAt = DateTime.UtcNow
        });
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
            .Include(x => x.Resident).ThenInclude(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
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
