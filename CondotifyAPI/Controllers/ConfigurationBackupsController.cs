using System.Security.Claims;
using System.Text.Json;
using System.Data.Common;
using CondotifyAPI.Data.Backups;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Backups;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/backups")]
[RequireLicensePermission(LicensePermissionEnum.ViewBackups)]
public sealed class ConfigurationBackupsController(
    DatabaseContext context,
    IConfigurationBackupService backupService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId)
    {
        var items = await context.ConfigurationBackups.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.Version)
            .Select(x => new ConfigurationBackupOut
            {
                Id = x.Id,
                Version = x.Version,
                Name = x.Name,
                Description = x.Description,
                DeviceCount = x.DeviceCount,
                RouteCount = x.RouteCount,
                CredentialCount = x.CredentialCount,
                BindingCount = x.BindingCount,
                OverrideCount = x.OverrideCount,
                CreatedBy = x.CreatedBy,
                CreatedAt = x.CreatedAt,
                LastRestoredAt = x.LastRestoredAt,
                LastRestoredBy = x.LastRestoredBy
            })
            .ToListAsync(HttpContext.RequestAborted);
        return Ok(items);
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> Create(Guid licenseId, [FromBody] CreateConfigurationBackupIn input)
    {
        if (input.Name.Trim().Length > 120)
            return BadRequest(new { Result = "InvalidBackup", Errors = "O nome deve ter no maximo 120 caracteres." });
        if (input.Description.Trim().Length > 500)
            return BadRequest(new { Result = "InvalidBackup", Errors = "A descricao deve ter no maximo 500 caracteres." });

        try
        {
            var backup = await backupService.CreateAsync(
                licenseId,
                input.Name,
                input.Description,
                CurrentActor(),
                HttpContext.RequestAborted);
            return Created(string.Empty, ToOut(backup));
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                Result = "ConcurrentBackup",
                Errors = "Outra versao foi criada ao mesmo tempo. Recarregue o historico e tente novamente."
            });
        }
        catch (DbException)
        {
            return Conflict(new
            {
                Result = "ConcurrentBackup",
                Errors = "Outra versao foi criada ao mesmo tempo. Recarregue o historico e tente novamente."
            });
        }
    }

    [HttpPost("{backupId:guid}/preview")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> Preview(
        Guid licenseId,
        Guid backupId,
        [FromBody] PreviewConfigurationRestoreIn input)
    {
        try
        {
            var preview = await backupService.PreviewAsync(
                licenseId,
                backupId,
                input,
                HttpContext.RequestAborted);
            return preview is null ? NotFound() : Ok(preview);
        }
        catch (ConfigurationRestoreException exception)
        {
            return Conflict(new { Result = exception.Code, Errors = exception.Message });
        }
    }

    [HttpPost("{backupId:guid}/restore")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> Restore(
        Guid licenseId,
        Guid backupId,
        [FromBody] ExecuteConfigurationRestoreIn input)
    {
        try
        {
            var result = await backupService.RestoreAsync(
                licenseId,
                backupId,
                input,
                CurrentActor(),
                HttpContext.RequestAborted);
            return result is null ? NotFound() : Ok(result);
        }
        catch (ConfigurationRestoreException exception)
        {
            return Conflict(new { Result = exception.Code, Errors = exception.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                Result = "ConcurrentRestore",
                Errors = "Os dados mudaram durante a restauracao. Execute uma nova simulacao e tente novamente."
            });
        }
        catch (DbException)
        {
            return Conflict(new
            {
                Result = "ConcurrentRestore",
                Errors = "Os dados mudaram durante a restauracao. Execute uma nova simulacao e tente novamente."
            });
        }
    }

    [HttpDelete("{backupId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid backupId)
    {
        var backup = await context.ConfigurationBackups
            .FirstOrDefaultAsync(x => x.Id == backupId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (backup is null) return NotFound();
        context.ConfigurationBackups.Remove(backup);
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "ConfigurationBackup",
            EntityId = backup.Id,
            Action = "Purged",
            Status = "Success",
            Summary = $"Backup v{backup.Version} excluido definitivamente.",
            DetailsJson = JsonSerializer.Serialize(new { backup.Version, backup.Name }),
            UserName = CurrentActor(),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return NoContent();
    }

    private string CurrentActor() =>
        User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";

    private static ConfigurationBackupOut ToOut(CondotifyAPI.Domain.DTO.Backup.ConfigurationBackupDTO item) => new()
    {
        Id = item.Id,
        Version = item.Version,
        Name = item.Name,
        Description = item.Description,
        DeviceCount = item.DeviceCount,
        RouteCount = item.RouteCount,
        CredentialCount = item.CredentialCount,
        BindingCount = item.BindingCount,
        OverrideCount = item.OverrideCount,
        CreatedBy = item.CreatedBy,
        CreatedAt = item.CreatedAt,
        LastRestoredAt = item.LastRestoredAt,
        LastRestoredBy = item.LastRestoredBy
    };
}
