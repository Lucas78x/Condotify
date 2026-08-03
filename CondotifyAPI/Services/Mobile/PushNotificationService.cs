using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using Microsoft.EntityFrameworkCore;
using MobileNotificationCategory = CondotifyAPI.Domain.Enums.Mobile.MobileNotificationCategory;

namespace CondotifyAPI.Services.Mobile;

public sealed record PushNotificationRequest(
    Guid SubjectId,
    string SubjectType,
    MobileNotificationCategory Category,
    string Title,
    string Body,
    string Route,
    string DeduplicationKey,
    IReadOnlyDictionary<string, string>? Data = null);

public interface IPushNotificationService
{
    Task<PushNotificationDTO> EnqueueAsync(
        PushNotificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PushNotificationService(DatabaseContext context) : IPushNotificationService
{
    public async Task<PushNotificationDTO> EnqueueAsync(
        PushNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        var key = request.DeduplicationKey.Trim();
        var existing = await context.PushNotifications.FirstOrDefaultAsync(x =>
            x.SubjectId == request.SubjectId &&
            x.SubjectType == request.SubjectType &&
            x.DeduplicationKey == key,
            cancellationToken);
        if (existing is not null) return existing;

        MobileDeepLinks.TryNormalize(request.Route, out var route);
        var notification = new PushNotificationDTO
        {
            Id = Guid.NewGuid(),
            SubjectId = request.SubjectId,
            SubjectType = request.SubjectType,
            Category = request.Category,
            Title = Short(request.Title, 160),
            Body = Short(request.Body, 500),
            Route = route,
            DeepLink = MobileDeepLinks.ToCanonicalUrl(route),
            DataJson = JsonSerializer.Serialize(request.Data ?? new Dictionary<string, string>()),
            DeduplicationKey = key,
            CreatedAt = DateTime.UtcNow
        };
        context.PushNotifications.Add(notification);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return notification;
        }
        catch (DbUpdateException)
        {
            context.Entry(notification).State = EntityState.Detached;
            return await context.PushNotifications.FirstAsync(x =>
                x.SubjectId == request.SubjectId &&
                x.SubjectType == request.SubjectType &&
                x.DeduplicationKey == key,
                cancellationToken);
        }
    }

    internal static void Validate(PushNotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SubjectId == Guid.Empty) throw new ArgumentException("Destinatário inválido.", nameof(request));
        if (request.SubjectType is not PrincipalTypes.User and not PrincipalTypes.Resident)
            throw new ArgumentException("Tipo de destinatário inválido.", nameof(request));
        if (!Enum.IsDefined(request.Category)) throw new ArgumentException("Categoria inválida.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
            throw new ArgumentException("Título e mensagem são obrigatórios.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DeduplicationKey) || request.DeduplicationKey.Trim().Length > 200)
            throw new ArgumentException("Chave de deduplicação inválida.", nameof(request));
        if (!MobileDeepLinks.TryNormalize(request.Route, out _))
            throw new ArgumentException("Rota mobile inválida.", nameof(request));
    }

    private static string Short(string value, int max)
    {
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}

public static class PushInstallationTokens
{
    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static void Retire(PushInstallationDTO installation, DateTime now)
    {
        installation.IsActive = false;
        installation.PushToken = string.Empty;
        installation.TokenHash = Hash($"retired:{installation.Id:N}:{now.Ticks}");
        installation.UpdatedAt = now;
    }
}
