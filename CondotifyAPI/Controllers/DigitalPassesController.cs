using System.Security.Claims;
using System.Security.Cryptography;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
public sealed class DigitalPassesController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IDigitalPassProviderService providers,
    IDigitalPassIssuanceService issuance,
    IAppleWalletPassService appleWallet,
    IConfiguration configuration) : ControllerBase
{
    [Authorize]
    [HttpPost("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Issue(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var result = await issuance.IssueAsync(licenseId, visitId, $"{Request.Scheme}://{Request.Host}", CurrentUserId(), CurrentActor(), HttpContext.RequestAborted);
        return result.Outcome switch
        {
            DigitalPassIssueOutcome.VisitNotFound => NotFound(),
            DigitalPassIssueOutcome.Success => Ok(result.Pass),
            _ => Conflict(new { Errors = result.Error })
        };
    }

    [Authorize]
    [HttpDelete("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Revoke(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var result = await issuance.RevokeAsync(licenseId, visitId, CurrentUserId(), CurrentActor(), HttpContext.RequestAborted);
        return result.Outcome == DigitalPassRevokeOutcome.NotFound ? NotFound() : NoContent();
    }

    [AllowAnonymous]
    [HttpGet("api/public/passes/{token}")]
    public async Task<IActionResult> Public(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return NotFound();
        var hash = DigitalPassIssuanceService.Hash(token);
        var pass = await context.DigitalPasses.Include(x => x.License)
            .Include(x => x.Visit).ThenInclude(x => x.Credential)
            .Include(x => x.Visit).ThenInclude(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.TokenHash == hash, HttpContext.RequestAborted);
        if (pass is null) return NotFound();
        var invalid = pass.Status != DigitalPassStatusEnum.Active || pass.ExpiresAt <= DateTime.UtcNow ||
                      pass.Visit.Status is not (AccessVisitStatusEnum.Scheduled or AccessVisitStatusEnum.CheckedIn);
        if (invalid) return StatusCode(StatusCodes.Status410Gone, new { Errors = "Este passe foi revogado ou expirou." });
        pass.LastViewedAt = DateTime.UtcNow; pass.ViewCount++; pass.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(ToOutput(pass, token, DigitalPassProviderService.ResolvePublicUrl(configuration, $"{Request.Scheme}://{Request.Host}", token)));
    }

    [AllowAnonymous]
    [HttpGet("api/public/passes/{token}/apple")]
    public async Task<IActionResult> Apple(string token)
    {
        if (!appleWallet.IsConfigured) return NotFound();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return NotFound();
        var pass = await context.DigitalPasses.AsNoTracking().Include(x => x.License)
            .Include(x => x.Visit).ThenInclude(x => x.Credential)
            .Include(x => x.Visit).ThenInclude(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.TokenHash == DigitalPassIssuanceService.Hash(token), HttpContext.RequestAborted);
        if (pass is null) return NotFound();
        if (pass.Status != DigitalPassStatusEnum.Active || pass.ExpiresAt <= DateTime.UtcNow ||
            pass.Visit.Status is not (AccessVisitStatusEnum.Scheduled or AccessVisitStatusEnum.CheckedIn))
            return StatusCode(StatusCodes.Status410Gone);
        try
        {
            return File(appleWallet.Build(pass), "application/vnd.apple.pkpass", $"condotify-{pass.Id:N}.pkpass");
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or InvalidOperationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { Errors = "A assinatura do Apple Wallet esta indisponivel." });
        }
    }

    private Condotify.Models.DigitalPassViewModel ToOutput(DigitalPassDTO pass, string token, string publicUrl)
    {
        var output = providers.Build(pass, token, publicUrl);
        if (appleWallet.IsConfigured)
        {
            output.AppleWalletUrl = $"{Request.Scheme}://{Request.Host}/api/public/passes/{Uri.EscapeDataString(token)}/apple";
            output.AppleWalletConfigured = true;
        }
        return output;
    }
    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentActor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
}
