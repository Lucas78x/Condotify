using System.Security.Claims;
using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/announcements")]
public sealed class AnnouncementsController(DatabaseContext context, IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> List(Guid licenseId, CancellationToken cancellationToken)
    {
        var results = await ListAnnouncementsCore(context, licenseId, cancellationToken);
        return Ok(results);
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Create(Guid licenseId, [FromBody] CreateAnnouncementIn input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160)
            return BadRequest(new { Errors = "Informe um titulo valido (ate 160 caracteres)." });
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 4000)
            return BadRequest(new { Errors = "Informe o texto do comunicado (ate 4000 caracteres)." });

        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var announcement = CreateAnnouncementCore(licenseId, input, actorName);
        context.Announcements.Add(announcement);
        await context.SaveChangesAsync(cancellationToken);

        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var residentId in ResourceDocumentsController.ResolveLicenseNotificationTargets(links, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                MobileNotificationCategory.Announcement,
                announcement.IsUrgent ? $"Comunicado urgente: {announcement.Title}" : $"Novo comunicado: {announcement.Title}",
                Truncate(announcement.Body, 140),
                "/comunicados",
                $"announcement-published:{announcement.Id:N}",
                cancellationToken);
        }

        return Created(string.Empty, ToOut(announcement));
    }

    [HttpPut("{announcementId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Update(Guid licenseId, Guid announcementId, [FromBody] UpdateAnnouncementIn input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160)
            return BadRequest(new { Errors = "Informe um titulo valido (ate 160 caracteres)." });
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 4000)
            return BadRequest(new { Errors = "Informe o texto do comunicado (ate 4000 caracteres)." });

        var announcement = await context.Announcements.FirstOrDefaultAsync(x => x.Id == announcementId && x.LicenseId == licenseId, cancellationToken);
        if (announcement is null) return NotFound();

        announcement.Title = input.Title.Trim();
        announcement.Body = input.Body.Trim();
        announcement.IsUrgent = input.IsUrgent;
        announcement.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return Ok(ToOut(announcement));
    }

    [HttpDelete("{announcementId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid announcementId, CancellationToken cancellationToken)
    {
        var announcement = await context.Announcements.FirstOrDefaultAsync(x => x.Id == announcementId && x.LicenseId == licenseId, cancellationToken);
        if (announcement is null) return NotFound();

        context.Announcements.Remove(announcement);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Deleted" });
    }

    internal static AnnouncementDTO CreateAnnouncementCore(Guid licenseId, CreateAnnouncementIn input, string actorName)
    {
        var now = DateTime.UtcNow;
        return new AnnouncementDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Title = input.Title.Trim(),
            Body = input.Body.Trim(),
            IsUrgent = input.IsUrgent,
            CreatedBy = actorName,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal static async Task<List<AnnouncementOut>> ListAnnouncementsCore(DatabaseContext context, Guid licenseId, CancellationToken cancellationToken = default) =>
        await context.Announcements.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToOut(x))
            .ToListAsync(cancellationToken);

    private static AnnouncementOut ToOut(AnnouncementDTO x) => new()
    {
        Id = x.Id, Title = x.Title, Body = x.Body, IsUrgent = x.IsUrgent,
        CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt
    };

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
