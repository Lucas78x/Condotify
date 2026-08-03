using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Services.Security;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Covers the pure decision logic behind "forgot password" (task 8): single-use, 30-minute
/// recovery tokens for residents. CondotifyAPI.Tests has no EF InMemory/SQLite provider (see
/// RefreshTokenServiceTests for the identical constraint on the sibling refresh-token table),
/// so - following the same precedent - the rules that matter (is this token still usable? has
/// a fresher request already superseded it? should issuance be throttled?) are extracted into
/// internal static functions the EF-backed <see cref="ResidentPasswordRecoveryService"/> calls
/// verbatim. What is NOT covered here: the actual database round-trip inside IssueAsync /
/// ConsumeAsync.
/// </summary>
public class ResidentPasswordRecoveryServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ResidentPasswordRecoveryTokenDTO Token(
        Guid? residentId = null,
        string? tokenHash = null,
        DateTime? createdAt = null,
        DateTime? expiresAt = null,
        DateTime? usedAt = null) => new()
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId ?? Guid.NewGuid(),
            TokenHash = tokenHash ?? ResidentPasswordRecoveryService.HashToken(Guid.NewGuid().ToString()),
            CreatedAt = createdAt ?? Now.AddMinutes(-5),
            ExpiresAt = expiresAt ?? Now.AddMinutes(25),
            UsedAt = usedAt,
        };

    // --- BuildIssuedEntity (hash-only storage, 30-minute validity) --------------------

    [Fact]
    public void BuildIssuedEntity_TokenHashColumn_NeverContainsThePlainTextToken()
    {
        var (plain, hash) = ResidentPasswordRecoveryService.GenerateTokenPair();
        var residentId = Guid.NewGuid();

        var entity = ResidentPasswordRecoveryService.BuildIssuedEntity(residentId, "198.51.100.4", hash, Now);

        Assert.Equal(hash, entity.TokenHash);
        Assert.NotEqual(plain, entity.TokenHash);
        Assert.DoesNotContain(plain, entity.TokenHash);
    }

    [Fact]
    public void BuildIssuedEntity_SetsThirtyMinuteExpiry()
    {
        var entity = ResidentPasswordRecoveryService.BuildIssuedEntity(Guid.NewGuid(), "127.0.0.1", "hash", Now);

        Assert.Equal(Now.AddMinutes(30), entity.ExpiresAt);
        Assert.Equal(Now, entity.CreatedAt);
        Assert.Null(entity.UsedAt);
    }

    // --- IsValid (single use + expiry) -------------------------------------------------

    [Fact]
    public void IsValid_FreshUnusedToken_ReturnsTrue()
    {
        var token = Token(expiresAt: Now.AddMinutes(10));

        Assert.True(ResidentPasswordRecoveryService.IsValid(token, Now));
    }

    [Fact]
    public void IsValid_AlreadyUsedToken_ReturnsFalse()
    {
        // The property that matters most: a token used once can never be used again.
        var token = Token(usedAt: Now.AddMinutes(-1));

        Assert.False(ResidentPasswordRecoveryService.IsValid(token, Now));
    }

    [Fact]
    public void IsValid_ExpiredToken_ReturnsFalse()
    {
        var token = Token(expiresAt: Now.AddMinutes(-1));

        Assert.False(ResidentPasswordRecoveryService.IsValid(token, Now));
    }

    [Fact]
    public void IsValid_ExpiresAtEqualToNow_ReturnsFalse()
    {
        var token = Token(expiresAt: Now);

        Assert.False(ResidentPasswordRecoveryService.IsValid(token, Now));
    }

    [Fact]
    public void IsValid_UsedAndExpiredToken_StillReturnsFalse()
    {
        var token = Token(expiresAt: Now.AddMinutes(-40), usedAt: Now.AddMinutes(-35));

        Assert.False(ResidentPasswordRecoveryService.IsValid(token, Now));
    }

    // --- SelectOutstandingTokens (a new forgot supersedes every prior unused token) ----

    [Fact]
    public void SelectOutstandingTokens_ReturnsOnlyUnusedTokensForThisResident()
    {
        var residentA = Guid.NewGuid();
        var residentB = Guid.NewGuid();

        var outstandingA1 = Token(residentId: residentA);
        var outstandingA2 = Token(residentId: residentA);
        var usedA = Token(residentId: residentA, usedAt: Now.AddMinutes(-1));
        var outstandingB = Token(residentId: residentB);

        var all = new[] { outstandingA1, outstandingA2, usedA, outstandingB };

        var outstanding = ResidentPasswordRecoveryService.SelectOutstandingTokens(all, residentA);

        Assert.Equal(2, outstanding.Count);
        Assert.Contains(outstandingA1, outstanding);
        Assert.Contains(outstandingA2, outstanding);
        Assert.DoesNotContain(usedA, outstanding);
        Assert.DoesNotContain(outstandingB, outstanding);
    }

    [Fact]
    public void SelectOutstandingTokens_NothingOutstanding_ReturnsEmpty()
    {
        var resident = Guid.NewGuid();
        var all = new[] { Token(residentId: resident, usedAt: Now.AddHours(-1)) };

        Assert.Empty(ResidentPasswordRecoveryService.SelectOutstandingTokens(all, resident));
    }

    // --- ShouldThrottle (defense against flooding one resident's inbox) ---------------

    [Fact]
    public void ShouldThrottle_NoPriorToken_ReturnsFalse()
    {
        Assert.False(ResidentPasswordRecoveryService.ShouldThrottle(Array.Empty<ResidentPasswordRecoveryTokenDTO>(), Now));
    }

    [Fact]
    public void ShouldThrottle_LastTokenIssuedJustNow_ReturnsTrue()
    {
        var recent = new[] { Token(createdAt: Now.AddSeconds(-5)) };

        Assert.True(ResidentPasswordRecoveryService.ShouldThrottle(recent, Now));
    }

    [Fact]
    public void ShouldThrottle_LastTokenIssuedOutsideCooldown_ReturnsFalse()
    {
        var older = new[] { Token(createdAt: Now.AddMinutes(-2)) };

        Assert.False(ResidentPasswordRecoveryService.ShouldThrottle(older, Now));
    }

    [Fact]
    public void ShouldThrottle_ConsidersOnlyTheMostRecentToken()
    {
        var mixed = new[]
        {
            Token(createdAt: Now.AddMinutes(-10)),
            Token(createdAt: Now.AddSeconds(-2)),
            Token(createdAt: Now.AddMinutes(-5)),
        };

        Assert.True(ResidentPasswordRecoveryService.ShouldThrottle(mixed, Now));
    }

    // --- HashToken / GenerateTokenPair (delegates to RefreshTokenService's scheme) ----

    [Fact]
    public void HashToken_SameInput_ProducesSameHash()
    {
        Assert.Equal(
            ResidentPasswordRecoveryService.HashToken("same-value"),
            ResidentPasswordRecoveryService.HashToken("same-value"));
    }

    [Fact]
    public void GenerateTokenPair_HashMatchesHashOfPlainToken()
    {
        var (plain, hash) = ResidentPasswordRecoveryService.GenerateTokenPair();

        Assert.Equal(ResidentPasswordRecoveryService.HashToken(plain), hash);
    }
}
