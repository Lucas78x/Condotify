using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Authorization;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// CondotifyAPI.Tests has no EF InMemory/SQLite provider (verified against
/// CondotifyAPI.Tests.csproj and DatabaseModelTests, whose one UseNpgsql context never
/// connects). So the filtering rules that ResidentAuthorizationService.GetGrantAsync and
/// CanAccessUnitAsync apply against the database are extracted into pure, internal static
/// predicates (visible here via InternalsVisibleTo in CondotifyAPI/Properties/AssemblyInfo.cs)
/// and exercised directly against hand-built DTOs, with `now` passed in explicitly so the
/// StartsAt/EndsAt boundary cases are deterministic. The EF-backed service methods call
/// these exact same predicates rather than re-implementing the rule in a LINQ Where(...),
/// so there is nothing left to drift between "what's tested" and "what runs against Postgres".
/// </summary>
public class ResidentAuthorizationServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ResidentUnitLinkDTO Link(
        bool isActive = true,
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        Guid? unitId = null) => new()
        {
            Id = Guid.NewGuid(),
            ResidentId = Guid.NewGuid(),
            UnitId = unitId ?? Guid.NewGuid(),
            IsActive = isActive,
            StartsAt = startsAt ?? Now.AddDays(-1),
            EndsAt = endsAt,
        };

    private static ResidentAccessDTO Resident(
        bool isActive = true,
        bool temporary = false,
        DateTime? expire = null,
        ResidentAccessTypeEnum accessType = ResidentAccessTypeEnum.Default) => new()
        {
            Id = Guid.NewGuid(),
            IsActive = isActive,
            Temporary = temporary,
            Expire = expire ?? Now.AddDays(1),
            AccessType = accessType,
        };

    // --- LinkIsCurrentlyValid ---------------------------------------------------------

    [Fact]
    public void LinkIsCurrentlyValid_ActiveAndCurrent_ReturnsTrue()
    {
        var link = Link(isActive: true, startsAt: Now.AddDays(-1), endsAt: Now.AddDays(1));

        Assert.True(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_ActiveWithNoEndDate_ReturnsTrue()
    {
        var link = Link(isActive: true, startsAt: Now.AddDays(-1), endsAt: null);

        Assert.True(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_InactiveLink_ReturnsFalse()
    {
        var link = Link(isActive: false, startsAt: Now.AddDays(-1), endsAt: Now.AddDays(1));

        Assert.False(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_EndsAtInThePast_ReturnsFalse()
    {
        var link = Link(isActive: true, startsAt: Now.AddDays(-10), endsAt: Now.AddDays(-1));

        Assert.False(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_StartsAtInTheFuture_ReturnsFalse()
    {
        var link = Link(isActive: true, startsAt: Now.AddDays(1), endsAt: null);

        Assert.False(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_StartsAtEqualsNow_ReturnsTrue()
    {
        // Rule is StartsAt <= now, so the exact instant a link becomes effective it must
        // already grant access - this is the boundary an off-by-one would silently miss.
        var link = Link(isActive: true, startsAt: Now, endsAt: null);

        Assert.True(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    [Fact]
    public void LinkIsCurrentlyValid_EndsAtEqualsNow_ReturnsFalse()
    {
        // Rule is EndsAt null or > now, so at the exact instant EndsAt is reached the link
        // must already be expired - the revocation-at-10:00 scenario from the brief.
        var link = Link(isActive: true, startsAt: Now.AddDays(-1), endsAt: Now);

        Assert.False(ResidentAuthorizationService.LinkIsCurrentlyValid(link, Now));
    }

    // --- ResidentCanSignIn -------------------------------------------------------------

    [Fact]
    public void ResidentCanSignIn_ActiveNonTemporary_ReturnsTrue()
    {
        var resident = Resident(isActive: true, temporary: false);

        Assert.True(ResidentAuthorizationService.ResidentCanSignIn(resident, Now));
    }

    [Fact]
    public void ResidentCanSignIn_InactiveResident_ReturnsFalse()
    {
        var resident = Resident(isActive: false);

        Assert.False(ResidentAuthorizationService.ResidentCanSignIn(resident, Now));
    }

    [Fact]
    public void ResidentCanSignIn_TemporaryWithExpireInThePast_ReturnsFalse()
    {
        var resident = Resident(isActive: true, temporary: true, expire: Now.AddMinutes(-1));

        Assert.False(ResidentAuthorizationService.ResidentCanSignIn(resident, Now));
    }

    [Fact]
    public void ResidentCanSignIn_TemporaryWithExpireInTheFuture_ReturnsTrue()
    {
        var resident = Resident(isActive: true, temporary: true, expire: Now.AddMinutes(1));

        Assert.True(ResidentAuthorizationService.ResidentCanSignIn(resident, Now));
    }

    [Fact]
    public void ResidentCanSignIn_TemporaryWithExpireEqualToNow_ReturnsFalse()
    {
        // Expire "must be in the future" - equal to now is not future.
        var resident = Resident(isActive: true, temporary: true, expire: Now);

        Assert.False(ResidentAuthorizationService.ResidentCanSignIn(resident, Now));
    }

    // --- ResolveAccessibleUnitIds / CanAccessUnit --------------------------------------

    [Fact]
    public void ResolveAccessibleUnitIds_OnlyIncludesCurrentlyValidLinks()
    {
        var validUnit = Guid.NewGuid();
        var revokedUnit = Guid.NewGuid();
        var notYetStartedUnit = Guid.NewGuid();
        var expiredUnit = Guid.NewGuid();

        var links = new[]
        {
            Link(isActive: true, startsAt: Now.AddDays(-1), endsAt: null, unitId: validUnit),
            Link(isActive: false, startsAt: Now.AddDays(-1), endsAt: null, unitId: revokedUnit),
            Link(isActive: true, startsAt: Now.AddDays(1), endsAt: null, unitId: notYetStartedUnit),
            Link(isActive: true, startsAt: Now.AddDays(-10), endsAt: Now.AddDays(-1), unitId: expiredUnit),
        };

        var accessible = ResidentAuthorizationService.ResolveAccessibleUnitIds(links, Now);

        Assert.Equal(new[] { validUnit }, accessible);
    }

    [Fact]
    public void CanAccessUnit_ForAnotherResidentsUnit_ReturnsFalse()
    {
        // This resident's own links never mention the other resident's unit at all -
        // the EF query that feeds this predicate is scoped by ResidentId, so a unit that
        // belongs only to a different resident simply never appears in the candidate set.
        var ownUnit = Guid.NewGuid();
        var otherResidentsUnit = Guid.NewGuid();
        var links = new[]
        {
            Link(isActive: true, startsAt: Now.AddDays(-1), endsAt: null, unitId: ownUnit),
        };

        Assert.True(ResidentAuthorizationService.CanAccessUnit(links, ownUnit, Now));
        Assert.False(ResidentAuthorizationService.CanAccessUnit(links, otherResidentsUnit, Now));
    }

    // --- TryResident (principal parsing) ------------------------------------------------

    [Fact]
    public void TryResident_PrincipalWithoutResidentPrincipalType_ReturnsFalse()
    {
        var principal = BuildPrincipal(PrincipalTypes.User, Guid.NewGuid(), Guid.NewGuid());

        var ok = ResidentAuthorizationService.TryResident(principal, out _, out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryResident_ResidentPrincipal_ReturnsTrueWithParsedIds()
    {
        var residentId = Guid.NewGuid();
        var licenseId = Guid.NewGuid();
        var principal = BuildPrincipal(PrincipalTypes.Resident, residentId, licenseId);

        var ok = ResidentAuthorizationService.TryResident(principal, out var parsedResidentId, out var parsedLicenseId);

        Assert.True(ok);
        Assert.Equal(residentId, parsedResidentId);
        Assert.Equal(licenseId, parsedLicenseId);
    }

    [Fact]
    public void TryResident_UnauthenticatedPrincipal_ReturnsFalse()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var ok = ResidentAuthorizationService.TryResident(principal, out _, out _);

        Assert.False(ok);
    }

    private static ClaimsPrincipal BuildPrincipal(string principalType, Guid residentId, Guid licenseId)
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        identity.AddClaim(new Claim(PrincipalTypes.Claim, principalType));
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, residentId.ToString()));
        identity.AddClaim(new Claim("license_id", licenseId.ToString()));
        return new ClaimsPrincipal(identity);
    }
}
