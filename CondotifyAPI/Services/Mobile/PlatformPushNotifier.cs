using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Mobile;

public interface IPlatformPushNotifier
{
    Task NotifyResidentAsync(
        Guid residentId,
        MobileNotificationCategory category,
        string title,
        string body,
        string route,
        string deduplicationKey,
        CancellationToken cancellationToken = default);

    Task NotifyLicenseUsersAsync(
        Guid licenseId,
        MobileNotificationCategory category,
        string title,
        string body,
        string route,
        string deduplicationKey,
        CancellationToken cancellationToken = default);
}

public sealed class PlatformPushNotifier(
    DatabaseContext context,
    IPushNotificationService notifications,
    ILogger<PlatformPushNotifier> logger) : IPlatformPushNotifier
{
    public Task NotifyResidentAsync(
        Guid residentId,
        MobileNotificationCategory category,
        string title,
        string body,
        string route,
        string deduplicationKey,
        CancellationToken cancellationToken = default) =>
        EnqueueSafelyAsync(
            residentId,
            PrincipalTypes.Resident,
            category,
            title,
            body,
            route,
            deduplicationKey,
            cancellationToken);

    public async Task NotifyLicenseUsersAsync(
        Guid licenseId,
        MobileNotificationCategory category,
        string title,
        string body,
        string route,
        string deduplicationKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userIds = await context.LicenseUserAccesses.AsNoTracking()
                .Where(x => x.LicenseId == licenseId && x.IsActive)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var userId in userIds)
                await EnqueueSafelyAsync(
                    userId,
                    PrincipalTypes.User,
                    category,
                    title,
                    body,
                    route,
                    $"{deduplicationKey}:{userId:N}",
                    cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Nao foi possivel selecionar destinatarios push da licenca {LicenseId}", licenseId);
        }
    }

    private async Task EnqueueSafelyAsync(
        Guid subjectId,
        string subjectType,
        MobileNotificationCategory category,
        string title,
        string body,
        string route,
        string deduplicationKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await notifications.EnqueueAsync(new PushNotificationRequest(
                subjectId,
                subjectType,
                category,
                title,
                body,
                route,
                deduplicationKey), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Evento push {DeduplicationKey} nao foi enfileirado para {SubjectType} {SubjectId}",
                deduplicationKey,
                subjectType,
                subjectId);
        }
    }
}
