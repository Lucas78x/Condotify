using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/media")]
public sealed class PrivateMediaController(DatabaseContext context, IPrivateMediaStore media, ILicenseAuthorizationService authorization, ILicenseModuleService modules) : ControllerBase
{
    [HttpGet("{mediaId:guid}")]
    public async Task<IActionResult> Get(Guid licenseId, Guid mediaId, CancellationToken cancellationToken)
    {
        var reference = PrivateMediaStore.Reference(licenseId, mediaId);
        var incidentLinked = await context.IncidentAttachments.AsNoTracking().AnyAsync(x => x.LicenseId == licenseId && x.MediaReference == reference, cancellationToken);
        if (incidentLinked)
        {
            if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ViewIncidents, cancellationToken)) return Forbid();
            if (!await modules.IsEnabledAsync(licenseId, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents, cancellationToken)) return NotFound();
        }
        else if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ViewPeople, cancellationToken)) return Forbid();
        var linked = incidentLinked || await context.Residents.AsNoTracking().ForLicense(licenseId).AnyAsync(x => x.ImgUrl == reference, cancellationToken)
            || await context.AccessVisits.AsNoTracking().AnyAsync(x => x.LicenseId == licenseId && x.PhotoUrl == reference, cancellationToken)
            || await context.VehicleAccessAudits.AsNoTracking().AnyAsync(x => x.Device.LicenseId == licenseId && x.SnapshotReference == reference, cancellationToken);
        if (!linked) return NotFound();
        var file = await media.ReadAsync(licenseId, mediaId, cancellationToken);
        return file is null ? NotFound() : File(file.Content, file.ContentType);
    }
}
