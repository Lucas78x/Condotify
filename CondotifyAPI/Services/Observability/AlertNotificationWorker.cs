using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Observability;

public sealed class AlertNotificationWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<AlertNotificationWorker> logger) : BackgroundService
{
    private static readonly string WorkerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ProcessAsync(stoppingToken);
        var interval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("Notifications:DispatchIntervalSeconds", 30),
            15,
            600));
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ProcessAsync(stoppingToken);
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            await ScheduleAsync(context, cancellationToken);
            var delivery = await ClaimAsync(context, cancellationToken);
            if (delivery is null) return;
            var sender = scope.ServiceProvider.GetRequiredService<IAlertNotificationChannelSender>();
            await DispatchAsync(context, sender, delivery, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no processamento de notificações de alerta");
        }
    }

    private static async Task ScheduleAsync(DatabaseContext context, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await context.AlertNotificationDeliveries
            .Where(x => x.Alert.Status == OperationalAlertStatus.Resolved &&
                        (x.Status == NotificationDeliveryStatus.Queued ||
                         x.Status == NotificationDeliveryStatus.Failed))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, NotificationDeliveryStatus.Cancelled)
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.NextAttemptAt, (DateTime?)null), cancellationToken);

        var policies = await context.AlertNotificationPolicies.AsNoTracking()
            .Where(x => x.Enabled && (x.WebhookEnabled || x.EmailEnabled))
            .ToDictionaryAsync(x => x.LicenseId, cancellationToken);
        if (policies.Count == 0) return;

        var licenseIds = policies.Keys.ToList();
        var alerts = await context.OperationalAlerts
            .Include(x => x.License)
            .Where(x => x.LicenseId != null &&
                        licenseIds.Contains(x.LicenseId.Value) &&
                        x.Status != OperationalAlertStatus.Resolved &&
                        (x.SuppressedUntil == null || x.SuppressedUntil <= now))
            .ToListAsync(cancellationToken);
        if (alerts.Count == 0) return;

        var alertIds = alerts.Select(x => x.Id).ToList();
        var existingKeys = await context.AlertNotificationDeliveries.AsNoTracking()
            .Where(x => alertIds.Contains(x.AlertId))
            .Select(x => x.DeliveryKey)
            .ToHashSetAsync(cancellationToken);

        foreach (var alert in alerts)
        {
            var policy = policies[alert.LicenseId!.Value];
            if (alert.Severity < policy.MinimumSeverity) continue;
            var level = EscalationLevel(alert, policy, now);
            var nextEscalationAt = NextEscalationAt(alert, policy, level);
            if (alert.NextEscalationAt != nextEscalationAt)
            {
                alert.NextEscalationAt = nextEscalationAt;
                alert.UpdatedAt = now;
            }
            AddDelivery(context, existingKeys, alert, policy, NotificationChannel.Webhook, level, policy.WebhookEnabled && !string.IsNullOrWhiteSpace(policy.WebhookUrl));
            AddDelivery(context, existingKeys, alert, policy, NotificationChannel.Email, level, policy.EmailEnabled && !string.IsNullOrWhiteSpace(policy.EmailRecipients));
        }

        try
        {
            if (context.ChangeTracker.HasChanges())
                await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
        }
    }

    private static void AddDelivery(
        DatabaseContext context,
        HashSet<string> existingKeys,
        OperationalAlertDTO alert,
        AlertNotificationPolicyDTO policy,
        NotificationChannel channel,
        int level,
        bool enabled)
    {
        if (!enabled) return;
        var key = $"{alert.Id:N}:{channel}:{level}";
        if (!existingKeys.Add(key)) return;
        context.AlertNotificationDeliveries.Add(new AlertNotificationDeliveryDTO
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            LicenseId = alert.LicenseId!.Value,
            Channel = channel,
            Status = NotificationDeliveryStatus.Queued,
            EscalationLevel = level,
            DeliveryKey = key,
            DestinationLabel = channel == NotificationChannel.Webhook
                ? EndpointLabel(policy.WebhookUrl)
                : RecipientLabel(policy.EmailRecipients),
            MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static async Task<AlertNotificationDeliveryDTO?> ClaimAsync(
        DatabaseContext context,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await context.AlertNotificationDeliveries
            .Where(x => x.AttemptCount >= x.MaxAttempts &&
                        (x.Status == NotificationDeliveryStatus.Queued ||
                         x.Status == NotificationDeliveryStatus.Failed ||
                         (x.Status == NotificationDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, NotificationDeliveryStatus.DeadLetter)
                .SetProperty(x => x.FinishedAt, now)
                .SetProperty(x => x.LeaseOwner, string.Empty)
                .SetProperty(x => x.LeaseExpiresAt, (DateTime?)null), cancellationToken);

        var candidates = await context.AlertNotificationDeliveries.AsNoTracking()
            .Where(x => x.AttemptCount < x.MaxAttempts &&
                        (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                        (x.Status == NotificationDeliveryStatus.Queued ||
                         x.Status == NotificationDeliveryStatus.Failed ||
                         (x.Status == NotificationDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
            .OrderBy(x => x.CreatedAt)
            .Select(x => x.Id)
            .Take(8)
            .ToListAsync(cancellationToken);
        foreach (var candidate in candidates)
        {
            var claimed = await context.AlertNotificationDeliveries
                .Where(x => x.Id == candidate &&
                            x.AttemptCount < x.MaxAttempts &&
                            (x.NextAttemptAt == null || x.NextAttemptAt <= now) &&
                            (x.Status == NotificationDeliveryStatus.Queued ||
                             x.Status == NotificationDeliveryStatus.Failed ||
                             (x.Status == NotificationDeliveryStatus.Sending && x.LeaseExpiresAt <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, NotificationDeliveryStatus.Sending)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.LeaseOwner, WorkerId)
                    .SetProperty(x => x.LeaseExpiresAt, now.Add(LeaseDuration))
                    .SetProperty(x => x.NextAttemptAt, (DateTime?)null)
                    .SetProperty(x => x.SentAt, now), cancellationToken);
            if (claimed == 0) continue;
            context.ChangeTracker.Clear();
            return await context.AlertNotificationDeliveries
                .Include(x => x.Alert).ThenInclude(x => x.License)
                .FirstAsync(x => x.Id == candidate, cancellationToken);
        }
        return null;
    }

    private static async Task DispatchAsync(
        DatabaseContext context,
        IAlertNotificationChannelSender sender,
        AlertNotificationDeliveryDTO delivery,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (delivery.Alert.Status == OperationalAlertStatus.Resolved)
        {
            delivery.Status = NotificationDeliveryStatus.Cancelled;
            delivery.FinishedAt = now;
            delivery.LeaseOwner = string.Empty;
            delivery.LeaseExpiresAt = null;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }
        if (delivery.Alert.SuppressedUntil > now)
        {
            delivery.Status = NotificationDeliveryStatus.Failed;
            delivery.NextAttemptAt = delivery.Alert.SuppressedUntil;
            delivery.LastError = "Entrega adiada enquanto o alerta está silenciado.";
            delivery.LeaseOwner = string.Empty;
            delivery.LeaseExpiresAt = null;
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var policy = await context.AlertNotificationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == delivery.LicenseId, cancellationToken);
        var channelEnabled = policy is not null &&
            ((delivery.Channel == NotificationChannel.Webhook &&
              policy.WebhookEnabled &&
              !string.IsNullOrWhiteSpace(policy.WebhookUrl)) ||
             (delivery.Channel == NotificationChannel.Email &&
              policy.EmailEnabled &&
              !string.IsNullOrWhiteSpace(policy.EmailRecipients)));
        if (policy is null ||
            !policy.Enabled ||
            !channelEnabled ||
            delivery.Alert.Severity < policy.MinimumSeverity)
        {
            delivery.Status = NotificationDeliveryStatus.Cancelled;
            delivery.FinishedAt = now;
        }
        else
        {
            var alert = delivery.Alert;
            var result = await sender.SendAsync(delivery.Channel, policy, new AlertNotificationMessage(
                alert.Id,
                delivery.LicenseId,
                alert.License?.Name ?? string.Empty,
                alert.Type,
                alert.Severity.ToString(),
                alert.Title,
                alert.Message,
                alert.Status.ToString(),
                delivery.EscalationLevel,
                alert.LastOccurredAt,
                alert.TargetUrl), cancellationToken);
            delivery.ResponseCode = result.ResponseCode;
            delivery.LastError = Short(result.Error, 1000);
            if (result.Success)
            {
                delivery.Status = NotificationDeliveryStatus.Delivered;
                delivery.FinishedAt = now;
                alert.LastNotifiedAt = now;
                alert.EscalationLevel = Math.Max(alert.EscalationLevel, delivery.EscalationLevel);
                alert.UpdatedAt = now;
            }
            else if (delivery.AttemptCount >= delivery.MaxAttempts)
            {
                delivery.Status = NotificationDeliveryStatus.DeadLetter;
                delivery.FinishedAt = now;
            }
            else
            {
                delivery.Status = NotificationDeliveryStatus.Failed;
                delivery.NextAttemptAt = now.AddMinutes(Math.Min(60, Math.Pow(2, delivery.AttemptCount)));
            }
        }
        delivery.LeaseOwner = string.Empty;
        delivery.LeaseExpiresAt = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    internal static int EscalationLevel(
        OperationalAlertDTO alert,
        AlertNotificationPolicyDTO policy,
        DateTime now)
    {
        var sla = alert.Severity == OperationalAlertSeverity.Critical
            ? policy.CriticalSlaMinutes
            : policy.WarningSlaMinutes;
        var elapsedMinutes = Math.Max(0, (now - alert.FirstOccurredAt).TotalMinutes);
        if (elapsedMinutes < sla) return 0;
        return Math.Min(99, 1 + (int)((elapsedMinutes - sla) / Math.Max(15, policy.EscalationRepeatMinutes)));
    }

    private static DateTime NextEscalationAt(
        OperationalAlertDTO alert,
        AlertNotificationPolicyDTO policy,
        int level)
    {
        var sla = alert.Severity == OperationalAlertSeverity.Critical
            ? policy.CriticalSlaMinutes
            : policy.WarningSlaMinutes;
        return level == 0
            ? alert.FirstOccurredAt.AddMinutes(sla)
            : alert.FirstOccurredAt.AddMinutes(sla + level * Math.Max(15, policy.EscalationRepeatMinutes));
    }

    internal static string EndpointLabel(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Host}{(uri.IsDefaultPort ? string.Empty : $":{uri.Port}")}{(uri.AbsolutePath == "/" ? string.Empty : "/•••")}"
            : "Webhook configurado";

    internal static string RecipientLabel(string value)
    {
        var count = AlertNotificationChannelSender.ParseRecipients(value).Count;
        return count == 1 ? "1 destinatário" : $"{count} destinatários";
    }

    private static string Short(string value, int max) => value.Length <= max ? value : value[..max];
}
