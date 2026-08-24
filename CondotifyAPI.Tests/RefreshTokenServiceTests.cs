using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Security;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// CondotifyAPI.Tests has no EF InMemory/SQLite provider (see DatabaseModelTests, whose one
/// UseNpgsql context never connects - the same fact Task 3's ResidentAuthorizationServiceTests
/// documents). RefreshTokenService's rules - hashing, expiry, rotation, and the reuse-detection
/// chain revocation - are therefore extracted into pure, internal static functions (visible here
/// via the existing InternalsVisibleTo in CondotifyAPI/Properties/AssemblyInfo.cs) that the
/// EF-backed async methods call verbatim, so there is nothing left to drift between what is
/// tested here and what runs against Postgres.
///
/// What is genuinely NOT covered by an automated test: the actual database round-trip inside
/// RotateAsync/RevokeAllAsync/ListAsync (querying Postgres, persisting the revocation). There is
/// no test provider available in this project to exercise that glue code, and adding one (EF
/// InMemory/SQLite) was out of scope for this task. The decision logic that reuse detection and
/// rotation depend on is covered below; the DB plumbing that carries it out is not.
/// </summary>
public class RefreshTokenServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static RefreshTokenDTO Token(
        Guid? subjectId = null,
        string subjectType = PrincipalTypes.Resident,
        string? tokenHash = null,
        DateTime? expiresAt = null,
        DateTime? revokedAt = null,
        string? replacedByHash = null) => new()
        {
            Id = Guid.NewGuid(),
            SubjectId = subjectId ?? Guid.NewGuid(),
            SubjectType = subjectType,
            TokenHash = tokenHash ?? RefreshTokenService.HashToken(Guid.NewGuid().ToString()),
            CreatedAt = Now.AddDays(-1),
            ExpiresAt = expiresAt ?? Now.AddDays(59),
            RevokedAt = revokedAt,
            ReplacedByHash = replacedByHash,
            DeviceLabel = "iPhone de Lucas",
            CreatedIp = "203.0.113.7",
        };

    // --- HashToken ------------------------------------------------------------------

    [Fact]
    public void HashToken_SameInput_ProducesSameHash()
    {
        var a = RefreshTokenService.HashToken("same-value");
        var b = RefreshTokenService.HashToken("same-value");

        Assert.Equal(a, b);
    }

    [Fact]
    public void HashToken_DifferentInput_ProducesDifferentHash()
    {
        var a = RefreshTokenService.HashToken("value-one");
        var b = RefreshTokenService.HashToken("value-two");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashToken_ProducesSixtyFourCharacterHex()
    {
        var hash = RefreshTokenService.HashToken("whatever");

        Assert.Equal(64, hash.Length);
        Assert.True(hash.All(Uri.IsHexDigit));
    }

    // --- GenerateTokenPair ------------------------------------------------------------

    [Fact]
    public void GenerateTokenPair_ProducesDifferentTokensEachCall()
    {
        var (tokenA, _) = RefreshTokenService.GenerateTokenPair();
        var (tokenB, _) = RefreshTokenService.GenerateTokenPair();

        Assert.NotEqual(tokenA, tokenB);
    }

    [Fact]
    public void GenerateTokenPair_HashMatchesHashOfPlainToken()
    {
        var (plain, hash) = RefreshTokenService.GenerateTokenPair();

        Assert.Equal(RefreshTokenService.HashToken(plain), hash);
    }

    [Fact]
    public void GenerateTokenPair_TokenIsUrlSafeWithNoPadding()
    {
        var (plain, _) = RefreshTokenService.GenerateTokenPair();

        Assert.DoesNotContain('+', plain);
        Assert.DoesNotContain('/', plain);
        Assert.DoesNotContain('=', plain);
    }

    // --- BuildIssuedEntity (emissao devolve token que valida / hash-only storage) -----

    [Fact]
    public void BuildIssuedEntity_TokenHashColumn_NeverContainsThePlainTextToken()
    {
        var (plain, hash) = RefreshTokenService.GenerateTokenPair();
        var subjectId = Guid.NewGuid();

        var entity = RefreshTokenService.BuildIssuedEntity(subjectId, PrincipalTypes.Resident, "Pixel 8", "198.51.100.4", hash, Now);

        Assert.Equal(hash, entity.TokenHash);
        Assert.NotEqual(plain, entity.TokenHash);
        Assert.DoesNotContain(plain, entity.TokenHash);
    }

    [Fact]
    public void BuildIssuedEntity_TheReturnedTokenValidatesAgainstTheStoredHash()
    {
        // Simulates the full issue: generate, build the row that would be persisted,
        // then confirm the value that would be handed to the caller hashes to exactly
        // what ended up in the TokenHash column.
        var (plain, hash) = RefreshTokenService.GenerateTokenPair();
        var entity = RefreshTokenService.BuildIssuedEntity(Guid.NewGuid(), PrincipalTypes.User, "Web", "127.0.0.1", hash, Now);

        Assert.Equal(entity.TokenHash, RefreshTokenService.HashToken(plain));
    }

    [Fact]
    public void BuildIssuedEntity_SetsSixtyDayExpiry()
    {
        var entity = RefreshTokenService.BuildIssuedEntity(Guid.NewGuid(), PrincipalTypes.Resident, "", "", "hash", Now);

        Assert.Equal(Now.AddDays(60), entity.ExpiresAt);
        Assert.Equal(Now, entity.CreatedAt);
        Assert.Null(entity.RevokedAt);
    }

    [Fact]
    public void BuildIssuedAndRotatedEntity_PreserveResidentLicenseContext()
    {
        var licenseId = Guid.NewGuid();
        var issued = RefreshTokenService.BuildIssuedEntity(
            Guid.NewGuid(), PrincipalTypes.Resident, "Android", "127.0.0.1", "hash", Now, licenseId);

        var rotated = RefreshTokenService.BuildRotatedEntity(issued, "replacement", Now.AddMinutes(1));

        Assert.Equal(licenseId, issued.ContextLicenseId);
        Assert.Equal(licenseId, rotated.ContextLicenseId);
    }

    [Fact]
    public void BuildContextSwitchedEntity_ReplacesLicenseAndKeepsTheSameSubject()
    {
        var previous = RefreshTokenService.BuildIssuedEntity(
            Guid.NewGuid(), PrincipalTypes.Resident, "Android", "127.0.0.1", "hash", Now, Guid.NewGuid());
        var nextLicense = Guid.NewGuid();

        var replacement = RefreshTokenService.BuildContextSwitchedEntity(previous, "replacement", Now.AddMinutes(1), nextLicense);

        Assert.Equal(previous.SubjectId, replacement.SubjectId);
        Assert.Equal(previous.SubjectType, replacement.SubjectType);
        Assert.Equal(nextLicense, replacement.ContextLicenseId);
        Assert.Equal("replacement", replacement.TokenHash);
    }

    // --- Decide (expiry / revocation / reuse-detection priority) ----------------------

    [Fact]
    public void Decide_ActiveUnexpiredToken_ReturnsAllow()
    {
        var token = Token(expiresAt: Now.AddDays(1));

        Assert.Equal(RefreshTokenService.RotationDecision.Allow, RefreshTokenService.Decide(token, Now));
    }

    [Fact]
    public void Decide_ExpiredToken_ReturnsRejectExpired()
    {
        var token = Token(expiresAt: Now.AddMinutes(-1));

        Assert.Equal(RefreshTokenService.RotationDecision.RejectExpired, RefreshTokenService.Decide(token, Now));
    }

    [Fact]
    public void Decide_ExpiresAtEqualToNow_ReturnsRejectExpired()
    {
        var token = Token(expiresAt: Now);

        Assert.Equal(RefreshTokenService.RotationDecision.RejectExpired, RefreshTokenService.Decide(token, Now));
    }

    [Fact]
    public void Decide_RevokedToken_ReturnsRejectReuse()
    {
        var token = Token(expiresAt: Now.AddDays(1), revokedAt: Now.AddMinutes(-5));

        Assert.Equal(RefreshTokenService.RotationDecision.RejectReuse, RefreshTokenService.Decide(token, Now));
    }

    [Fact]
    public void Decide_RevokedAndExpiredToken_ReuseTakesPriorityOverExpiry()
    {
        // A stolen token that has both been rotated past (revoked) AND aged out
        // (expired) must still be treated as a reuse/theft signal, not silently
        // dismissed as "just expired".
        var token = Token(expiresAt: Now.AddDays(-30), revokedAt: Now.AddDays(-29));

        Assert.Equal(RefreshTokenService.RotationDecision.RejectReuse, RefreshTokenService.Decide(token, Now));
    }

    // --- ApplyRotation (rotacao invalida o token anterior) ----------------------------

    [Fact]
    public void ApplyRotation_InvalidatesThePreviousToken()
    {
        var previous = Token(expiresAt: Now.AddDays(1));
        var (_, newHash) = RefreshTokenService.GenerateTokenPair();
        var replacement = RefreshTokenService.BuildRotatedEntity(previous, newHash, Now);

        RefreshTokenService.ApplyRotation(previous, replacement, Now);

        Assert.Equal(Now, previous.RevokedAt);
        Assert.Equal(newHash, previous.ReplacedByHash);
        // The previous token is no longer usable for a further rotation: presenting it
        // again after this point must now be treated as reuse.
        Assert.Equal(RefreshTokenService.RotationDecision.RejectReuse, RefreshTokenService.Decide(previous, Now));
    }

    [Fact]
    public void BuildRotatedEntity_CarriesSubjectAndDeviceInfoForwardWithFreshExpiry()
    {
        var previous = Token(expiresAt: Now.AddDays(1));
        var (_, newHash) = RefreshTokenService.GenerateTokenPair();

        var replacement = RefreshTokenService.BuildRotatedEntity(previous, newHash, Now);

        Assert.Equal(previous.SubjectId, replacement.SubjectId);
        Assert.Equal(previous.SubjectType, replacement.SubjectType);
        Assert.Equal(previous.DeviceLabel, replacement.DeviceLabel);
        Assert.Equal(previous.CreatedIp, replacement.CreatedIp);
        Assert.Equal(newHash, replacement.TokenHash);
        Assert.Equal(Now.AddDays(60), replacement.ExpiresAt);
        Assert.NotEqual(previous.Id, replacement.Id);
    }

    // --- SelectTokensToRevoke (reuse detection revokes the WHOLE chain; RevokeAllAsync) -

    [Fact]
    public void SelectTokensToRevoke_RevokesEveryActiveTokenOfTheSubject_AndLeavesOtherSubjectsAlone()
    {
        var subjectA = Guid.NewGuid();
        var subjectB = Guid.NewGuid();

        var activeA1 = Token(subjectId: subjectA, subjectType: PrincipalTypes.Resident);
        var activeA2 = Token(subjectId: subjectA, subjectType: PrincipalTypes.Resident);
        var alreadyRevokedA = Token(subjectId: subjectA, subjectType: PrincipalTypes.Resident, revokedAt: Now.AddDays(-1));
        var activeB = Token(subjectId: subjectB, subjectType: PrincipalTypes.Resident);
        // Same subject id, different type (e.g. a staff user reusing a resident's guid
        // is not realistic, but the selector must key on both fields regardless).
        var sameIdDifferentType = Token(subjectId: subjectA, subjectType: PrincipalTypes.User);

        var all = new[] { activeA1, activeA2, alreadyRevokedA, activeB, sameIdDifferentType };

        var toRevoke = RefreshTokenService.SelectTokensToRevoke(all, subjectA, PrincipalTypes.Resident);

        Assert.Equal(2, toRevoke.Count);
        Assert.Contains(activeA1, toRevoke);
        Assert.Contains(activeA2, toRevoke);
        Assert.DoesNotContain(alreadyRevokedA, toRevoke);
        Assert.DoesNotContain(activeB, toRevoke);
        Assert.DoesNotContain(sameIdDifferentType, toRevoke);
    }

    [Fact]
    public void SelectTokensToRevoke_SubjectWithNothingActive_ReturnsEmpty()
    {
        var subject = Guid.NewGuid();
        var all = new[] { Token(subjectId: subject, revokedAt: Now.AddHours(-1)) };

        var toRevoke = RefreshTokenService.SelectTokensToRevoke(all, subject, PrincipalTypes.Resident);

        Assert.Empty(toRevoke);
    }

    // --- TryRevoke (logout revokes the presented token) -------------------------------

    [Fact]
    public void TryRevoke_ActiveToken_SetsRevokedAtAndReturnsTrue()
    {
        var token = Token();

        var result = RefreshTokenService.TryRevoke(token, Now);

        Assert.True(result);
        Assert.Equal(Now, token.RevokedAt);
    }

    [Fact]
    public void TryRevoke_AlreadyRevokedToken_IsNoOpAndReturnsFalse()
    {
        var originalRevokedAt = Now.AddDays(-3);
        var token = Token(revokedAt: originalRevokedAt);

        var result = RefreshTokenService.TryRevoke(token, Now);

        Assert.False(result);
        Assert.Equal(originalRevokedAt, token.RevokedAt);
    }
}
