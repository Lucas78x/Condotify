using System.Text.Json;
using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Mobile;

public sealed class PushNotificationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<PushNotificationWorker> logger) : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessAsync(stoppingToken);
        var seconds = Math.Clamp(configuration.GetValue("Push:DispatchIntervalSeconds", 20), 5, 300);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessAsync(stoppingToken);
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
            var transport = scope.ServiceProvider.GetRequiredService<IPushTransport>();
            await DeactivateStaleAsync(context, cancellationToken);
            await FanoutAsync(context, cancellationToken);
            if (!transport.IsConfigured) return;

            for (var index = 0; index < 20; index++)
            {
                var delivery = await ClaimAsync(context, cancellationToken);
                if (delivery is null) break;
                await DispatchAsync(context, transport, delivery, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no processamento do outbox push");
        }
    }

    private static async Task DeactivateStaleAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var staleBefore = DateTime.UtcNow.AddDays(-60);
        await context.PushInstallations
            .Where(x => x.IsActive && x.LastSeenAt < staleBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsActive, false)
                .SetProperty(x => x.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    private static async Task FanoutAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var notifications = await context.PushNotifications
            .Where(x => x.FanoutCompletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            var preference = await context.PushPreferences.AsNoTracking().FirstOrDefaultAsync(x =>
                x.SubjectId == notification.SubjectId &&
                x.SubjectType == notification.SubjectType &&
                x.Category == notification.Category,
                cancellationToken);
            if (!PreferenceAllows(notification.Category, preference?.Enabled))
            {
                notification.FanoutCompletedAt = DateTime.UtcNow;
                continue;
            }

            var installations = await context.PushInstallations.AsNoTracking().Where(x =>
                x.SubjectId == notification.SubjectId &&
                x.SubjectType == notification.SubjectType &&
                x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            var existing = await context.PushDeliveries.AsNoTracking()
                .Where(x => x.NotificationId == notification.Id)
                .Select(x => x.InstallationId)
                .ToHashSetAsync(cancellationToken);
            foreach (var installationId in installations.Where(existing.Add))
            {
                context.PushDeliveries.Add(new PushDeliveryDTO
                {
                    Id = Guid.NewGuid(),
                    NotificationId = notification.Id,
                    InstallationId = installationId,
                    Status = PushDeliveryStatus.Queued,
                    MaxAttempts = 5,
                    CreatedAt = DateTime.UtcNow
                });
            }
            notification.FanoutCompletedAt = DateTime.UtcNow;
        }

        if (!context.ChangeTracker.HasChanges()) return;
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { context.ChangeTracker.Clear(); }
    }

    private static async Task<PushDeliveryDTO?> ClaimAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await context.PushDeliveries.Where(x =>
                x.AttemptCount >= x.MaxAttempts &&
                (x.Status == PushDeliveryStatus.Queued || x.Status == PushDeliveryStatus.Failed ||
                 (x.Status == PushDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, PushDeliveryStatus.DeadLetter)
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.LeaseOwner, string.Empty)
                .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), cancellationToken);

        var candidates = await context.PushDeliveries.AsNoTracking().Where(x =>
                x.AttemptCount < x.MaxAttempts &&
                (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                (x.Status == PushDeliveryStatus.Queued || x.Status == PushDeliveryStatus.Failed ||
                 (x.Status == PushDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(8).ToListAsync(cancellationToken);

        foreach (var id in candidates)
        {
            var claimed = await context.PushDeliveries.Where(x =>
                    x.Id == id && x.AttemptCount < x.MaxAttempts &&
                    (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                    (x.Status == PushDeliveryStatus.Queued || x.Status == PushDeliveryStatus.Failed ||
                     (x.Status == PushDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, PushDeliveryStatus.Sending)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LeaseOwner, WorkerId)
                    .SetProperty(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.SentAt, now), cancellationToken);
            if (claimed == 0) continue;
            context.ChangeTracker.Clear();
            return await context.PushDeliveries
                .Include(x => x.Notification)
                .Include(x => x.Installation)
                .FirstAsync(x => x.Id == id, cancellationToken);
        }
        return null;
    }

    private static async Task DispatchAsync(
        DatabaseContext context,
        IPushTransport transport,
        PushDeliveryDTO delivery,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (!delivery.Installation.IsActive)
        {
            delivery.Status = PushDeliveryStatus.Cancelled;
            delivery.FinishedAt = now;
        }
        else
        {
            var data = ReadData(delivery.Notification.DataJson);
            data["notificationId"] = delivery.NotificationId.ToString("D");
            var result = await transport.SendAsync(
                delivery.Installation.PushToken,
                new PushTransportMessage(
                    delivery.Notification.Title,
                    delivery.Notification.Body,
                    delivery.Notification.Route,
                    delivery.Notification.DeepLink,
                    delivery.Notification.Category.ToString(),
                    data),
                cancellationToken);
            ApplyResult(delivery, delivery.Installation, result, now);
        }

        delivery.LeaseOwner = string.Empty;
        delivery.LeaseExpiresAt = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static bool PreferenceAllows(MobileNotificationCategory category, bool? enabled) =>
        category is MobileNotificationCategory.Security or MobileNotificationCategory.System || enabled is not false;

    internal static void ApplyResult(
        PushDeliveryDTO delivery,
        PushInstallationDTO installation,
        PushTransportResult result,
        DateTime now)
    {
        delivery.ResponseCode = result.ResponseCode;
        delivery.ProviderMessageId = Short(result.ProviderMessageId, 300);
        delivery.LastError = Short(result.Error, 1000);
        switch (result.Outcome)
        {
            case PushTransportOutcome.Delivered:
                delivery.Status = PushDeliveryStatus.Delivered;
                delivery.FinishedAt = now;
                break;
            case PushTransportOutcome.InvalidToken:
                delivery.Status = PushDeliveryStatus.Cancelled;
                delivery.FinishedAt = now;
                PushInstallationTokens.Retire(installation, now);
                break;
            case PushTransportOutcome.TransientFailure:
            case PushTransportOutcome.Unavailable:
                if (delivery.AttemptCount >= delivery.MaxAttempts)
                {
                    delivery.Status = PushDeliveryStatus.DeadLetter;
                    delivery.FinishedAt = now;
                }
                else
                {
                    delivery.Status = PushDeliveryStatus.Failed;
                    delivery.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, delivery.AttemptCount)));
                }
                break;
            default:
                delivery.Status = PushDeliveryStatus.DeadLetter;
                delivery.FinishedAt = now;
                break;
        }
    }

    private static Dictionary<string, string> ReadData(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
