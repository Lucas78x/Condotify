using CondotifyAPI.Data.Mobile;
using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = SessionController.AnyPrincipalPolicy)]
[Route("api/mobile")]
public sealed class MobileNotificationsController : ControllerBase
{
    private static readonly MobileNotificationCategory[] Categories = Enum.GetValues<MobileNotificationCategory>();
    private readonly DatabaseContext _context;
    private readonly IPushNotificationService? _notifications;

    public MobileNotificationsController(DatabaseContext context, IPushNotificationService? notifications = null)
    {
        _context = context;
        _notifications = notifications;
    }

    [HttpPut("installations/{installationId}")]
    public async Task<IActionResult> UpsertInstallation(
        string installationId,
        [FromBody] MobileInstallationUpsertIn? input,
        CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();

        var error = ValidateInstallation(installationId, input);
        if (error is not null) return BadRequest(new { Errors = error });

        installationId = installationId.Trim();
        var token = input!.PushToken.Trim();
        var tokenHash = PushInstallationTokens.Hash(token);
        var now = DateTime.UtcNow;
        var installation = await _context.PushInstallations
            .FirstOrDefaultAsync(x => x.InstallationId == installationId, cancellationToken);
        var tokenOwner = await _context.PushInstallations
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.InstallationId != installationId, cancellationToken);

        if (installation is null && tokenOwner is not null)
        {
            installation = tokenOwner;
            installation.InstallationId = installationId;
        }
        else if (tokenOwner is not null)
        {
            PushInstallationTokens.Retire(tokenOwner, now);
        }

        if (installation is null)
        {
            installation = new PushInstallationDTO
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                CreatedAt = now
            };
            _context.PushInstallations.Add(installation);
        }

        ApplyRegistration(installation, subject.Value, input, token, tokenHash, now);
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(ToInstallation(installation));
    }

    [HttpDelete("installations/{installationId}")]
    public async Task<IActionResult> DeleteInstallation(string installationId, CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();

        var installation = await _context.PushInstallations.FirstOrDefaultAsync(x =>
            x.InstallationId == installationId &&
            x.SubjectId == subject.Value.Id &&
            x.SubjectType == subject.Value.SubjectType,
            cancellationToken);
        if (installation is null) return NoContent();

        PushInstallationTokens.Retire(installation, DateTime.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("notification-preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();

        var stored = await _context.PushPreferences.AsNoTracking().Where(x =>
            x.SubjectId == subject.Value.Id && x.SubjectType == subject.Value.SubjectType)
            .ToDictionaryAsync(x => x.Category, cancellationToken);
        return Ok(BuildPreferences(stored));
    }

    [HttpPut("notification-preferences")]
    public async Task<IActionResult> UpdatePreferences(
        [FromBody] MobileNotificationPreferencesUpdateIn? input,
        CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();
        if (input?.Preferences is null) return BadRequest(new { Errors = "Informe as preferências." });
        if (input.Preferences.GroupBy(x => x.Category).Any(x => x.Count() > 1))
            return BadRequest(new { Errors = "Cada categoria deve aparecer uma única vez." });
        if (input.Preferences.Any(x => !Categories.Contains(x.Category)))
            return BadRequest(new { Errors = "Categoria de notificação inválida." });

        var now = DateTime.UtcNow;
        var stored = await _context.PushPreferences.Where(x =>
            x.SubjectId == subject.Value.Id && x.SubjectType == subject.Value.SubjectType)
            .ToDictionaryAsync(x => x.Category, cancellationToken);

        foreach (var requested in input.Preferences)
        {
            var enabled = IsEssential(requested.Category) || requested.Enabled;
            if (stored.TryGetValue(requested.Category, out var preference))
            {
                preference.Enabled = enabled;
                preference.UpdatedAt = now;
            }
            else
            {
                preference = new PushPreferenceDTO
                {
                    Id = Guid.NewGuid(),
                    SubjectId = subject.Value.Id,
                    SubjectType = subject.Value.SubjectType,
                    Category = requested.Category,
                    Enabled = enabled,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                stored.Add(requested.Category, preference);
                _context.PushPreferences.Add(preference);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(BuildPreferences(stored));
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 30,
        CancellationToken cancellationToken = default)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.PushNotifications.AsNoTracking().Where(x =>
            x.SubjectId == subject.Value.Id && x.SubjectType == subject.Value.SubjectType);
        var total = await query.CountAsync(cancellationToken);
        var unread = await query.CountAsync(x => x.ReadAt == null, cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        return Ok(new MobileNotificationPageOut
        {
            Items = rows.Select(ToNotification).ToList(),
            Page = page,
            PageSize = pageSize,
            Total = total,
            Unread = unread
        });
    }

    [HttpPost("notifications/{notificationId:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid notificationId, CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();

        var notification = await _context.PushNotifications.FirstOrDefaultAsync(x =>
            x.Id == notificationId &&
            x.SubjectId == subject.Value.Id &&
            x.SubjectType == subject.Value.SubjectType,
            cancellationToken);
        if (notification is null) return NotFound();
        notification.ReadAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return Ok(ToNotification(notification));
    }

    [HttpPost("notifications/test")]
    public async Task<IActionResult> SendTestNotification(CancellationToken cancellationToken)
    {
        var subject = SessionController.ResolveSubject(User);
        if (subject is null) return Unauthorized();
        if (_notifications is null) return StatusCode(StatusCodes.Status503ServiceUnavailable);

        var notification = await _notifications.EnqueueAsync(new PushNotificationRequest(
            subject.Value.Id,
            subject.Value.SubjectType,
            MobileNotificationCategory.System,
            "Notificacoes ativadas",
            "Este dispositivo esta pronto para receber atualizacoes do Condotify.",
            "/home",
            $"mobile-test:{subject.Value.SubjectType}:{subject.Value.Id:N}:{Guid.NewGuid():N}"), cancellationToken);
        return Created(string.Empty, ToNotification(notification));
    }

    internal static string? ValidateInstallation(string installationId, MobileInstallationUpsertIn? input)
    {
        if (string.IsNullOrWhiteSpace(installationId) || installationId.Trim().Length > 200)
            return "Identificador da instalação inválido.";
        if (input is null || string.IsNullOrWhiteSpace(input.PushToken) || input.PushToken.Trim().Length is < 20 or > 3500)
            return "Token push inválido.";
        if (input.Platform is not MobilePlatform.Android and not MobilePlatform.Ios and not MobilePlatform.Windows)
            return "Plataforma inválida.";
        return null;
    }

    internal static bool IsEssential(MobileNotificationCategory category) =>
        category is MobileNotificationCategory.Security or MobileNotificationCategory.System;

    private static void ApplyRegistration(
        PushInstallationDTO installation,
        SessionController.AuthenticatedSubject subject,
        MobileInstallationUpsertIn input,
        string token,
        string tokenHash,
        DateTime now)
    {
        installation.SubjectId = subject.Id;
        installation.SubjectType = subject.SubjectType;
        installation.PushToken = token;
        installation.TokenHash = tokenHash;
        installation.Platform = input.Platform;
        installation.DeviceName = Short(input.DeviceName, 160);
        installation.AppVersion = Short(input.AppVersion, 40);
        installation.Locale = Short(input.Locale, 20);
        installation.TimeZone = Short(input.TimeZone, 100);
        installation.IsActive = true;
        installation.LastSeenAt = now;
        installation.UpdatedAt = now;
    }

    private static IReadOnlyCollection<MobileNotificationPreferenceOut> BuildPreferences(
        IReadOnlyDictionary<MobileNotificationCategory, PushPreferenceDTO> stored) =>
        Categories.Select(category => new MobileNotificationPreferenceOut
        {
            Category = category,
            IsEssential = IsEssential(category),
            Enabled = IsEssential(category) || !stored.TryGetValue(category, out var preference) || preference.Enabled
        }).ToList();

    private static MobileInstallationOut ToInstallation(PushInstallationDTO value) => new()
    {
        Id = value.Id,
        InstallationId = value.InstallationId,
        Platform = value.Platform,
        DeviceName = value.DeviceName,
        AppVersion = value.AppVersion,
        IsActive = value.IsActive,
        LastSeenAt = value.LastSeenAt
    };

    private static MobileNotificationOut ToNotification(PushNotificationDTO value) => new()
    {
        Id = value.Id,
        Category = value.Category,
        Title = value.Title,
        Body = value.Body,
        Route = value.Route,
        DeepLink = value.DeepLink,
        IsRead = value.ReadAt.HasValue,
        CreatedAt = value.CreatedAt,
        ReadAt = value.ReadAt
    };

    private static string Short(string? value, int max)
    {
        value = value?.Trim() ?? string.Empty;
        return value.Length <= max ? value : value[..max];
    }
}
