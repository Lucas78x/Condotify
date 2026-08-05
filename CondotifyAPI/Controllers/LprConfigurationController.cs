using System.Security.Claims;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/devices/{deviceId:guid}/lpr")]
public sealed class LprConfigurationController(DatabaseContext context) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
    public async Task<IActionResult> Get(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await context.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.LicenseId == licenseId);
        if (device == null) return NotFound();

        return Ok(ToOut(device));
    }

    [HttpPut]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> Configure(Guid licenseId, Guid deviceId, [FromBody] LprConfigurationIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.LicenseId == licenseId);
        if (device == null) return NotFound();

        if (input.LprMode.HasValue)
        {
            if (!input.LprCameraId.HasValue)
                return BadRequest(new { Result = "MissingCamera", Errors = "Selecione a camera que filma esta cancela antes de ativar o LPR." });

            var cameraBelongsToLicense = await context.CFTVDevices.AsNoTracking()
                .AnyAsync(c => c.Id == input.LprCameraId && c.LicenseId == licenseId);
            if (!cameraBelongsToLicense)
                return BadRequest(new { Result = "CameraNotFound", Errors = "Camera nao encontrada nesta licenca." });
        }

        device.LprCameraId = input.LprMode.HasValue ? input.LprCameraId : null;
        device.LprCameraChannel = input.LprMode.HasValue ? input.LprCameraChannel : null;
        device.LprMode = input.LprMode;
        device.LastUpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return Ok(ToOut(device));
    }

    private static LprConfigurationOut ToOut(AccessControlDeviceDTO device) => new()
    {
        LprCameraId = device.LprCameraId,
        LprCameraChannel = device.LprCameraChannel,
        LprMode = device.LprMode
    };

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
