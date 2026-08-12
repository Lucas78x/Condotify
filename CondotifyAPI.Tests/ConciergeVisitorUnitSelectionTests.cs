using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Tests;

public sealed class ConciergeVisitorUnitSelectionTests
{
    [Fact]
    public void ResolveHostUnit_UsesThePrimaryLinkWhenTheLegacyUnitIdIsEmpty()
    {
        var licenseId = Guid.NewGuid();
        var expected = Unit(licenseId, "101");
        var host = new ResidentAccessDTO
        {
            UnitId = Guid.Empty,
            UnitLinks =
            [
                new ResidentUnitLinkDTO
                {
                    UnitId = expected.Id,
                    Unit = expected,
                    IsActive = true,
                    IsPrimary = true
                }
            ]
        };

        var selected = ResidentLicenseScope.ResolveCurrentUnitForLicense(host, licenseId, DateTime.UtcNow);

        Assert.Same(expected, selected);
    }

    [Fact]
    public void ResolveHostUnit_DoesNotFallBackToLegacyUnitWhenAConflictingLinkExists()
    {
        var licenseId = Guid.NewGuid();
        var wrongUnit = Unit(Guid.NewGuid(), "999");
        var expected = Unit(licenseId, "202");
        var host = new ResidentAccessDTO
        {
            UnitId = expected.Id,
            Unit = expected,
            UnitLinks =
            [
                new ResidentUnitLinkDTO
                {
                    UnitId = wrongUnit.Id,
                    Unit = wrongUnit,
                    IsActive = true,
                    IsPrimary = true
                }
            ]
        };

        var selected = ResidentLicenseScope.ResolveCurrentUnitForLicense(host, licenseId, DateTime.UtcNow);

        Assert.Null(selected);
    }

    private static UnitDTO Unit(Guid licenseId, string number)
    {
        var block = new BlockDTO { Id = Guid.NewGuid(), LicenseId = licenseId, Name = "Bloco" };
        return new UnitDTO { Id = Guid.NewGuid(), Number = number, BlockId = block.Id, Block = block };
    }
}
