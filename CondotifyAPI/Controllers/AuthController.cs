using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Login;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace DigitalWorldOnline.Management.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly DatabaseContext _context;
    private readonly IMapper _mapper;
    private readonly IJwtTokenService _jwt;
    private readonly IPasswordHasher<UserAccess> _passwordHasher;
    private readonly ITotpService _totp;
    private readonly IRefreshTokenService _refreshTokens;

    public AuthController(DatabaseContext context, IMapper mapper, IJwtTokenService jwt, IPasswordHasher<UserAccess> passwordHasher, ITotpService totp, IRefreshTokenService refreshTokens)
    {
        _context = context;
        _mapper = mapper;
        _jwt = jwt;
        _passwordHasher = passwordHasher;
        _totp = totp;
        _refreshTokens = refreshTokens;
    }

    [HttpGet("validate")]
    [Authorize]
    public IActionResult ValidateToken() => Ok(new { Result = "Valid", User = User.Identity?.Name });

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginIn input, CancellationToken cancellationToken)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(input.Password)) return InvalidCredentials();
        var dto = await _context.Users.FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (dto is null) return InvalidCredentials();
        var user = _mapper.Map<UserAccess>(dto);
        if (!user.VerifyPassword(input.Password, _passwordHasher)) return InvalidCredentials();

        if (dto.MfaEnabled)
        {
            var challenge = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
            dto.MfaChallengeHash = Hash(challenge);
            dto.MfaChallengeExpiresAt = DateTime.UtcNow.AddMinutes(5);
            await _context.SaveChangesAsync();
            return Ok(new LoginOut { Result = "MfaRequired", MfaRequired = true, ChallengeToken = challenge, ExpiresIn = 300 });
        }

        dto.LastAccess = DateTime.UtcNow;
        var refresh = await IssueRefreshTokenAsync(dto.Id, input.DeviceLabel, cancellationToken);
        return Success(user, refresh);
    }

    [HttpPost("mfa/verify")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> VerifyMfa([FromBody] VerifyMfaIn input, CancellationToken cancellationToken)
    {
        var challengeHash = Hash(input.ChallengeToken);
        var dto = await _context.Users.FirstOrDefaultAsync(x => x.MfaEnabled && x.MfaChallengeHash == challengeHash && x.MfaChallengeExpiresAt > DateTime.UtcNow);
        if (dto is null || !ConsumeValidCode(dto, input.Code)) return Unauthorized(new LoginOut { Result = "InvalidMfaCode" });
        dto.MfaChallengeHash = string.Empty;
        dto.MfaChallengeExpiresAt = null;
        dto.LastAccess = DateTime.UtcNow;
        var refresh = await IssueRefreshTokenAsync(dto.Id, input.DeviceLabel, cancellationToken);
        return Success(_mapper.Map<UserAccess>(dto), refresh);
    }

    [HttpGet("mfa/status")]
    [Authorize]
    public async Task<IActionResult> MfaStatus()
    {
        var user = await CurrentUserAsync();
        return user is null ? NotFound() : Ok(new MfaSetupOut { Enabled = user.MfaEnabled });
    }

    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<IActionResult> SetupMfa()
    {
        var user = await CurrentUserAsync();
        if (user is null) return NotFound();
        if (user.MfaEnabled) return Conflict(new { Errors = "A autenticação em duas etapas já está ativa." });
        user.MfaSecret = _totp.GenerateSecret();
        await _context.SaveChangesAsync();
        return Ok(new MfaSetupOut { Secret = user.MfaSecret, ProvisioningUri = _totp.BuildUri(user.MfaSecret, user.Email) });
    }

    [HttpPost("mfa/enable")]
    [Authorize]
    public async Task<IActionResult> EnableMfa([FromBody] MfaCodeIn input)
    {
        var user = await CurrentUserAsync();
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(user.MfaSecret) || !_totp.Verify(user.MfaSecret, input.Code))
            return BadRequest(new { Errors = "O código informado não é válido." });
        var recoveryCodes = Enumerable.Range(0, 8).Select(_ => RecoveryCode()).ToList();
        user.MfaRecoveryCodeHashesJson = JsonSerializer.Serialize(recoveryCodes.Select(x => Hash(NormalizeRecoveryCode(x))));
        user.MfaEnabled = true;
        await _context.SaveChangesAsync();
        return Ok(new MfaSetupOut { Enabled = true, RecoveryCodes = recoveryCodes });
    }

    [HttpPost("mfa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableMfa([FromBody] MfaCodeIn input)
    {
        var user = await CurrentUserAsync();
        if (user is null) return NotFound();
        if (!user.MfaEnabled || !ConsumeValidCode(user, input.Code)) return BadRequest(new { Errors = "Código de segurança inválido." });
        user.MfaEnabled = false;
        user.MfaSecret = string.Empty;
        user.MfaRecoveryCodeHashesJson = "[]";
        user.MfaChallengeHash = string.Empty;
        user.MfaChallengeExpiresAt = null;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("password/change")]
    [Authorize]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordIn input)
    {
        var dto = await CurrentUserAsync();
        if (dto is null) return NotFound();

        var user = _mapper.Map<UserAccess>(dto);
        if (!user.VerifyPassword(input.CurrentPassword, _passwordHasher))
            return BadRequest(new { Errors = "A senha atual está incorreta." });

        var passwordError = PasswordPolicy.Validate(input.NewPassword);
        if (passwordError is not null)
            return BadRequest(new { Errors = passwordError });

        if (user.VerifyPassword(input.NewPassword, _passwordHasher))
            return BadRequest(new { Errors = "A nova senha deve ser diferente da senha atual." });

        user.SetPassword(input.NewPassword, _passwordHasher);
        dto.SetPasswordHash(user.PasswordHash);
        dto.FirstAccess = false;
        dto.MfaChallengeHash = string.Empty;
        dto.MfaChallengeExpiresAt = null;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<UserAccessDTO?> CurrentUserAsync() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? await _context.Users.FirstOrDefaultAsync(x => x.Id == userId)
        : null;

    private bool ConsumeValidCode(UserAccessDTO user, string code)
    {
        if (_totp.Verify(user.MfaSecret, code)) return true;
        var suppliedHash = Hash(NormalizeRecoveryCode(code));
        var hashes = DeserializeHashes(user.MfaRecoveryCodeHashesJson);
        var match = hashes.FirstOrDefault(x => FixedEquals(x, suppliedHash));
        if (match is null) return false;
        hashes.Remove(match);
        user.MfaRecoveryCodeHashesJson = JsonSerializer.Serialize(hashes);
        return true;
    }

    /// <summary>Issues the refresh token half of a new session, alongside whatever change
    /// the caller already staged on the tracked <c>UserAccessDTO</c> (LastAccess, cleared MFA
    /// challenge, ...) - IssueAsync's own SaveChangesAsync flushes both in one round trip,
    /// the same pattern ResidentAuthController.Login uses for the resident side.</summary>
    private async Task<RefreshTokenIssued> IssueRefreshTokenAsync(Guid userId, string? deviceLabelInput, CancellationToken cancellationToken)
    {
        var deviceLabel = SessionController.ResolveDeviceLabel(deviceLabelInput, Request.Headers.UserAgent.ToString());
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
        return await _refreshTokens.IssueAsync(userId, PrincipalTypes.User, deviceLabel, ip, cancellationToken);
    }

    private IActionResult Success(UserAccess user, RefreshTokenIssued refresh) => Ok(new LoginOut
    {
        Result = "Success",
        AccessToken = _jwt.CreateAccessToken(user),
        RefreshToken = refresh.Token,
        ExpiresIn = _jwt.AccessTokenLifetimeSeconds
    });
    private static IActionResult InvalidCredentials() => new UnauthorizedObjectResult(new LoginOut { Result = "InvalidCredentials" });
    private static string RecoveryCode() => $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4))}";
    private static string NormalizeRecoveryCode(string value) => value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));
    private static bool FixedEquals(string left, string right)
    {
        try
        {
            var expected = Convert.FromHexString(left);
            var supplied = Convert.FromHexString(right);
            return expected.Length == supplied.Length &&
                   CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }
    private static List<string> DeserializeHashes(string json) { try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; } catch { return []; } }
}
