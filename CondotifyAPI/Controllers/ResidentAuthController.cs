using CondotifyAPI.Data.Login;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Models.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

/// <summary>
/// Resident login, the session-issuing counterpart to <c>AuthController.Login</c> for
/// staff. Every failure - unknown email, wrong password, inactive resident, resident with
/// no password set, or an expired temporary resident - returns the exact same
/// <see cref="FailureResponse"/> instance, and <see cref="Decide"/> pays for a real
/// password-hash comparison on every call (even when there is no resident, or no stored
/// password to compare against) so that path is not measurably faster than a genuine
/// wrong-password check. See <see cref="Decide"/> for the full rationale.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/auth/resident")]
public sealed class ResidentAuthController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher<ResidentAccess> _passwordHasher;
    private readonly IRefreshTokenService _refreshTokens;

    public ResidentAuthController(
        DatabaseContext context,
        IJwtTokenService jwt,
        IPasswordHasher<ResidentAccess> passwordHasher,
        IRefreshTokenService refreshTokens)
    {
        _context = context;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
        _refreshTokens = refreshTokens;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] ResidentLoginIn input, CancellationToken cancellationToken)
    {
        var email = (input.Email ?? string.Empty).Trim().ToLowerInvariant();

        // Same query shape regardless of whether email is well-formed, so a malformed
        // email does not short-circuit before the hash-comparison work in Decide.
        var resident = string.IsNullOrWhiteSpace(email)
            ? null
            : await _context.Residents
                .Include(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
                .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email, cancellationToken);

        var now = DateTime.UtcNow;
        var decision = Decide(resident, input.Password ?? string.Empty, _passwordHasher, now);
        if (!decision.Success) return InvalidCredentials();

        // decision.Success is only ever true when resident is non-null and a licence was
        // resolved - both are guaranteed together by Decide.
        resident!.LastAccess = now;

        var deviceLabel = string.IsNullOrWhiteSpace(input.DeviceLabel) ? "Desconhecido" : input.DeviceLabel!.Trim();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var refresh = await _refreshTokens.IssueAsync(resident.Id, PrincipalTypes.Resident, deviceLabel, ip, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var accessToken = _jwt.CreateResidentAccessToken(resident, decision.LicenseId!.Value);
        var unit = ResolvePrimaryUnit(resident);

        return Ok(new ResidentLoginOut
        {
            Result = "Success",
            AccessToken = accessToken,
            RefreshToken = refresh.Token,
            ExpiresIn = _jwt.AccessTokenLifetimeSeconds,
            ResidentId = resident.Id,
            Name = resident.Name,
            Email = resident.Email,
            AccessType = resident.AccessType,
            LicenseId = decision.LicenseId,
            LicenseName = unit?.Block?.License?.Name,
            UnitId = unit?.Id,
            UnitNumber = unit?.Number,
            BlockName = unit?.Block?.Name,
        });
    }

    /// <summary>The one shape every failure returns - 401 with a body that carries no
    /// signal about which of the possible failure reasons actually applied.</summary>
    private static readonly ResidentLoginOut FailureResponse = new() { Result = "InvalidCredentials" };

    private static IActionResult InvalidCredentials() => new UnauthorizedObjectResult(FailureResponse);

    // --- Pure, tested decision logic ----------------------------------------------------

    /// <summary>Result of <see cref="Decide"/>. On failure this is always the exact same
    /// <see cref="Failure"/> instance - there is nothing else on this type for a caller
    /// to inspect that could distinguish "wrong password" from "no such resident".</summary>
    internal sealed record ResidentLoginDecision(bool Success, Guid? LicenseId)
    {
        internal static readonly ResidentLoginDecision Failure = new(false, null);
        internal static ResidentLoginDecision Ok(Guid licenseId) => new(true, licenseId);
    }

    /// <summary>
    /// A hash of a fixed, never-used password, verified whenever there is no real stored
    /// hash to compare against (resident not found, or resident.Password is empty). This
    /// keeps the "no such resident" and "no password set" paths paying for the same
    /// expensive hash comparison as a genuine wrong-password attempt, so a timing
    /// difference cannot be used to learn whether an email is registered.
    /// </summary>
    internal static readonly string DummyPasswordHash =
        new PasswordHasher<ResidentAccess>().HashPassword(null!, "Dummy-Timing-Guard-Password-1!");

    /// <summary>
    /// Decides whether a login attempt succeeds. Always calls
    /// <see cref="IPasswordHasher{TUser}.VerifyHashedPassword"/> exactly once - against the
    /// resident's real stored hash when there is one, or <see cref="DummyPasswordHash"/>
    /// otherwise - before any other check, so the expensive part of this decision runs
    /// identically whether or not the resident exists or has a password set.
    ///
    /// Built-in <c>PasswordHasher&lt;TUser&gt;.VerifyHashedPassword</c> treats an empty
    /// stored hash as a 0-length payload and returns <see cref="PasswordVerificationResult.Failed"/>
    /// rather than throwing (verified by RegistrationInvitePasswordTests' sibling coverage
    /// of the same hasher, and by this class's own EmptyStoredPassword test) - so a resident
    /// with <c>Password == ""</c> is rejected like any other failure, never a crash.
    /// </summary>
    internal static ResidentLoginDecision Decide(
        ResidentAccessDTO? resident,
        string password,
        IPasswordHasher<ResidentAccess> hasher,
        DateTime now)
    {
        var hasStoredPassword = resident is not null && !string.IsNullOrEmpty(resident.Password);
        var storedHash = hasStoredPassword ? resident!.Password : DummyPasswordHash;

        var verification = hasher.VerifyHashedPassword(null!, storedHash, password ?? string.Empty);
        var passwordMatches = verification != PasswordVerificationResult.Failed;

        if (resident is null || !hasStoredPassword || !passwordMatches)
            return ResidentLoginDecision.Failure;

        if (!ResidentAuthorizationService.ResidentCanSignIn(resident, now))
            return ResidentLoginDecision.Failure;

        var licenseId = ResolveLicenseId(resident);
        return licenseId is null ? ResidentLoginDecision.Failure : ResidentLoginDecision.Ok(licenseId.Value);
    }

    /// <summary>
    /// A resident reaches a licence through Unit -&gt; Block -&gt; License. The primary
    /// <see cref="ResidentUnitLinkDTO"/> (or, absent any links, the resident's direct
    /// <see cref="ResidentAccessDTO.Unit"/> - CondotifyAPI.DevelopmentDataSeeder's own demo
    /// resident has zero rows in ResidentUnitLinks, only the direct UnitId) decides which
    /// licence the resulting token is scoped to. A resident linked to units under more than
    /// one licence signs in to the licence of their primary unit only - the other licence
    /// is simply not reachable through this token, by design; there is no attempt to merge
    /// or pick "the more important one" across licences, which would be ambiguous.
    /// </summary>
    internal static Guid? ResolveLicenseId(ResidentAccessDTO resident)
    {
        var unit = ResolvePrimaryUnit(resident);
        if (unit?.Block is null || unit.Block.LicenseId == Guid.Empty) return null;
        return unit.Block.LicenseId;
    }

    /// <summary>Same "primary link, else first link" precedent already used by
    /// AccessRouteResolver.ResolveAudience and PeopleManagementController - ordered by
    /// CreatedAt for a deterministic pick if more than one link is (incorrectly) flagged
    /// primary. Falls back to the resident's direct Unit when there are no links at all.</summary>
    internal static UnitDTO? ResolvePrimaryUnit(ResidentAccessDTO resident)
    {
        var ordered = resident.UnitLinks.OrderBy(x => x.CreatedAt).ToList();
        var primaryLink = ordered.FirstOrDefault(x => x.IsPrimary) ?? ordered.FirstOrDefault();
        return primaryLink?.Unit ?? resident.Unit;
    }
}
