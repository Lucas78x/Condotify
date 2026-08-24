using System.Security.Claims;
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
[Route("api/auth/resident")]
public sealed class ResidentAuthController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher<ResidentAccess> _passwordHasher;
    private readonly IRefreshTokenService _refreshTokens;
    private readonly IResidentPasswordRecoveryService _passwordRecovery;
    private readonly IResidentPasswordRecoveryMailer _passwordRecoveryMailer;

    public ResidentAuthController(
        DatabaseContext context,
        IJwtTokenService jwt,
        IPasswordHasher<ResidentAccess> passwordHasher,
        IRefreshTokenService refreshTokens,
        IResidentPasswordRecoveryService passwordRecovery,
        IResidentPasswordRecoveryMailer passwordRecoveryMailer)
    {
        _context = context;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
        _refreshTokens = refreshTokens;
        _passwordRecovery = passwordRecovery;
        _passwordRecoveryMailer = passwordRecoveryMailer;
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
            // IgnoreQueryFilters() deliberado: login e AllowAnonymous, nao ha principal
            // para popular o accessor. A consulta ja e restrita a um email especifico
            // (nao uma listagem) -- ver Task 7 do plano de filtro de tenant.
            : await _context.Residents.IgnoreQueryFilters()
                .Include(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
                .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
                // Convites pendentes usam um cadastro provisório com senha vazia e podem
                // compartilhar o e-mail da conta central já existente. Nunca deixe esse
                // placeholder interceptar o login da conta efetivamente ativada.
                .FirstOrDefaultAsync(x => x.Email.ToLower() == email && x.Password != string.Empty, cancellationToken);

        var now = DateTime.UtcNow;
        var decision = Decide(resident, input.Password ?? string.Empty, _passwordHasher, now, input.LicenseId);
        if (!decision.Success) return InvalidCredentials();

        // decision.Success is only ever true when resident is non-null and a licence was
        // resolved - both are guaranteed together by Decide.
        resident!.LastAccess = now;

        var deviceLabel = SessionController.ResolveDeviceLabel(input.DeviceLabel, Request.Headers.UserAgent.ToString());
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var refresh = await _refreshTokens.IssueAsync(resident.Id, PrincipalTypes.Resident, deviceLabel, ip, cancellationToken, decision.LicenseId);

        await _context.SaveChangesAsync(cancellationToken);

        return Ok(Success(resident, decision.LicenseId!.Value, refresh));
    }

    [HttpGet("contexts")]
    [Authorize(Policy = "Resident")]
    public async Task<IActionResult> Contexts(CancellationToken cancellationToken)
    {
        var resident = await LoadCurrentResidentAsync(cancellationToken);
        return resident is null ? Unauthorized() : Ok(ResolveContexts(resident, DateTime.UtcNow));
    }

    [HttpPost("contexts/{licenseId:guid}/switch")]
    [Authorize(Policy = "Resident")]
    public async Task<IActionResult> SwitchContext(
        Guid licenseId,
        [FromBody] SwitchResidentContextIn? input,
        CancellationToken cancellationToken)
    {
        var resident = await LoadCurrentResidentAsync(cancellationToken);
        if (resident is null || ResolveLicenseId(resident, licenseId, DateTime.UtcNow) != licenseId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(input?.RefreshToken)) return Unauthorized();
        var refresh = await _refreshTokens.RotateContextAsync(
            input.RefreshToken, resident.Id, PrincipalTypes.Resident, licenseId, cancellationToken);
        if (refresh is null) return Unauthorized();
        return Ok(Success(resident, licenseId, refresh));
    }

    private async Task<ResidentAccessDTO?> LoadCurrentResidentAsync(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var residentId)) return null;
        // Ignora o filtro do contexto atual de propósito: esta consulta é restrita ao
        // próprio subject autenticado e precisa enxergar os demais condomínios dele.
        return await _context.Residents.IgnoreQueryFilters()
            .Include(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
            .Include(x => x.UnitLinks).ThenInclude(x => x.Unit).ThenInclude(x => x.Block).ThenInclude(x => x.License)
            .FirstOrDefaultAsync(x => x.Id == residentId, cancellationToken);
    }

    private ResidentLoginOut Success(ResidentAccessDTO resident, Guid licenseId, RefreshTokenIssued refresh)
    {
        var contexts = ResolveContexts(resident, DateTime.UtcNow);
        var selected = contexts.First(x => x.LicenseId == licenseId);
        var unit = selected.Units.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.BlockName).ThenBy(x => x.UnitNumber).First();
        return new ResidentLoginOut
        {
            Result = "Success", AccessToken = _jwt.CreateResidentAccessToken(resident, licenseId),
            RefreshToken = refresh.Token, ExpiresIn = _jwt.AccessTokenLifetimeSeconds,
            ResidentId = resident.Id, Name = resident.Name, Email = resident.Email,
            AccessType = resident.AccessType, LicenseId = licenseId, LicenseName = selected.LicenseName,
            UnitId = unit.UnitId, UnitNumber = unit.UnitNumber, BlockName = unit.BlockName,
            Contexts = contexts
        };
    }

    /// <summary>The one shape every failure returns - 401 with a body that carries no
    /// signal about which of the possible failure reasons actually applied.</summary>
    private static readonly ResidentLoginOut FailureResponse = new() { Result = "InvalidCredentials" };

    private static IActionResult InvalidCredentials() => new UnauthorizedObjectResult(FailureResponse);

    // --- Password recovery (task 8) -----------------------------------------------------

    /// <summary>
    /// The one response body this endpoint ever returns. There is exactly one <c>return</c>
    /// statement in <see cref="ForgotPassword"/> and it is always this constant - not a value
    /// chosen after checking whether the e-mail matched a resident. That is what makes "202
    /// whether or not the e-mail exists, whether or not SMTP is configured, whether or not
    /// sending succeeded" true by construction rather than by discipline: there is no branch
    /// in this action that could return anything else, so nothing downstream of it (a lookup
    /// throwing, a resident not being found, SMTP being unreachable) can change the response.
    /// </summary>
    internal static readonly ForgotPasswordOut ForgotPasswordAcceptedBody = new() { Result = "Accepted" };

    /// <summary>
    /// POST /api/auth/resident/password/forgot. Always answers 202 (see
    /// <see cref="ForgotPasswordAcceptedBody"/>) - whether the e-mail belongs to a resident,
    /// whether that resident is eligible to sign in, whether a token was actually issued
    /// (<see cref="IResidentPasswordRecoveryService.IssueAsync"/> also silently declines when a
    /// token for the same resident was issued too recently - see
    /// <see cref="ResidentPasswordRecoveryService.ShouldThrottle"/>), and whether the e-mail
    /// send itself succeeded. All of that work happens inside <see cref="TryRunAsync"/>, which
    /// swallows every exception, so there is no path from a lookup/DB/SMTP failure back out to
    /// this method's single return statement.
    /// </summary>
    [HttpPost("password/forgot")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordIn? input, CancellationToken cancellationToken)
    {
        await TryRunAsync(() => IssueAndSendRecoveryAsync(input?.Email, cancellationToken));

        return Accepted(ForgotPasswordAcceptedBody);
    }

    private async Task IssueAndSendRecoveryAsync(string? email, CancellationToken cancellationToken)
    {
        var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail)) return;

        var resident = await _context.Residents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email.ToLower() == normalizedEmail && x.Password != string.Empty, cancellationToken);
        if (resident is null || !ResidentAuthorizationService.ResidentCanSignIn(resident, DateTime.UtcNow)) return;

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        var issued = await _passwordRecovery.IssueAsync(resident.Id, ip, cancellationToken);
        if (issued is null) return; // throttled - a token for this resident was issued moments ago

        await _passwordRecoveryMailer.SendAsync(resident.Email, resident.Name, issued.Token, cancellationToken);
    }

    /// <summary>
    /// POST /api/auth/resident/password/reset. Validates the new password against
    /// <see cref="PasswordPolicy"/> FIRST - before the recovery token
    /// <see cref="ForgotPassword"/> emailed out is even looked up, let alone consumed - so a
    /// caller who mistypes a password that fails policy does not burn their one-time code.
    /// Only once that succeeds does it consume the token and apply the same "write the hash,
    /// revoke every refresh token" primitive (<see cref="ApplyPasswordAsync"/>) that
    /// <see cref="ChangePassword"/> also ends in (via <see cref="ApplyNewPasswordAsync"/>).
    /// </summary>
    [HttpPost("password/reset")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ResetPassword([FromBody] ResidentResetPasswordIn? input, CancellationToken cancellationToken)
    {
        // Validated BEFORE the token is looked up (let alone consumed): a caller who mistypes
        // a password that fails PasswordPolicy must not burn their one-time recovery code just
        // to find that out - they would otherwise have to request a whole new "forgot" e-mail.
        var passwordResult = ResidentPasswordSetter.Resolve(input?.NewPassword, _passwordHasher, "nova senha");
        if (!passwordResult.Succeeded)
            return BadRequest(new ResidentPasswordOperationOut { Result = "InvalidPassword", Error = passwordResult.Error });

        if (string.IsNullOrWhiteSpace(input?.Token))
            return InvalidRecoveryToken();

        var residentId = await _passwordRecovery.ConsumeAsync(input.Token, cancellationToken);
        if (residentId is null)
            return InvalidRecoveryToken();

        var resident = await _context.Residents.FirstOrDefaultAsync(x => x.Id == residentId.Value, cancellationToken);
        if (resident is null)
            return InvalidRecoveryToken();

        await ApplyPasswordAsync(
            resident,
            passwordResult.Hash!,
            () => _refreshTokens.RevokeAllAsync(resident.Id, PrincipalTypes.Resident, cancellationToken));

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new ResidentPasswordOperationOut { Result = "Success" });
    }

    /// <summary>
    /// POST /api/auth/resident/password/change - requires an authenticated resident (policy
    /// "Resident", see Program.cs) and their current password, then the same "validate, hash,
    /// revoke every refresh token" tail as <see cref="ResetPassword"/>. A password change is
    /// what a resident does when they believe they may be compromised, so every existing
    /// session for them - including the one making this very request - is ended; the caller
    /// must sign in again with the new password afterwards.
    /// </summary>
    [HttpPost("password/change")]
    [Authorize(Policy = "Resident")]
    public async Task<IActionResult> ChangePassword([FromBody] ResidentChangePasswordIn? input, CancellationToken cancellationToken)
    {
        if (!ResidentAuthorizationService.TryResident(User, out var residentId, out _))
            return Unauthorized();

        var resident = await _context.Residents.FirstOrDefaultAsync(x => x.Id == residentId, cancellationToken);
        if (resident is null)
            return Unauthorized();

        if (!VerifyCurrentPassword(resident.Password, input?.CurrentPassword ?? string.Empty, _passwordHasher))
            return BadRequest(new ResidentPasswordOperationOut { Result = "WrongCurrentPassword", Error = "Senha atual incorreta." });

        var result = await ApplyNewPasswordAsync(
            resident,
            input?.NewPassword ?? string.Empty,
            _passwordHasher,
            () => _refreshTokens.RevokeAllAsync(resident.Id, PrincipalTypes.Resident, cancellationToken));

        if (!result.Succeeded)
            return BadRequest(new ResidentPasswordOperationOut { Result = "InvalidPassword", Error = result.Error });

        await _context.SaveChangesAsync(cancellationToken);
        return Ok(new ResidentPasswordOperationOut { Result = "Success" });
    }

    private static IActionResult InvalidRecoveryToken() =>
        new BadRequestObjectResult(new ResidentPasswordOperationOut { Result = "InvalidToken", Error = "Código de recuperação inválido ou expirado." });

    // --- Pure, tested decision logic (password recovery) --------------------------------

    /// <summary>
    /// Runs <paramref name="action"/>, swallowing any exception it throws. Backs
    /// <see cref="ForgotPassword"/>'s "always 202" guarantee: a DB lookup failing, the
    /// recovery-token table being unreachable, or the SMTP send throwing must never surface as
    /// anything other than silence to the caller.
    /// </summary>
    internal static async Task TryRunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            // Deliberately swallowed - see ForgotPassword's remarks.
        }
    }

    /// <summary>
    /// Verifies <paramref name="presented"/> against <paramref name="storedHash"/>, using
    /// <see cref="DummyPasswordHash"/> when there is no real stored hash to compare against -
    /// same timing-safety precedent as <see cref="Decide"/>, though here the resident is
    /// already known to exist (this backs <see cref="ChangePassword"/>, which requires
    /// authentication first).
    /// </summary>
    internal static bool VerifyCurrentPassword(string storedHash, string presented, IPasswordHasher<ResidentAccess> hasher)
    {
        var hashToCompare = string.IsNullOrEmpty(storedHash) ? DummyPasswordHash : storedHash;
        return hasher.VerifyHashedPassword(null!, hashToCompare, presented ?? string.Empty) != PasswordVerificationResult.Failed;
    }

    /// <summary>
    /// The shared tail of both /reset and /change: validate <paramref name="newPassword"/>
    /// against <see cref="PasswordPolicy"/> (via <see cref="ResidentPasswordSetter"/>, "nova
    /// senha" wording - the resident already had a password), and only when that succeeds,
    /// write the new hash onto <paramref name="resident"/> and invoke
    /// <paramref name="revokeAllTokens"/>. The revoke delegate is called exactly once, and only
    /// on success - never for a password that fails policy - which is the property
    /// ResidentAuthControllerPasswordTests checks directly with a counting fake, since there is
    /// no EF InMemory/SQLite provider to exercise the real
    /// <see cref="IRefreshTokenService.RevokeAllAsync"/> call this delegate wraps in production.
    /// </summary>
    internal static async Task<ResidentPasswordResult> ApplyNewPasswordAsync(
        ResidentAccessDTO resident,
        string newPassword,
        IPasswordHasher<ResidentAccess> hasher,
        Func<Task> revokeAllTokens)
    {
        var result = ResidentPasswordSetter.Resolve(newPassword, hasher, "nova senha");
        if (!result.Succeeded) return result;

        await ApplyPasswordAsync(resident, result.Hash!, revokeAllTokens);
        return result;
    }

    /// <summary>
    /// The lowest-level primitive both <see cref="ApplyNewPasswordAsync"/> (used by
    /// <see cref="ChangePassword"/>) and <see cref="ResetPassword"/> end in: write the new
    /// hash, then revoke every refresh token for this resident. <see cref="ResetPassword"/>
    /// calls this directly (rather than <see cref="ApplyNewPasswordAsync"/>) because it must
    /// validate the new password and only THEN consume the recovery token - by the time this
    /// runs, the password has already been validated and the token already consumed.
    /// </summary>
    internal static async Task ApplyPasswordAsync(ResidentAccessDTO resident, string passwordHash, Func<Task> revokeAllTokens)
    {
        resident.Password = passwordHash;
        await revokeAllTokens();
    }

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
        DateTime now,
        Guid? requestedLicenseId = null)
    {
        var hasStoredPassword = resident is not null && !string.IsNullOrEmpty(resident.Password);
        var storedHash = hasStoredPassword ? resident!.Password : DummyPasswordHash;

        var verification = hasher.VerifyHashedPassword(null!, storedHash, password ?? string.Empty);
        var passwordMatches = verification != PasswordVerificationResult.Failed;

        if (resident is null || !hasStoredPassword || !passwordMatches)
            return ResidentLoginDecision.Failure;

        if (!ResidentAuthorizationService.ResidentCanSignIn(resident, now))
            return ResidentLoginDecision.Failure;

        var licenseId = ResolveLicenseId(resident, requestedLicenseId, now);
        return licenseId is null ? ResidentLoginDecision.Failure : ResidentLoginDecision.Ok(licenseId.Value);
    }

    /// <summary>
    /// Resolves one active licence context from all currently valid unit links. Login uses
    /// the primary unit by default, while an explicit requested licence is accepted only when
    /// the resident has at least one active unit there. This is the authorization boundary
    /// that allows one account to switch condominiums without widening a token's tenant scope.
    /// Legacy seeded residents with only the direct UnitId remain supported.
    /// </summary>
    internal static Guid? ResolveLicenseId(ResidentAccessDTO resident, Guid? requestedLicenseId = null, DateTime? now = null)
    {
        var contexts = ResolveContexts(resident, now ?? DateTime.UtcNow);
        if (requestedLicenseId.HasValue)
            return contexts.Any(x => x.LicenseId == requestedLicenseId.Value) ? requestedLicenseId : null;
        var unit = ResolvePrimaryUnit(resident);
        if (unit?.Block is not null && contexts.Any(x => x.LicenseId == unit.Block.LicenseId))
            return unit.Block.LicenseId;
        return contexts.FirstOrDefault()?.LicenseId;
    }

    internal static List<ResidentContextOut> ResolveContexts(ResidentAccessDTO resident, DateTime now)
    {
        var activeLinks = resident.UnitLinks
            .Where(x => ResidentAuthorizationService.LinkIsCurrentlyValid(x, now) &&
                        x.Unit?.Block is not null && x.Unit.Block.LicenseId != Guid.Empty)
            .ToList();

        if (activeLinks.Count == 0 && resident.Unit?.Block is not null && resident.Unit.Block.LicenseId != Guid.Empty)
        {
            return
            [
                new ResidentContextOut
                {
                    LicenseId = resident.Unit.Block.LicenseId,
                    LicenseName = resident.Unit.Block.License?.Name ?? string.Empty,
                    Units =
                    [
                        new ResidentContextUnitOut
                        {
                            UnitId = resident.Unit.Id, BlockName = resident.Unit.Block.Name,
                            UnitNumber = resident.Unit.Number, Relationship = resident.AccessType.ToString(), IsPrimary = true
                        }
                    ]
                }
            ];
        }

        return activeLinks
            .GroupBy(x => new { x.Unit.Block.LicenseId, Name = x.Unit.Block.License?.Name ?? string.Empty })
            .OrderBy(x => x.Key.Name)
            .Select(group => new ResidentContextOut
            {
                LicenseId = group.Key.LicenseId,
                LicenseName = group.Key.Name,
                Units = group.OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Unit.Block.Name).ThenBy(x => x.Unit.Number)
                    .Select(x => new ResidentContextUnitOut
                    {
                        UnitId = x.UnitId, BlockName = x.Unit.Block.Name, UnitNumber = x.Unit.Number,
                        Relationship = x.Relationship.ToString(), IsPrimary = x.IsPrimary
                    }).ToList()
            }).ToList();
    }

    /// <summary>Same "primary link, else first link" precedent already used by
    /// AccessRouteResolver.ResolveAudience and PeopleManagementController - ordered by
    /// CreatedAt for a deterministic pick if more than one link is (incorrectly) flagged
    /// primary. Falls back to the resident's direct Unit when there are no links at all.</summary>
    internal static UnitDTO? ResolvePrimaryUnit(ResidentAccessDTO resident)
        => ResidentLicenseScope.ResolvePrimaryUnit(resident);
}
