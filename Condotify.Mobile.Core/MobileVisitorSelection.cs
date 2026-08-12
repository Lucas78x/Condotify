using Condotify.Models;

namespace Condotify.Mobile.Core;

public static class MobileVisitorSelection
{
    public static Guid ResolveUnit(
        IEnumerable<ResidentUnitViewModel> units,
        string? preferredBlockName = null,
        string? preferredUnitNumber = null)
    {
        var validUnits = units.Where(unit => unit.UnitId != Guid.Empty).ToList();
        if (validUnits.Count == 0) return Guid.Empty;

        if (!string.IsNullOrWhiteSpace(preferredBlockName) &&
            !string.IsNullOrWhiteSpace(preferredUnitNumber))
        {
            var preferred = validUnits.FirstOrDefault(unit =>
                unit.BlockName.Equals(preferredBlockName, StringComparison.OrdinalIgnoreCase) &&
                unit.Number.Equals(preferredUnitNumber, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null) return preferred.UnitId;
        }

        return validUnits.FirstOrDefault(unit => unit.IsPrimary)?.UnitId
            ?? validUnits[0].UnitId;
    }

    public static Guid ResolveHost(IEnumerable<Guid> hostIds) =>
        hostIds.FirstOrDefault(hostId => hostId != Guid.Empty);
}
