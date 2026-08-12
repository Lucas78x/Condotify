using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Tests;

public sealed class ResidentLicenseScopeTests
{
    private static readonly DateTime Now = new(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ResolveUnitForLicense_SelectsTheUnitFromTheRequestedLicense()
    {
        var firstLicense = Guid.NewGuid();
        var secondLicense = Guid.NewGuid();
        var firstUnit = Unit(firstLicense, "101");
        var expected = Unit(secondLicense, "202");
        var resident = Resident(Link(firstUnit, primary: true), Link(expected));

        var selected = ResidentLicenseScope.ResolveUnitForLicense(resident, secondLicense);

        Assert.Same(expected, selected);
    }

    [Fact]
    public void ResolveCurrentUnitForLicense_RejectsInactiveAndExpiredLinks()
    {
        var licenseId = Guid.NewGuid();
        var inactive = Link(Unit(licenseId, "101"), primary: true);
        inactive.IsActive = false;
        var expired = Link(Unit(licenseId, "102"));
        expired.EndsAt = Now;
        var expected = Link(Unit(licenseId, "103"));
        expected.StartsAt = Now.AddMinutes(-1);
        var resident = Resident(inactive, expired, expected);

        var selected = ResidentLicenseScope.ResolveCurrentUnitForLicense(resident, licenseId, Now);

        Assert.Same(expected.Unit, selected);
    }

    [Fact]
    public void ResolveUnitForLicense_DoesNotFallBackToLegacyUnitWhenLinksExist()
    {
        var requestedLicense = Guid.NewGuid();
        var linkedUnit = Unit(Guid.NewGuid(), "201");
        var legacyUnit = Unit(requestedLicense, "999");
        var resident = Resident(Link(linkedUnit, primary: true));
        resident.UnitId = legacyUnit.Id;
        resident.Unit = legacyUnit;

        var selected = ResidentLicenseScope.ResolveUnitForLicense(resident, requestedLicense);

        Assert.Null(selected);
    }

    [Fact]
    public void ResolveUnitForLicense_FallsBackForLegacyResidentWithoutLinks()
    {
        var licenseId = Guid.NewGuid();
        var legacyUnit = Unit(licenseId, "101");
        var resident = new ResidentAccessDTO { UnitId = legacyUnit.Id, Unit = legacyUnit };

        var selected = ResidentLicenseScope.ResolveUnitForLicense(resident, licenseId);

        Assert.Same(legacyUnit, selected);
    }

    private static ResidentAccessDTO Resident(params ResidentUnitLinkDTO[] links) => new()
    {
        UnitLinks = links,
        UnitId = Guid.Empty
    };

    private static ResidentUnitLinkDTO Link(UnitDTO unit, bool primary = false) => new()
    {
        UnitId = unit.Id,
        Unit = unit,
        IsPrimary = primary,
        IsActive = true,
        StartsAt = Now.AddDays(-1),
        CreatedAt = Now
    };

    private static UnitDTO Unit(Guid licenseId, string number)
    {
        var block = new BlockDTO { Id = Guid.NewGuid(), LicenseId = licenseId, Name = "Bloco" };
        return new UnitDTO { Id = Guid.NewGuid(), Number = number, BlockId = block.Id, Block = block };
    }
}
