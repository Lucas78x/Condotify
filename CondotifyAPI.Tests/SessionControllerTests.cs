using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Task 7 (session: refresh, logout, devices). CondotifyAPI.Tests has no EF InMemory/SQLite
/// provider (same constraint as RefreshTokenServiceTests/ResidentLoginTests), so every rule
/// SessionController depends on is extracted into pure, internal static functions exercised
/// directly here - no DbContext, no HTTP pipeline.
///
/// The property that matters most, per the task 7 brief: a refresh token issued to a
/// resident (SubjectType = "resident") must resolve to <see cref="SessionController.AccessTokenKind.Resident"/>
/// and NEVER to <see cref="SessionController.AccessTokenKind.User"/> - the single worst
/// outcome in this sub-project would be a resident's refresh token minting a staff access
/// token. ResolveAccessTokenKind_ThenMint_* below closes the loop end-to-end: it feeds the
/// resolved kind into the real <see cref="JwtTokenService"/> (not a mock) and reads back the
/// signed JWT's principal_type claim, exactly like JwtPrincipalTypeTests does for login.
/// </summary>
public class SessionControllerTests
{
    private static readonly Guid LicenseId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly PasswordHasher<UserAccess> Hasher = new();

    // --- ResolveAccessTokenKind (the fork that must never grant staff access to a resident) --

    [Fact]
    public void ResolveAccessTokenKind_UserSubjectType_ResolvesToUser()
    {
        Assert.Equal(SessionController.AccessTokenKind.User, SessionController.ResolveAccessTokenKind(PrincipalTypes.User));
    }

    [Fact]
    public void ResolveAccessTokenKind_ResidentSubjectType_ResolvesToResident()
    {
        Assert.Equal(SessionController.AccessTokenKind.Resident, SessionController.ResolveAccessTokenKind(PrincipalTypes.Resident));
    }

    [Theory]
    [InlineData("")]
    [InlineData("USER")]
    [InlineData("Resident")]
    [InlineData("admin")]
    [InlineData("staff")]
    [InlineData(" user")]
    public void ResolveAccessTokenKind_UnrecognisedSubjectType_ResolvesToNull_NeverToUser(string subjectType)
    {
        var kind = SessionController.ResolveAccessTokenKind(subjectType);

        Assert.Null(kind);
        // Spelled out explicitly: an unrecognised value must never silently become the
        // higher-privileged branch. Null (rejected) is the only safe outcome.
        Assert.NotEqual(SessionController.AccessTokenKind.User, kind);
    }

    [Fact]
    public void ResolveAccessTokenKind_NullSubjectType_ResolvesToNull()
    {
        Assert.Null(SessionController.ResolveAccessTokenKind(null!));
    }

    // --- End-to-end (minus the DB): resolved kind drives the real JwtTokenService ------------

    [Fact]
    public void ResidentSubjectType_EndToEnd_MintsATokenCarryingPrincipalTypeResident_NeverUser()
    {
        var kind = SessionController.ResolveAccessTokenKind(PrincipalTypes.Resident);
        Assert.Equal(SessionController.AccessTokenKind.Resident, kind);

        // Exactly the branch SessionController.Refresh takes when kind == Resident.
        var jwt = kind == SessionController.AccessTokenKind.User
            ? CreateJwtService().CreateAccessToken(SampleUser())
            : CreateJwtService().CreateResidentAccessToken(SampleResident(), LicenseId);

        var claim = ReadToken(jwt).Claims.First(x => x.Type == PrincipalTypes.Claim).Value;

        Assert.Equal(PrincipalTypes.Resident, claim);
        Assert.NotEqual(PrincipalTypes.User, claim);
    }

    [Fact]
    public void UserSubjectType_EndToEnd_MintsATokenCarryingPrincipalTypeUser_NeverResident()
    {
        var kind = SessionController.ResolveAccessTokenKind(PrincipalTypes.User);
        Assert.Equal(SessionController.AccessTokenKind.User, kind);

        var jwt = kind == SessionController.AccessTokenKind.User
            ? CreateJwtService().CreateAccessToken(SampleUser())
            : CreateJwtService().CreateResidentAccessToken(SampleResident(), LicenseId);

        var claim = ReadToken(jwt).Claims.First(x => x.Type == PrincipalTypes.Claim).Value;

        Assert.Equal(PrincipalTypes.User, claim);
        Assert.NotEqual(PrincipalTypes.Resident, claim);
    }

    // --- ResolveSubject (who is calling - derived from claims, never a parameter) -----------

    [Fact]
    public void ResolveSubject_UserPrincipal_ReturnsItsOwnIdAndType()
    {
        var id = Guid.NewGuid();
        var principal = BuildPrincipal(PrincipalTypes.User, id.ToString());

        var subject = SessionController.ResolveSubject(principal);

        Assert.NotNull(subject);
        Assert.Equal(id, subject!.Value.Id);
        Assert.Equal(PrincipalTypes.User, subject.Value.SubjectType);
    }

    [Fact]
    public void ResolveSubject_ResidentPrincipal_ReturnsItsOwnIdAndType()
    {
        var id = Guid.NewGuid();
        var principal = BuildPrincipal(PrincipalTypes.Resident, id.ToString());

        var subject = SessionController.ResolveSubject(principal);

        Assert.NotNull(subject);
        Assert.Equal(id, subject!.Value.Id);
        Assert.Equal(PrincipalTypes.Resident, subject.Value.SubjectType);
    }

    [Fact]
    public void ResolveSubject_MissingPrincipalTypeClaim_ReturnsNull()
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var principal = new ClaimsPrincipal(identity);

        Assert.Null(SessionController.ResolveSubject(principal));
    }

    [Fact]
    public void ResolveSubject_UnparsableNameIdentifier_ReturnsNull()
    {
        var principal = BuildPrincipal(PrincipalTypes.User, "not-a-guid");

        Assert.Null(SessionController.ResolveSubject(principal));
    }

    [Fact]
    public void ResolveSubject_UnrecognisedPrincipalType_ReturnsNull()
    {
        var principal = BuildPrincipal("superadmin", Guid.NewGuid().ToString());

        Assert.Null(SessionController.ResolveSubject(principal));
    }

    // --- TokenBelongsToSubject (logout must not revoke someone else's token) ----------------

    [Fact]
    public void TokenBelongsToSubject_MatchingIdAndType_ReturnsTrue()
    {
        var id = Guid.NewGuid();
        var caller = new SessionController.AuthenticatedSubject(id, PrincipalTypes.Resident);

        Assert.True(SessionController.TokenBelongsToSubject(id, PrincipalTypes.Resident, caller));
    }

    [Fact]
    public void TokenBelongsToSubject_DifferentId_ReturnsFalse()
    {
        var caller = new SessionController.AuthenticatedSubject(Guid.NewGuid(), PrincipalTypes.Resident);

        Assert.False(SessionController.TokenBelongsToSubject(Guid.NewGuid(), PrincipalTypes.Resident, caller));
    }

    [Fact]
    public void TokenBelongsToSubject_SameId_DifferentType_ReturnsFalse()
    {
        var id = Guid.NewGuid();
        var caller = new SessionController.AuthenticatedSubject(id, PrincipalTypes.User);

        // Same GUID reused as a resident id is not realistic, but the check must still
        // key on both fields - never id alone.
        Assert.False(SessionController.TokenBelongsToSubject(id, PrincipalTypes.Resident, caller));
    }

    // --- SelectActiveSessions (GET /api/auth/sessions lists only currently-active ones) -----

    [Fact]
    public void SelectActiveSessions_KeepsOnlyUnrevokedUnexpiredSessions()
    {
        var now = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var active = Summary(revokedAt: null, expiresAt: now.AddDays(1));
        var revoked = Summary(revokedAt: now.AddMinutes(-1), expiresAt: now.AddDays(1));
        var expired = Summary(revokedAt: null, expiresAt: now.AddMinutes(-1));
        var expiringExactlyNow = Summary(revokedAt: null, expiresAt: now);

        var result = SessionController.SelectActiveSessions(new[] { active, revoked, expired, expiringExactlyNow }, now);

        Assert.Single(result);
        Assert.Contains(active, result);
    }

    [Fact]
    public void SelectActiveSessions_NoSessions_ReturnsEmpty()
    {
        var result = SessionController.SelectActiveSessions(Array.Empty<RefreshTokenSummary>(), DateTime.UtcNow);

        Assert.Empty(result);
    }

    // --- ResolveDeviceLabel (caller-supplied / User-Agent, always untrusted free text) ------

    [Fact]
    public void ResolveDeviceLabel_ExplicitLabelProvided_IsUsedTrimmed()
    {
        Assert.Equal("iPhone de Lucas", SessionController.ResolveDeviceLabel("  iPhone de Lucas  ", "Mozilla/5.0"));
    }

    [Fact]
    public void ResolveDeviceLabel_NoExplicitLabel_FallsBackToUserAgent()
    {
        Assert.Equal("Mozilla/5.0", SessionController.ResolveDeviceLabel(null, "Mozilla/5.0"));
    }

    [Fact]
    public void ResolveDeviceLabel_NeitherProvided_FallsBackToDesconhecido()
    {
        Assert.Equal("Desconhecido", SessionController.ResolveDeviceLabel(null, null));
        Assert.Equal("Desconhecido", SessionController.ResolveDeviceLabel("   ", "  "));
    }

    [Fact]
    public void ResolveDeviceLabel_OverlongValue_IsTruncatedToTwoHundredChars()
    {
        var huge = new string('a', 500);

        var label = SessionController.ResolveDeviceLabel(huge, null);

        Assert.Equal(200, label.Length);
    }

    // --- helpers -----------------------------------------------------------------------------

    private static ClaimsPrincipal BuildPrincipal(string principalType, string nameIdentifier)
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        identity.AddClaim(new Claim(PrincipalTypes.Claim, principalType));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));
        return new ClaimsPrincipal(identity);
    }

    private static RefreshTokenSummary Summary(DateTime? revokedAt, DateTime expiresAt) => new(
        Guid.NewGuid(), "Device", "127.0.0.1", DateTime.UtcNow.AddDays(-1), expiresAt, revokedAt);

    private static JwtSecurityToken ReadToken(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);

    private static JwtTokenService CreateJwtService()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT:Secret"] = "unit-test-signing-secret-unit-test-signing-secret",
                ["JWT:Issuer"] = "Condotify.Tests",
                ["JWT:Audience"] = "Condotify.Tests"
            })
            .Build();
        return new JwtTokenService(configuration);
    }

    private static UserAccess SampleUser() => UserAccess.Create(
        "Lucas Bastos", "lucas@test.com", "Senha123!", "11999999999", "12345678900", "1234567",
        "1990-01-01", AccessTypeEnum.Admin, false, DateTime.UtcNow, DateTime.UtcNow, Hasher, Guid.NewGuid());

    private static ResidentAccessDTO SampleResident() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Maria Moradora",
        Email = "maria@test.com",
        UnitId = Guid.NewGuid()
    };
}
