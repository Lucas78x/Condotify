using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/announcements")]
public sealed class ResidentAnnouncementsController(DatabaseContext context, IResidentAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var results = await AnnouncementsController.ListAnnouncementsCore(context, grant.LicenseId, cancellationToken);
        return Ok(results);
    }
}
