using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Security;

/// <summary>The plaintext recovery token, returned to the caller exactly once (to be emailed
/// by <see cref="ResidentAuthController"/> - see <see cref="Controllers.ResidentAuthController"/>).
/// Never persisted - only the row's <c>TokenHash</c> is written to the database.</summary>
public sealed record ResidentPasswordRecoveryIssued(Guid ResidentId, string Token, DateTime ExpiresAt);

public interface IResidentPasswordRecoveryService
{
    /// <summary>
    /// Issues a fresh single-use, 30-minute recovery token for <paramref name="residentId"/>,
    /// invalidating every other outstanding (unused) token that resident already had - see
    /// <see cref="ResidentPasswordRecoveryService.SelectOutstandingTokens"/>. Returns null,
    /// issuing nothing, when a token for this resident was already created inside the last
    /// <see cref="ResidentPasswordRecoveryService.IssueCooldown"/> (defence against an attacker
    /// flooding one resident's inbox by repeatedly calling "forgot password" - see
    /// <see cref="ResidentPasswordRecoveryService.ShouldThrottle"/>). The caller
    /// (<c>ResidentAuthController.ForgotPassword</c>) treats null exactly like a successful,
    /// silent no-op: "forgot" always returns 202 either way.
    /// </summary>
    Task<ResidentPasswordRecoveryIssued?> IssueAsync(Guid residentId, string ip, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes <paramref name="presentedToken"/> if - and only if - it is currently valid
    /// (exists, unused, unexpired - see <see cref="ResidentPasswordRecoveryService.IsValid"/>).
    /// On success, marks it used and returns the resident it belonged to. Returns null for an
    /// unknown, already-used, or expired token - the caller cannot distinguish those cases,
    /// same "one shape for every failure" precedent as <c>ResidentAuthController.Decide</c>.
    /// </summary>
    Task<Guid?> ConsumeAsync(string presentedToken, CancellationToken cancellationToken = default);
}

/// <summary>
/// Password-recovery tokens for residents (task 8 - "forgot password"). Stored only as a
/// SHA-256 hash (<see cref="ResidentPasswordRecoveryTokenDTO.TokenHash"/>), exactly the same
/// scheme <see cref="RefreshTokenService"/> already uses for refresh tokens - this class
/// deliberately reuses <see cref="RefreshTokenService.HashToken"/> and
/// <see cref="RefreshTokenService.GenerateTokenPair"/> (both internal, same assembly) rather
/// than re-implementing the same CSPRNG-token-plus-hash construction a second time; see
/// <see cref="HashToken"/> / <see cref="GenerateTokenPair"/> below.
///
/// CondotifyAPI.Tests has no EF InMemory/SQLite provider, so - same precedent as
/// RefreshTokenService - every rule that matters (validity, which prior tokens a new one
/// supersedes, whether issuance should be throttled) is a pure internal static function this
/// class calls verbatim, exercised directly by ResidentPasswordRecoveryServiceTests. What is
/// NOT covered by an automated test: the actual database round-trip inside IssueAsync/ConsumeAsync.
/// </summary>
public sealed class ResidentPasswordRecoveryService : IResidentPasswordRecoveryService
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Minimum time between two tokens being issued for the same resident. This is
    /// the real defence against inbox flooding (see <see cref="Controllers.ResidentAuthController"/>'s
    /// remarks on why per-IP rate limiting alone does not protect the victim): it is keyed on
    /// the resident being recovered, not on the caller's IP, so it holds regardless of how many
    /// different addresses a request comes from.</summary>
    internal static readonly TimeSpan IssueCooldown = TimeSpan.FromSeconds(60);

    private readonly DatabaseContext _context;

    public ResidentPasswordRecoveryService(DatabaseContext context) => _context = context;

    public async Task<ResidentPasswordRecoveryIssued?> IssueAsync(Guid residentId, string ip, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // Scoped coarsely in SQL (this resident), then the exact throttle/supersede rules are
        // applied in memory via the same predicates ResidentPasswordRecoveryServiceTests
        // exercises directly - same split RefreshTokenService.RevokeAllInternalAsync uses.
        var existing = await _context.ResidentPasswordRecoveryTokens
            .Where(x => x.ResidentId == residentId)
            .ToListAsync(cancellationToken);

        if (ShouldThrottle(existing, now)) return null;

        foreach (var token in SelectOutstandingTokens(existing, residentId))
            token.UsedAt = now;

        var (plainToken, hash) = GenerateTokenPair();
        var entity = BuildIssuedEntity(residentId, ip, hash, now);

        _context.ResidentPasswordRecoveryTokens.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return new ResidentPasswordRecoveryIssued(residentId, plainToken, entity.ExpiresAt);
    }

    public async Task<Guid?> ConsumeAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(presentedToken);
        var existing = await _context.ResidentPasswordRecoveryTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null) return null;

        var now = DateTime.UtcNow;
        if (!IsValid(existing, now)) return null;

        existing.UsedAt = now;
        await _context.SaveChangesAsync(cancellationToken);
        return existing.ResidentId;
    }

    // --- Pure helpers, exercised directly by ResidentPasswordRecoveryServiceTests ------

    /// <summary>Delegates to <see cref="RefreshTokenService.HashToken"/> - same SHA-256, same
    /// hex encoding; there is nothing resident-recovery-specific about hashing a token.</summary>
    internal static string HashToken(string token) => RefreshTokenService.HashToken(token);

    /// <summary>Delegates to <see cref="RefreshTokenService.GenerateTokenPair"/> - same
    /// 256-bit CSPRNG, base64url construction already used for refresh tokens.</summary>
    internal static (string PlainToken, string Hash) GenerateTokenPair() => RefreshTokenService.GenerateTokenPair();

    internal static ResidentPasswordRecoveryTokenDTO BuildIssuedEntity(Guid residentId, string ip, string tokenHash, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        ResidentId = residentId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(Lifetime),
        CreatedIp = ip ?? string.Empty,
    };

    /// <summary>A token is usable exactly while it has never been used AND has not yet
    /// expired. Revocation-by-use is checked independently of expiry (mirrors
    /// RefreshTokenService.Decide's "reuse takes priority" precedent) so a token that is both
    /// used and expired is still correctly rejected, never accidentally treated as merely
    /// "expired" by a caller that only checks one of the two fields.</summary>
    internal static bool IsValid(ResidentPasswordRecoveryTokenDTO token, DateTime now) =>
        token.UsedAt is null && token.ExpiresAt > now;

    /// <summary>Every currently-outstanding (unused) token for this resident - what a fresh
    /// "forgot password" call invalidates before issuing its own, so only the most recent
    /// email's token is ever valid at once.</summary>
    internal static IReadOnlyCollection<ResidentPasswordRecoveryTokenDTO> SelectOutstandingTokens(
        IEnumerable<ResidentPasswordRecoveryTokenDTO> candidates, Guid residentId) =>
        candidates.Where(x => x.ResidentId == residentId && x.UsedAt is null).ToList();

    /// <summary>True when the most recently created token in <paramref name="recentTokensForResident"/>
    /// is younger than <see cref="IssueCooldown"/> - i.e. issuance should be skipped this time.
    /// Looks only at CreatedAt (used or not): a resident who just requested recovery should not
    /// get a second email a moment later even before using the first token.</summary>
    internal static bool ShouldThrottle(IEnumerable<ResidentPasswordRecoveryTokenDTO> recentTokensForResident, DateTime now)
    {
        var last = recentTokensForResident.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return last is not null && now - last.CreatedAt < IssueCooldown;
    }
}
