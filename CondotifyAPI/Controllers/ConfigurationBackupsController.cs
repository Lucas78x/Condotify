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
    IConfigurationBackupService backupService,
    IConfigurationBackupArchiveService archiveService,
    IAutomaticBackupRunner automaticRunner) : ControllerBase
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

    [HttpGet("automation")]
    public async Task<IActionResult> GetAutomation(Guid licenseId)
    {
        var policy = await context.BackupAutomationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        return Ok(ToPolicyOut(policy, archiveService));
    }

    [HttpPut("automation")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> UpdateAutomation(
        Guid licenseId,
        [FromBody] UpdateBackupAutomationPolicyIn input)
    {
        if (input.IntervalHours is < 6 or > 168)
            return BadRequest(new { Result = "InvalidSchedule", Errors = "O intervalo deve ficar entre 6 horas e 7 dias." });
        if (input.ExternalRetentionDays is < 7 or > 3650)
            return BadRequest(new { Result = "InvalidRetention", Errors = "A retencao externa deve ficar entre 7 dias e 10 anos." });
        if (input.Enabled && input.ExportEnabled && !archiveService.ExternalStorageReady)
            return BadRequest(new
            {
                Result = "ExternalStorageUnavailable",
                Errors = "Configure CONDOTIFY_BACKUP_EXPORT_PATH antes de habilitar a copia externa."
            });

        var now = DateTime.UtcNow;
        var policy = await context.BackupAutomationPolicies
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (policy is null)
        {
            policy = new CondotifyAPI.Domain.DTO.Backup.BackupAutomationPolicyDTO { LicenseId = licenseId };
            context.BackupAutomationPolicies.Add(policy);
        }
        policy.Enabled = input.Enabled;
        policy.IntervalHours = input.IntervalHours;
        policy.ExportEnabled = input.ExportEnabled;
        policy.ExternalRetentionDays = input.ExternalRetentionDays;
        policy.NextRunAt = input.Enabled ? now.AddHours(input.IntervalHours) : null;
        policy.LastError = input.Enabled && input.ExportEnabled && !archiveService.ExternalStorageReady
            ? "Destino externo nao configurado."
            : string.Empty;
        policy.LeaseOwner = string.Empty;
        policy.LeaseExpiresAt = null;
        policy.UpdatedAt = now;
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "BackupAutomation",
            Action = "Updated",
            Status = "Success",
            Summary = input.Enabled
                ? $"Backups automaticos habilitados a cada {input.IntervalHours} hora(s)."
                : "Backups automaticos desabilitados.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                input.Enabled,
                input.IntervalHours,
                input.ExportEnabled,
                input.ExternalRetentionDays,
                policy.NextRunAt
            }),
            UserName = CurrentActor(),
            CreatedAt = now
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToPolicyOut(policy, archiveService));
    }

    [HttpPost("run")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> RunNow(Guid licenseId)
    {
        var policy = await context.BackupAutomationPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
        var exportEnabled = policy?.ExportEnabled == true;
        if (exportEnabled && !archiveService.ExternalStorageReady)
            return Conflict(new
            {
                Result = "ExternalStorageUnavailable",
                Errors = "O snapshot interno nao foi iniciado porque o destino externo configurado nao esta disponivel."
            });
        var result = await automaticRunner.RunAsync(
            licenseId,
            CurrentActor(),
            exportEnabled,
            policy?.ExternalRetentionDays ?? 90,
            HttpContext.RequestAborted);
        if (policy is not null)
        {
            policy = await context.BackupAutomationPolicies
                .FirstAsync(x => x.LicenseId == licenseId, HttpContext.RequestAborted);
            policy.LastRunAt = DateTime.UtcNow;
            policy.LastSuccessAt = !exportEnabled || result.Exported ? DateTime.UtcNow : policy.LastSuccessAt;
            policy.LastError = result.ExportError;
            policy.NextRunAt = policy.Enabled
                ? DateTime.UtcNow.AddHours(policy.IntervalHours)
                : null;
            policy.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync(HttpContext.RequestAborted);
        }
        return Ok(new BackupRunOut
        {
            Backup = ToOut(result.Backup),
            Exported = result.Exported,
            ExportFileName = result.ExportFileName,
            Message = result.Exported
                ? "Snapshot criado e copiado para o destino externo."
                : "Snapshot interno criado com sucesso."
        });
    }

    [HttpPost("{backupId:guid}/archive")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> BuildArchive(Guid licenseId, Guid backupId)
    {
        var backup = await context.ConfigurationBackups.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == backupId && x.LicenseId == licenseId,
                HttpContext.RequestAborted);
        if (backup is null) return NotFound();
        try
        {
            var archive = await archiveService.BuildAsync(backup, HttpContext.RequestAborted);
            var contentBase64 = Convert.ToBase64String(archive.Content);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(archive.Content);
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(),
                LicenseId = licenseId,
                EntityType = "ConfigurationBackup",
                EntityId = backup.Id,
                Action = "ArchiveGenerated",
                Status = "Success",
                Summary = $"Arquivo portatil do backup v{backup.Version} gerado.",
                DetailsJson = JsonSerializer.Serialize(new { archive.FileName, archive.Sha256 }),
                UserName = CurrentActor(),
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync(HttpContext.RequestAborted);
            return Ok(new BackupArchiveOut
            {
                FileName = archive.FileName,
                ContentBase64 = contentBase64,
                Sha256 = archive.Sha256
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { Result = "ArchiveUnavailable", Errors = exception.Message });
        }
    }

    [HttpPost("import")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBackups)]
    public async Task<IActionResult> ImportArchive(
        Guid licenseId,
        [FromBody] ImportBackupArchiveIn input)
    {
        if (!input.FileName.EndsWith(".cnbak", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidArchive", Errors = "Selecione um arquivo .cnbak." });
        if (string.IsNullOrWhiteSpace(input.ContentBase64) || input.ContentBase64.Length > 21_000_000)
            return BadRequest(new { Result = "InvalidArchive", Errors = "O arquivo esta vazio ou excede o limite permitido." });
        byte[]? content = null;
        try
        {
            content = Convert.FromBase64String(input.ContentBase64);
            var imported = await archiveService.ReadAsync(
                licenseId,
                content,
                HttpContext.RequestAborted);
            var backup = await backupService.ImportAsync(
                licenseId,
                imported,
                CurrentActor(),
                HttpContext.RequestAborted);
            return Created(string.Empty, ToOut(backup));
        }
        catch (FormatException)
        {
            return BadRequest(new { Result = "InvalidArchive", Errors = "O conteudo enviado nao esta em Base64 valido." });
        }
        catch (InvalidDataException exception)
        {
            return Conflict(new { Result = "InvalidArchive", Errors = exception.Message });
        }
        catch (ConfigurationRestoreException exception)
        {
            return Conflict(new { Result = exception.Code, Errors = exception.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                Result = "ConcurrentBackup",
                Errors = "Outra versao foi criada ao mesmo tempo. Recarregue o historico e tente novamente."
            });
        }
        finally
        {
            if (content is not null)
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(content);
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

    private static BackupAutomationPolicyOut ToPolicyOut(
        CondotifyAPI.Domain.DTO.Backup.BackupAutomationPolicyDTO? item,
        IConfigurationBackupArchiveService archive) => new()
    {
        Enabled = item?.Enabled ?? false,
        IntervalHours = item?.IntervalHours ?? 24,
        ExportEnabled = item?.ExportEnabled ?? true,
        ExternalRetentionDays = item?.ExternalRetentionDays ?? 90,
        LastRunAt = item?.LastRunAt,
        LastSuccessAt = item?.LastSuccessAt,
        NextRunAt = item?.NextRunAt,
        LastError = item?.LastError ?? string.Empty,
        ExternalStorageReady = archive.ExternalStorageReady,
        ExternalStorageLabel = archive.ExternalStorageLabel
    };
}
