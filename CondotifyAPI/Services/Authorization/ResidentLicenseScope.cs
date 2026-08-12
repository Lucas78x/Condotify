using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Services.Authorization;

public static class ResidentLicenseScope
{
    public static IQueryable<ResidentAccessDTO> ForLicense(
        this IQueryable<ResidentAccessDTO> residents,
        Guid licenseId) =>
        residents.Where(resident =>
            resident.UnitLinks.Any(link => link.Unit.Block.LicenseId == licenseId) ||
            (!resident.UnitLinks.Any() && resident.Unit.Block.LicenseId == licenseId));

    public static IQueryable<ResidentAccessCredentialDTO> ForLicense(
        this IQueryable<ResidentAccessCredentialDTO> credentials,
        Guid licenseId) =>
        credentials.Where(credential =>
            credential.Resident.UnitLinks.Any(link => link.Unit.Block.LicenseId == licenseId) ||
            (!credential.Resident.UnitLinks.Any() && credential.Resident.Unit.Block.LicenseId == licenseId));

    public static IReadOnlyList<UnitDTO> ResolveUnitsForLicense(ResidentAccessDTO resident, Guid licenseId)
    {
        var linkedUnits = resident.UnitLinks
            .Where(link => link.UnitId != Guid.Empty && link.Unit?.Block?.LicenseId == licenseId)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.CreatedAt)
            .Select(link => link.Unit)
            .DistinctBy(unit => unit.Id)
            .ToList();
        if (linkedUnits.Count > 0 || resident.UnitLinks.Count > 0) return linkedUnits;

        return resident.UnitId != Guid.Empty && resident.Unit?.Block?.LicenseId == licenseId
            ? [resident.Unit]
            : [];
    }

    public static UnitDTO? ResolveUnitForLicense(ResidentAccessDTO resident, Guid licenseId) =>
        ResolveUnitsForLicense(resident, licenseId).FirstOrDefault();

    public static UnitDTO? ResolveCurrentUnitForLicense(ResidentAccessDTO resident, Guid licenseId, DateTime now)
    {
        var linkedUnit = resident.UnitLinks
            .Where(link => link.UnitId != Guid.Empty &&
                           link.Unit?.Block?.LicenseId == licenseId &&
                           ResidentAuthorizationService.LinkIsCurrentlyValid(link, now))
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.CreatedAt)
            .Select(link => link.Unit)
            .FirstOrDefault();
        if (linkedUnit is not null || resident.UnitLinks.Count > 0) return linkedUnit;

        return resident.UnitId != Guid.Empty && resident.Unit?.Block?.LicenseId == licenseId
            ? resident.Unit
            : null;
    }

    public static UnitDTO? ResolvePrimaryUnit(ResidentAccessDTO resident)
    {
        var linkedUnit = resident.UnitLinks
            .Where(link => link.UnitId != Guid.Empty && link.Unit is not null)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.CreatedAt)
            .Select(link => link.Unit)
            .FirstOrDefault();
        if (linkedUnit is not null || resident.UnitLinks.Count > 0) return linkedUnit;

        return resident.UnitId != Guid.Empty ? resident.Unit : null;
    }

    public static ResidentUnitLinkDTO? ResolveLinkForLicense(ResidentAccessDTO resident, Guid licenseId) =>
        resident.UnitLinks
            .Where(link => link.Unit?.Block?.LicenseId == licenseId)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.CreatedAt)
            .FirstOrDefault();
}
