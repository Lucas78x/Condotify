using System.Security.Cryptography;
using System.Text;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CondotifyAPI.Services.Security;

/// <summary>The plaintext token, returned to the caller exactly once. Never persisted -
/// only <see cref="RefreshTokenDTO.TokenHash"/> is written to the database.</summary>
public sealed record RefreshTokenIssued(Guid Id, string Token, DateTime ExpiresAt);

/// <summary>Session summary for "list my devices" (GET /api/auth/sessions, task 7).
/// Deliberately excludes the token hash - callers never need it and it should not
/// leave this service any more than the plaintext token does.</summary>
public sealed record RefreshTokenSummary(
    Guid Id,
    string DeviceLabel,
    string CreatedIp,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt);

public interface IRefreshTokenService
{
    Task<RefreshTokenIssued> IssueAsync(Guid subjectId, string subjectType, string deviceLabel, string ip, CancellationToken cancellationToken = default, Guid? contextLicenseId = null);

    /// <summary>
    /// Consumes <paramref name="presentedToken"/>. On success, revokes it and returns a
    /// freshly issued replacement linked via <see cref="RefreshTokenDTO.ReplacedByHash"/>.
    /// Returns null when the token is unknown, expired, or already revoked. Presenting an
    /// already-revoked token is treated as reuse/theft: every token for that subject is
    /// revoked as a side effect before returning null - see <see cref="RefreshTokenService"/>.
    /// </summary>
    Task<RefreshTokenIssued?> RotateAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Rotates an authenticated resident session while replacing its active
    /// licence context. The presented token must belong to the authenticated subject.</summary>
    Task<RefreshTokenIssued?> RotateContextAsync(
        string presentedToken,
        Guid subjectId,
        string subjectType,
        Guid contextLicenseId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes the presented token (e.g. logout). A no-op if the token is unknown
    /// or already revoked - logout is idempotent, not an oracle for token validity.</summary>
    Task RevokeAsync(string presentedToken, CancellationToken cancellationToken = default);

    /// <summary>Revokes every currently-active token for this subject ("log out everywhere").
    /// Touches only rows matching both <paramref name="subjectId"/> and <paramref name="subjectType"/>.</summary>
    Task RevokeAllAsync(Guid subjectId, string subjectType, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<RefreshTokenSummary>> ListAsync(Guid subjectId, string subjectType, CancellationToken cancellationToken = default);
}

/// <summary>
/// Refresh tokens for both staff and resident principals (<see cref="Jwt.PrincipalTypes"/>),
/// backing the 1-hour access token introduced in task 2: without this, every session -
/// staff or resident, web or mobile - would be dropped hourly.
///
/// The database only ever sees <see cref="RefreshTokenDTO.TokenHash"/> - a SHA-256 hash,
/// same pattern as <c>RegistrationInviteDTO.TokenHash</c>/<c>PublicRegistrationController
/// .FindInviteAsync</c>. The plaintext value is generated, returned once by IssueAsync or
/// RotateAsync, and then discarded; it is never logged.
///
/// The rule that matters most is reuse detection: a revoked token being presented again
/// means either a replay of an intercepted request, or a stolen token being used after the
/// legitimate client already rotated past it. Either way, every session for that subject is
/// ended (RotateAsync -&gt; <see cref="Decide"/> returns <see cref="RotationDecision.RejectReuse"/>
/// -&gt; the entire chain is revoked via the same selection <see cref="SelectTokensToRevoke"/>
/// that backs RevokeAllAsync).
///
/// CondotifyAPI.Tests has no EF InMemory/SQLite provider, so the decision logic below is
/// exposed as internal static pure functions (via the existing InternalsVisibleTo) that this
/// class calls verbatim - nothing here re-implements a rule that isn't exercised directly by
/// RefreshTokenServiceTests. What is NOT covered by an automated test is the database
/// round-trip itself (the EF queries/SaveChangesAsync calls below).
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    internal static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);

    internal enum RotationDecision
    {
        Allow,
        RejectExpired,
        RejectReuse,
    }

    private readonly DatabaseContext _context;

    public RefreshTokenService(DatabaseContext context) => _context = context;

    public async Task<RefreshTokenIssued> IssueAsync(Guid subjectId, string subjectType, string deviceLabel, string ip, CancellationToken cancellationToken = default, Guid? contextLicenseId = null)
    {
        var (plainToken, hash) = GenerateTokenPair();
        var entity = BuildIssuedEntity(subjectId, subjectType, deviceLabel, ip, hash, DateTime.UtcNow, contextLicenseId);

        _context.RefreshTokens.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenIssued(entity.Id, plainToken, entity.ExpiresAt);
    }

    public async Task<RefreshTokenIssued?> RotateAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(presentedToken);
        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null) return null;

        var now = DateTime.UtcNow;
        var decision = Decide(existing, now);

        if (decision == RotationDecision.RejectReuse)
        {
            // The legitimate client already moved past this token (or it was stolen and
            // used after logout/rotation). Either way, trust in every session for this
            // subject is gone - end all of them, not just this one.
            await RevokeAllInternalAsync(existing.SubjectId, existing.SubjectType, now, cancellationToken);
            return null;
        }

        if (decision == RotationDecision.RejectExpired) return null;

        var (plainToken, newHash) = GenerateTokenPair();
        var replacement = BuildRotatedEntity(existing, newHash, now);
        ApplyRotation(existing, replacement, now);

        _context.RefreshTokens.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenIssued(replacement.Id, plainToken, replacement.ExpiresAt);
    }

    public async Task<RefreshTokenIssued?> RotateContextAsync(
        string presentedToken,
        Guid subjectId,
        string subjectType,
        Guid contextLicenseId,
        CancellationToken cancellationToken = default)
    {
        var hash = HashToken(presentedToken);
        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null || existing.SubjectId != subjectId || existing.SubjectType != subjectType)
            return null;

        var now = DateTime.UtcNow;
        var decision = Decide(existing, now);
        if (decision == RotationDecision.RejectReuse)
        {
            await RevokeAllInternalAsync(existing.SubjectId, existing.SubjectType, now, cancellationToken);
            return null;
        }
        if (decision == RotationDecision.RejectExpired) return null;

        var (plainToken, newHash) = GenerateTokenPair();
        var replacement = BuildContextSwitchedEntity(existing, newHash, now, contextLicenseId);
        ApplyRotation(existing, replacement, now);
        _context.RefreshTokens.Add(replacement);
        await _context.SaveChangesAsync(cancellationToken);
        return new RefreshTokenIssued(replacement.Id, plainToken, replacement.ExpiresAt);
    }

    public async Task RevokeAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        var hash = HashToken(presentedToken);
        var existing = await _context.RefreshTokens.FirstOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null) return;

        if (TryRevoke(existing, DateTime.UtcNow))
            await _context.SaveChangesAsync(cancellationToken);
    }

    public Task RevokeAllAsync(Guid subjectId, string subjectType, CancellationToken cancellationToken = default) =>
        RevokeAllInternalAsync(subjectId, subjectType, DateTime.UtcNow, cancellationToken);

    public async Task<IReadOnlyCollection<RefreshTokenSummary>> ListAsync(Guid subjectId, string subjectType, CancellationToken cancellationToken = default)
    {
        var tokens = await _context.RefreshTokens.AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.SubjectType == subjectType)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return tokens.Select(ToSummary).ToList();
    }

    private async Task RevokeAllInternalAsync(Guid subjectId, string subjectType, DateTime now, CancellationToken cancellationToken)
    {
        // Scoped coarsely in SQL (this subject), then the exact "still active" refinement
        // is the same in-memory predicate RefreshTokenServiceTests exercises directly -
        // see ResidentAuthorizationService for the precedent of this split in this codebase.
        var candidates = await _context.RefreshTokens
            .Where(x => x.SubjectId == subjectId && x.SubjectType == subjectType)
            .ToListAsync(cancellationToken);

        foreach (var token in SelectTokensToRevoke(candidates, subjectId, subjectType))
            token.RevokedAt = now;

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static RefreshTokenSummary ToSummary(RefreshTokenDTO token) => new(
        token.Id, token.DeviceLabel, token.CreatedIp, token.CreatedAt, token.ExpiresAt, token.RevokedAt);

    // --- Pure helpers, exercised directly by RefreshTokenServiceTests -----------------

    /// <summary>SHA-256 of the token, hex-encoded - identical scheme to
    /// <c>PublicRegistrationController.FindInviteAsync</c>'s hashing of invite tokens.</summary>
    internal static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));

    /// <summary>256 bits from a CSPRNG, base64url-encoded - same construction already used
    /// for the MFA challenge token in <c>AuthController.Login</c>.</summary>
    internal static (string PlainToken, string Hash) GenerateTokenPair()
    {
        var plainToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (plainToken, HashToken(plainToken));
    }

    internal static RefreshTokenDTO BuildIssuedEntity(Guid subjectId, string subjectType, string deviceLabel, string ip, string tokenHash, DateTime now, Guid? contextLicenseId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        SubjectType = subjectType,
        ContextLicenseId = contextLicenseId,
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now.Add(Lifetime),
        DeviceLabel = deviceLabel ?? string.Empty,
        CreatedIp = ip ?? string.Empty,
    };

    internal static RefreshTokenDTO BuildRotatedEntity(RefreshTokenDTO previous, string newHash, DateTime now) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = previous.SubjectId,
        SubjectType = previous.SubjectType,
        ContextLicenseId = previous.ContextLicenseId,
        TokenHash = newHash,
        CreatedAt = now,
        ExpiresAt = now.Add(Lifetime),
        DeviceLabel = previous.DeviceLabel,
        CreatedIp = previous.CreatedIp,
    };

    internal static RefreshTokenDTO BuildContextSwitchedEntity(
        RefreshTokenDTO previous,
        string newHash,
        DateTime now,
        Guid contextLicenseId)
    {
        var replacement = BuildRotatedEntity(previous, newHash, now);
        replacement.ContextLicenseId = contextLicenseId;
        return replacement;
    }

    /// <summary>Links the rotated-out token to its replacement and revokes it. After this
    /// call, <see cref="Decide"/> on <paramref name="previous"/> returns RejectReuse -
    /// presenting it again is now indistinguishable from theft, by design.</summary>
    internal static void ApplyRotation(RefreshTokenDTO previous, RefreshTokenDTO replacement, DateTime now)
    {
        previous.RevokedAt = now;
        previous.ReplacedByHash = replacement.TokenHash;
    }

    /// <summary>
    /// Decides what presenting this token for rotation means. Revocation is checked before
    /// expiry deliberately: a token that is both revoked and expired is still a reuse signal,
    /// not "just" an expired one - an attacker replaying an old, revoked, long-expired token
    /// must still trigger revoking the whole chain.
    /// </summary>
    internal static RotationDecision Decide(RefreshTokenDTO token, DateTime now)
    {
        if (token.RevokedAt is not null) return RotationDecision.RejectReuse;
        if (token.ExpiresAt <= now) return RotationDecision.RejectExpired;
        return RotationDecision.Allow;
    }

    /// <summary>
    /// The reuse-detection "revoke the entire chain" rule and RevokeAllAsync's core are the
    /// same operation: every currently-active (not yet revoked) token belonging to exactly
    /// this subject (id AND type). Takes the full candidate list rather than assuming it was
    /// already scoped, so the "touches only this subject" guarantee is tested here directly
    /// rather than trusted from the caller's SQL Where(...).
    /// </summary>
    internal static IReadOnlyCollection<RefreshTokenDTO> SelectTokensToRevoke(
        IEnumerable<RefreshTokenDTO> candidates, Guid subjectId, string subjectType) =>
        candidates
            .Where(x => x.SubjectId == subjectId && x.SubjectType == subjectType && x.RevokedAt is null)
            .ToList();

    /// <summary>Revokes a single token. Returns false (no-op) if it was already revoked -
    /// logout must be idempotent and must not become a way to probe token validity.</summary>
    internal static bool TryRevoke(RefreshTokenDTO token, DateTime now)
    {
        if (token.RevokedAt is not null) return false;
        token.RevokedAt = now;
        return true;
    }
}
