using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class MobileVisitorSelectionTests
{
    [Fact]
    public void ResolveUnit_IgnoresEmptyGuidAndUsesThePrimaryUnit()
    {
        var expected = Guid.NewGuid();
        var units = new[]
        {
            new ResidentUnitViewModel { UnitId = Guid.Empty, IsPrimary = true, BlockName = "Inválido", Number = "0" },
            new ResidentUnitViewModel { UnitId = Guid.NewGuid(), BlockName = "Bloco B", Number = "202" },
            new ResidentUnitViewModel { UnitId = expected, IsPrimary = true, BlockName = "Bloco A", Number = "101" }
        };

        var selected = MobileVisitorSelection.ResolveUnit(units);

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void ResolveUnit_UsesTheValidTemplateUnitWhenRepeatingAVisit()
    {
        var expected = Guid.NewGuid();
        var units = new[]
        {
            new ResidentUnitViewModel { UnitId = Guid.NewGuid(), IsPrimary = true, BlockName = "Bloco A", Number = "101" },
            new ResidentUnitViewModel { UnitId = expected, BlockName = "Bloco B", Number = "202" }
        };

        var selected = MobileVisitorSelection.ResolveUnit(units, "bloco b", "202");

        Assert.Equal(expected, selected);
    }

    [Fact]
    public void ResolveHost_DoesNotLeaveTheStaffFormWithAnEmptyGuid()
    {
        var expected = Guid.NewGuid();

        var selected = MobileVisitorSelection.ResolveHost([Guid.Empty, expected]);

        Assert.Equal(expected, selected);
    }
}
