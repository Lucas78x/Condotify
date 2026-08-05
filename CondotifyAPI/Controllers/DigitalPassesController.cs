using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace CondotifyAPI.Controllers;

[ApiController]
public sealed class DigitalPassesController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IDigitalPassProviderService providers,
    IAppleWalletPassService appleWallet,
    IConfiguration configuration) : ControllerBase
{
    [Authorize]
    [HttpPost("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Issue(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var visit = await VisitQuery().FirstOrDefaultAsync(x => x.Id == visitId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (visit is null) return NotFound();
        if (visit.ValidTo <= DateTime.UtcNow || visit.Status is not (AccessVisitStatusEnum.Scheduled or AccessVisitStatusEnum.CheckedIn))
            return Conflict(new { Errors = "A visita nao esta valida para emissao de passe." });
        if (string.IsNullOrWhiteSpace(visit.Credential.Identifier))
            return Conflict(new { Errors = "A visita ainda nao possui uma credencial de acesso." });

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId, HttpContext.RequestAborted);
        if (pass is null)
        {
            pass = new DigitalPassDTO { Id = Guid.NewGuid(), LicenseId = licenseId, VisitId = visitId, CreatedAt = now };
            context.DigitalPasses.Add(pass);
        }
        pass.TokenHash = hash; pass.Status = DigitalPassStatusEnum.Active; pass.IssuedAt = now;
        pass.ExpiresAt = visit.ValidTo; pass.RevokedAt = null; pass.UpdatedAt = now;
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Issued", Status = "Success", Summary = $"Passe digital emitido para {visit.VisitorName}.",
            DetailsJson = JsonSerializer.Serialize(new { visitId, visit.ValidFrom, visit.ValidTo }),
            UserId = CurrentUserId(), UserName = CurrentActor(), CreatedAt = now
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        pass.Visit = visit; pass.License = visit.License;
        var publicUrl = PublicUrl(token);
        return Ok(ToOutput(pass, token, publicUrl));
    }

    [Authorize]
    [HttpDelete("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Revoke(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId && x.LicenseId == licenseId, HttpContext.RequestAborted);
        if (pass is null) return NotFound();
        pass.Status = DigitalPassStatusEnum.Revoked; pass.RevokedAt = DateTime.UtcNow; pass.UpdatedAt = DateTime.UtcNow;
        pass.TokenHash = Hash($"revoked:{pass.Id:N}:{RandomNumberGenerator.GetHexString(16)}");
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Revoked", Status = "Success", Summary = "Passe digital revogado.", DetailsJson = "{}",
            UserId = CurrentUserId(), UserName = CurrentActor(), CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(HttpContext.RequestAborted);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("api/public/passes/{token}")]
    public async Task<IActionResult> Public(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return NotFound();
        var hash = Hash(token);
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
        return Ok(ToOutput(pass, token, PublicUrl(token)));
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
            .FirstOrDefaultAsync(x => x.TokenHash == Hash(token), HttpContext.RequestAborted);
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

    private IQueryable<CondotifyAPI.Domain.DTO.Invitation.AccessVisitDTO> VisitQuery() => context.AccessVisits
        .Include(x => x.License).Include(x => x.Credential)
        .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block);

    private string PublicUrl(string token)
    {
        var root = configuration["DigitalPass:PublicAppUrl"] ?? Environment.GetEnvironmentVariable("CONDOTIFY_PUBLIC_APP_URL");
        if (string.IsNullOrWhiteSpace(root)) root = $"{Request.Scheme}://{Request.Host}";
        return $"{root.TrimEnd('/')}/passe/{Uri.EscapeDataString(token)}";
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
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string CurrentActor() => User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuario";
}
