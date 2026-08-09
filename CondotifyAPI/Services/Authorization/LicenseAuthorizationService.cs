using System.Security.Claims;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Authorization;

public sealed record LicenseAccessGrant(
    Guid LicenseId,
    Guid UserId,
    LicenseAccessRoleEnum Role,
    LicensePermissionEnum Permissions,
    bool IsEnterpriseAdministrator)
{
    public bool Has(LicensePermissionEnum permission) => (Permissions & permission) == permission;
}

public interface ILicenseAuthorizationService
{
    Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default);
    Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(
        ClaimsPrincipal principal,
        LicensePermissionEnum permission,
        CancellationToken cancellationToken = default);
}

public sealed class LicenseAuthorizationService : ILicenseAuthorizationService
{
    private readonly DatabaseContext _context;

    public LicenseAuthorizationService(DatabaseContext context) => _context = context;

    public async Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default)
    {
        if (!TryUser(principal, out var userId, out var enterpriseId)) return null;
        // IgnoreQueryFilters() deliberado: este metodo CALCULA o conjunto de
        // licencas acessiveis que o filtro global usa. Sem isso, a consulta
        // dependeria circularmente do resultado que ainda nao existe.
        // Unicas duas excecoes esperadas neste projeto -- ver
        // docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md.
        var user = await _context.Users.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userId && x.EnterpriseId == enterpriseId, cancellationToken);
        if (user is null) return null;
        if (!await _context.Licenses.AsNoTracking().IgnoreQueryFilters().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId, cancellationToken)) return null;

        if (user.AccessType is AccessTypeEnum.Developer or AccessTypeEnum.Admin)
            return new LicenseAccessGrant(licenseId, userId, LicenseAccessRoleEnum.Administrator, LicensePermissionEnum.All, true);

        var access = await _context.LicenseUserAccesses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == licenseId && x.UserId == userId && x.IsActive, cancellationToken);
        return access is null ? null : new LicenseAccessGrant(licenseId, userId, access.Role, LicenseAccessDefaults.Normalize(access.Permissions), false);
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default) =>
        (await GetGrantAsync(principal, licenseId, cancellationToken))?.Has(permission) == true;

    public async Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default) =>
        (await GetLicensePermissionsAsync(principal, cancellationToken)).Keys.ToHashSet();

    public async Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default)
    {
        if (!TryUser(principal, out var userId, out var enterpriseId))
            return new Dictionary<Guid, LicensePermissionEnum>();

        // IgnoreQueryFilters() deliberado: este metodo CALCULA o conjunto de
        // licencas acessiveis que o filtro global usa. Sem isso, a consulta
        // dependeria circularmente do resultado que ainda nao existe.
        // Unicas duas excecoes esperadas neste projeto -- ver
        // docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md.
        var user = await _context.Users.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId && x.EnterpriseId == enterpriseId, cancellationToken);
        if (user is null)
            return new Dictionary<Guid, LicensePermissionEnum>();

        if (user.AccessType is AccessTypeEnum.Developer or AccessTypeEnum.Admin)
        {
            return await _context.Licenses.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.EnterpriseId == enterpriseId)
                .ToDictionaryAsync(x => x.Id, _ => LicensePermissionEnum.All, cancellationToken);
        }

        var grants = await _context.LicenseUserAccesses.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.IsActive && x.License.EnterpriseId == enterpriseId)
            .Select(x => new { x.LicenseId, x.Permissions })
            .ToListAsync(cancellationToken);

        return grants.ToDictionary(
            x => x.LicenseId,
            x => LicenseAccessDefaults.Normalize(x.Permissions));
    }

    public async Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(
        ClaimsPrincipal principal,
        LicensePermissionEnum permission,
        CancellationToken cancellationToken = default)
    {
        var grants = await GetLicensePermissionsAsync(principal, cancellationToken);
        return grants
            .Where(x => (x.Value & permission) == permission)
            .Select(x => x.Key)
            .ToHashSet();
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId, out Guid enterpriseId)
    {
        userId = Guid.Empty;
        enterpriseId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
            Guid.TryParse(principal.FindFirstValue("enterprise_id"), out enterpriseId);
    }
}
