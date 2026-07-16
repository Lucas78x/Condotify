using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.AccessControl;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Extensions;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.AccessControl;

public interface IAccessRouteResolver
{
    Task<ResolvedAccessRouteSet> ResolveAsync(Guid licenseId, ResidentAccessDTO resident, AccessCredentialTypeEnum credentialType);
}

public sealed class AccessRouteResolver : IAccessRouteResolver
{
    private readonly DatabaseContext _context;

    public AccessRouteResolver(DatabaseContext context) => _context = context;

    public async Task<ResolvedAccessRouteSet> ResolveAsync(
        Guid licenseId,
        ResidentAccessDTO resident,
        AccessCredentialTypeEnum credentialType)
    {
        var audience = ResolveAudience(resident);
        var routes = await _context.AccessRoutes.AsNoTracking()
            .Include(x => x.Devices).ThenInclude(x => x.Device)
            .Include(x => x.ResidentOverrides)
            .Where(x => x.LicenseId == licenseId && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync();

        routes = routes.Where(route =>
        {
            var overrideMode = route.ResidentOverrides.FirstOrDefault(x => x.ResidentId == resident.Id)?.Mode;
            if (overrideMode == AccessRouteOverrideModeEnum.Exclude) return false;
            if (overrideMode == AccessRouteOverrideModeEnum.Include) return true;
            return (route.Audience & audience) != 0 && (!resident.Temporary || route.AllowTemporary);
        }).ToList();

        var targets = routes
            .SelectMany(route => route.Devices
                .Where(link => link.IsActive && link.Device.IsActive && Supports(link.Device, credentialType))
                .Select(link => new { Route = route, Link = link }))
            .GroupBy(x => x.Link.DeviceId)
            .Select(group => new ResolvedAccessRouteTarget(
                group.First().Link.Device,
                group.Select(x => new AccessPortalAssignment(
                        x.Link.PortalNumber,
                        x.Link.Direction,
                        x.Route.Name,
                        x.Route.DaysOfWeekMask,
                        x.Route.StartTime,
                        x.Route.EndTime))
                    .Distinct()
                    .ToList()))
            .OrderBy(x => x.Device.Name)
            .ToList();

        return new ResolvedAccessRouteSet(audience, routes.Select(x => x.Name).ToList(), targets);
    }

    public static AccessRouteAudienceEnum ResolveAudience(ResidentAccessDTO resident)
    {
        if (resident.AccessType == ResidentAccessTypeEnum.Guest) return AccessRouteAudienceEnum.Visitor;
        if (resident.AccessType == ResidentAccessTypeEnum.ServiceProvider) return AccessRouteAudienceEnum.ServiceProvider;

        var link = resident.UnitLinks.FirstOrDefault(x => x.IsPrimary) ?? resident.UnitLinks.FirstOrDefault();
        return link?.Relationship switch
        {
            ResidentUnitRelationshipEnum.OwnerResponsible => AccessRouteAudienceEnum.OwnerResponsible,
            ResidentUnitRelationshipEnum.TenantResponsible => AccessRouteAudienceEnum.TenantResponsible,
            ResidentUnitRelationshipEnum.Responsible => AccessRouteAudienceEnum.Responsible,
            ResidentUnitRelationshipEnum.Dependent => AccessRouteAudienceEnum.Dependent,
            _ => resident.AccessType == ResidentAccessTypeEnum.Responsible
                ? AccessRouteAudienceEnum.Responsible
                : AccessRouteAudienceEnum.Resident
        };
    }

    private static bool Supports(AccessControlDeviceDTO device, AccessCredentialTypeEnum type) =>
        type != AccessCredentialTypeEnum.Face || device.Type.SupportsFace();
}

public sealed record ResolvedAccessRouteSet(
    AccessRouteAudienceEnum Audience,
    IReadOnlyList<string> RouteNames,
    IReadOnlyList<ResolvedAccessRouteTarget> Targets);

public sealed record ResolvedAccessRouteTarget(
    AccessControlDeviceDTO Device,
    IReadOnlyList<AccessPortalAssignment> Portals);
