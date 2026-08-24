using System.Security.Claims;
using AutoMapper;
using CondotifyAPI.Data.Login;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

/// <summary>
/// Session lifecycle for both principal types (task 7): refresh (mint a fresh access +
/// refresh pair from a still-valid refresh token), logout (revoke one session), logout/all
/// (revoke every session for the caller), and sessions (list the caller's own active
/// "devices"). None of this owns any rule - <see cref="IRefreshTokenService"/>,
/// <see cref="IJwtTokenService"/> and <see cref="ResidentAuthorizationService"/> already do,
/// and are composed here rather than modified.
///
/// <see cref="Refresh"/> is the one route that must get its access-token kind exactly right:
/// a refresh token issued to a resident (<c>RefreshTokenDTO.SubjectType == PrincipalTypes.Resident</c>)
/// must mint a resident access token, never a staff one. That fork is <see cref="ResolveAccessTokenKind"/> -
/// a pure function with no DB dependency, exhaustively covered by SessionControllerTests
/// (including an end-to-end check that feeds its result into the real
/// <see cref="JwtTokenService"/> and reads back the signed token's principal_type claim).
///
/// <c>logout</c>, <c>logout/all</c> and <c>sessions</c> require authentication but must accept
/// EITHER principal type - the default policy (Program.cs) requires principal_type=user, which
/// would otherwise lock residents out of managing their own sessions. They are annotated with
/// the <see cref="AnyPrincipalPolicy"/> named policy (added in Program.cs, requiring only
/// <c>RequireAuthenticatedUser()</c> - no claim check) instead of a bare <c>[Authorize]</c>. The
/// default policy itself is untouched.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class SessionController : ControllerBase
{
    /// <summary>Named policy: authenticated, either principal type. Deliberately does not
    /// require <see cref="PrincipalTypes.Claim"/> at all - unlike the default policy (staff
    /// only) and the "Resident" policy (resident only), both defined in Program.cs and left
    /// untouched by this task.</summary>
    public const string AnyPrincipalPolicy = "AuthenticatedAnyPrincipal";

    private readonly DatabaseContext _context;
    private readonly IMapper _mapper;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokens;

    public SessionController(DatabaseContext context, IMapper mapper, IJwtTokenService jwt, IRefreshTokenService refreshTokens)
    {
        _context = context;
        _mapper = mapper;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Refresh([FromBody] RefreshIn input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input?.RefreshToken)) return InvalidRefresh();

        // Peeked read-only, before any rotation: if the subject no longer exists or is no
        // longer eligible (e.g. a resident deactivated after issuing this token), the
        // presented refresh token is left untouched rather than burned on a failed attempt.
        var hash = RefreshTokenService.HashToken(input.RefreshToken);
        var existing = await _context.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null) return InvalidRefresh();

        var kind = ResolveAccessTokenKind(existing.SubjectType);
        if (kind is null) return InvalidRefresh();

        var accessToken = kind == AccessTokenKind.User
            ? await MintStaffAccessTokenAsync(existing.SubjectId, cancellationToken)
            : await MintResidentAccessTokenAsync(existing.SubjectId, existing.ContextLicenseId, cancellationToken);
        if (accessToken is null) return InvalidRefresh();

        var rotated = await _refreshTokens.RotateAsync(input.RefreshToken, cancellationToken);
        if (rotated is null) return InvalidRefresh();

        return Ok(new RefreshOut
        {
            Result = "Success",
            AccessToken = accessToken,
            RefreshToken = rotated.Token,
            ExpiresIn = _jwt.AccessTokenLifetimeSeconds,
        });
    }

    [HttpPost("logout")]
    [Authorize(Policy = AnyPrincipalPolicy)]
    public async Task<IActionResult> Logout([FromBody] LogoutIn? input, CancellationToken cancellationToken)
    {
        var subject = ResolveSubject(User);
        if (subject is null) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(input?.RefreshToken))
        {
            var hash = RefreshTokenService.HashToken(input.RefreshToken);
            var owner = await _context.RefreshTokens.AsNoTracking()
                .Where(x => x.TokenHash == hash)
                .Select(x => new { x.SubjectId, x.SubjectType })
                .FirstOrDefaultAsync(cancellationToken);

            // Logout is idempotent (204 either way - see IRefreshTokenService.RevokeAsync),
            // so this ownership check is never an oracle for "does this token exist". It only
            // stops an authenticated caller from using someone else's refresh token value
            // (which they would have to already possess) as a way to end someone else's
            // session.
            if (owner is not null && TokenBelongsToSubject(owner.SubjectId, owner.SubjectType, subject.Value))
                await _refreshTokens.RevokeAsync(input!.RefreshToken!, cancellationToken);
        }

        return NoContent();
    }

    [HttpPost("logout/all")]
    [Authorize(Policy = AnyPrincipalPolicy)]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var subject = ResolveSubject(User);
        if (subject is null) return Unauthorized();

        await _refreshTokens.RevokeAllAsync(subject.Value.Id, subject.Value.SubjectType, cancellationToken);
        return NoContent();
    }

    [HttpGet("sessions")]
    [Authorize(Policy = AnyPrincipalPolicy)]
    public async Task<IActionResult> Sessions(CancellationToken cancellationToken)
    {
        // Subject comes only from the caller's own token claims - never a route/query
        // parameter - so this can only ever list the caller's own sessions.
        var subject = ResolveSubject(User);
        if (subject is null) return Unauthorized();

        var all = await _refreshTokens.ListAsync(subject.Value.Id, subject.Value.SubjectType, cancellationToken);
        var active = SelectActiveSessions(all, DateTime.UtcNow);

        return Ok(active.Select(ToSessionOut));
    }

    private async Task<string?> MintStaffAccessTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var dto = await _context.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        return dto is null ? null : _jwt.CreateAccessToken(_mapper.Map<UserAccess>(dto));
    }

    private async Task<string?> MintResidentAccessTokenAsync(Guid residentId, Guid? contextLicenseId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters() deliberado: chamado a partir de um endpoint AllowAnonymous
        // de refresh de token, sem principal para popular o accessor. Consulta ja restrita
        // a um residentId especifico -- ver Task 7 do plano de filtro de tenant.
        var resident = await _context.Residents.IgnoreQueryFilters()
            .Include(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
            .FirstOrDefaultAsync(x => x.Id == residentId, cancellationToken);

        if (resident is null || !ResidentAuthorizationService.ResidentCanSignIn(resident, DateTime.UtcNow))
            return null;

        var licenseId = ResidentAuthController.ResolveLicenseId(resident, contextLicenseId, DateTime.UtcNow);
        return licenseId is null ? null : _jwt.CreateResidentAccessToken(resident, licenseId.Value);
    }

    private static SessionOut ToSessionOut(RefreshTokenSummary s) => new()
    {
        Id = s.Id,
        DeviceLabel = s.DeviceLabel,
        CreatedIp = s.CreatedIp,
        CreatedAt = s.CreatedAt,
        ExpiresAt = s.ExpiresAt,
    };

    private static IActionResult InvalidRefresh() => new UnauthorizedObjectResult(new RefreshOut { Result = "InvalidToken" });

    // --- Pure, tested decision logic ---------------------------------------------------------

    public enum AccessTokenKind { User, Resident }

    /// <summary>
    /// The single fork deciding which JWT-minting method a refresh call uses, keyed on the
    /// EXISTING refresh token row's own <c>SubjectType</c> - never on anything the caller
    /// supplies. An unrecognised value resolves to null (rejected), never to
    /// <see cref="AccessTokenKind.User"/> - see SessionControllerTests for the exhaustive
    /// check that this can never silently hand out staff access.
    /// </summary>
    internal static AccessTokenKind? ResolveAccessTokenKind(string subjectType) => subjectType switch
    {
        PrincipalTypes.User => AccessTokenKind.User,
        PrincipalTypes.Resident => AccessTokenKind.Resident,
        _ => null,
    };

    internal readonly record struct AuthenticatedSubject(Guid Id, string SubjectType);

    /// <summary>Derives who is calling from the access token's own claims - never from a
    /// route/query/body parameter - so logout/logout-all/sessions can only ever act on the
    /// caller's own data. Mirrors <see cref="ResidentAuthorizationService.TryResident"/>'s
    /// precedent but accepts either principal type.</summary>
    internal static AuthenticatedSubject? ResolveSubject(ClaimsPrincipal principal)
    {
        var subjectType = principal.FindFirstValue(PrincipalTypes.Claim);
        if (subjectType != PrincipalTypes.User && subjectType != PrincipalTypes.Resident) return null;

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? new AuthenticatedSubject(id, subjectType)
            : null;
    }

    /// <summary>Used by <see cref="Logout"/> so a caller can only ever revoke a refresh
    /// token that is actually theirs (id AND type, matching <c>SelectTokensToRevoke</c>'s
    /// same-keying precedent in RefreshTokenService).</summary>
    internal static bool TokenBelongsToSubject(Guid tokenSubjectId, string tokenSubjectType, AuthenticatedSubject caller) =>
        tokenSubjectId == caller.Id && tokenSubjectType == caller.SubjectType;

    /// <summary>GET /api/auth/sessions lists only currently-active sessions: not revoked,
    /// and not yet expired. ExpiresAt == now is treated as already expired, matching
    /// RefreshTokenService.Decide's identical boundary.</summary>
    internal static IReadOnlyCollection<RefreshTokenSummary> SelectActiveSessions(IEnumerable<RefreshTokenSummary> sessions, DateTime now) =>
        sessions.Where(x => x.RevokedAt is null && x.ExpiresAt > now).ToList();

    /// <summary>
    /// The device label is caller-supplied (or, absent that, the User-Agent header) free-form
    /// text and is therefore untrusted. It is stored only as a plain column value
    /// (RefreshTokenConfiguration.DeviceLabel, max length 200) and returned verbatim as a JSON
    /// property - it is never interpolated into SQL, a log message, or anything shell/command
    /// related. Trimmed and capped here so an oversized User-Agent cannot fail the save.
    /// </summary>
    internal static string ResolveDeviceLabel(string? explicitLabel, string? userAgent)
    {
        var candidate = string.IsNullOrWhiteSpace(explicitLabel) ? userAgent : explicitLabel;
        if (string.IsNullOrWhiteSpace(candidate)) return "Desconhecido";

        candidate = candidate.Trim();
        return candidate.Length > 200 ? candidate[..200] : candidate;
    }
}
