using System.Security.Claims;
using System.Text.Json;
using CondotifyAPI.Data.RecycleBin;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.RecycleBin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/recycle-bin")]
public sealed class RecycleBinController(
    DatabaseContext context,
    IRecycleBinService recycleBin,
    ILicenseAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId)
    {
        var canStructure = await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageStructure, HttpContext.RequestAborted);
        var canPeople = await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted);
        var canDevices = await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted);
        if (!canStructure && !canPeople && !canDevices) return Forbid();
        var allowedTypes = new List<string>();
        if (canStructure) allowedTypes.AddRange(["Block", "Unit"]);
        if (canPeople) allowedTypes.AddRange(["Resident", "Vehicle"]);
        if (canDevices) allowedTypes.Add("Device");
        var now = DateTime.UtcNow;
        var rows = await context.RecycleBinItems.AsNoTracking()
            .Where(x => x.LicenseId == licenseId && allowedTypes.Contains(x.EntityType) && !x.RestoredAt.HasValue && x.ExpiresAt > now)
            .OrderByDescending(x => x.DeletedAt)
            .ToListAsync();
        var items = rows.Select(x => new RecycleBinItemOut
            {
                Id = x.Id,
                EntityType = x.EntityType,
                EntityId = x.EntityId,
                DisplayName = x.DisplayName,
                DeletedBy = x.DeletedBy,
                DeletedAt = x.DeletedAt,
                ExpiresAt = x.ExpiresAt,
                DaysRemaining = (int)Math.Ceiling((x.ExpiresAt - now).TotalDays)
            }).ToList();
        return Ok(items);
    }

    [HttpPost("{itemId:guid}/restore")]
    public async Task<IActionResult> Restore(Guid licenseId, Guid itemId)
    {
        var item = await context.RecycleBinItems.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == itemId && x.LicenseId == licenseId);
        if (item is null) return NotFound();
        if (!await CanManageAsync(licenseId, item.EntityType)) return Forbid();

        RecycleRestoreResult result;
        try
        {
            result = await recycleBin.RestoreAsync(
                licenseId,
                itemId,
                CurrentActor(),
                HttpContext.RequestAborted);
        }
        catch (DbUpdateException)
        {
            return Conflict(new
            {
                Result = "RestoreConflict",
                Errors = "Os dados mudaram durante a restauracao. Recarregue a lixeira e tente novamente."
            });
        }

        if (!result.Found) return NotFound();
        if (!result.Success)
            return Conflict(new { Result = "RestoreConflict", Errors = result.Message });
        return Ok(new RecycleBinRestoreOut
        {
            ItemId = result.ItemId,
            EntityId = result.EntityId,
            EntityType = result.EntityType,
            Message = result.Message
        });
    }

    [HttpDelete("{itemId:guid}")]
    public async Task<IActionResult> Purge(Guid licenseId, Guid itemId)
    {
        var item = await context.RecycleBinItems
            .FirstOrDefaultAsync(x => x.Id == itemId && x.LicenseId == licenseId);
        if (item is null) return NotFound();
        if (!await CanManageAsync(licenseId, item.EntityType)) return Forbid();

        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = item.EntityType,
            EntityId = item.EntityId,
            Action = "Purged",
            Status = "Success",
            Summary = $"{item.DisplayName} excluido definitivamente da lixeira.",
            DetailsJson = JsonSerializer.Serialize(new { RecycleBinItemId = item.Id }),
            UserName = CurrentActor(),
            CreatedAt = DateTime.UtcNow
        });
        var result = await recycleBin.PurgeAsync(licenseId, itemId, HttpContext.RequestAborted);
        return result.Found ? NoContent() : NotFound();
    }

    private Task<bool> CanManageAsync(Guid licenseId, string entityType) =>
        authorization.HasPermissionAsync(User, licenseId, entityType switch
        {
            "Resident" or "Vehicle" => LicensePermissionEnum.ManagePeople,
            "Device" => LicensePermissionEnum.ManageDevices,
            _ => LicensePermissionEnum.ManageStructure
        }, HttpContext.RequestAborted);

    private string CurrentActor() =>
        User.FindFirstValue("name") ?? User.Identity?.Name ?? "Usuario do portal";
}
