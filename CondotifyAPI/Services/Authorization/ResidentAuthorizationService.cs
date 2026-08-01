using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Authorization;

/// <summary>
/// The resident equivalent of <see cref="LicenseAccessGrant"/>. A resident has no
/// permissions over a licence the way staff do - they have access to the specific units
/// <see cref="Domain.DTO.Resident.ResidentUnitLinkDTO"/> currently links them to. LicenseId
/// is not derived from those units (a resident could in principle be linked to units under
/// different licences); it is read from the "license_id" claim the resident token already
/// carries, chosen at login time by <c>JwtTokenService.CreateResidentAccessToken</c>. UnitIds
/// is always resolved fresh from the database for that licence - never from the token - so a
/// link revoked mid-session stops granting access on the very next request.
/// </summary>
public sealed record ResidentAccessGrant(
    Guid ResidentId,
    Guid LicenseId,
    IReadOnlyCollection<Guid> UnitIds,
    ResidentAccessTypeEnum AccessType,
    bool IsResponsible);

public interface IResidentAuthorizationService
{
    Task<ResidentAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default);
    Task<bool> CanAccessUnitAsync(ClaimsPrincipal principal, Guid unitId, CancellationToken cancellationToken = default);
}

public sealed class ResidentAuthorizationService : IResidentAuthorizationService
{
    private readonly DatabaseContext _context;

    public ResidentAuthorizationService(DatabaseContext context) => _context = context;

    public async Task<ResidentAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!TryResident(principal, out var residentId, out var licenseId)) return null;

        var resident = await _context.Residents.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == residentId, cancellationToken);
        if (resident is null) return null;

        var now = DateTime.UtcNow;
        if (!ResidentCanSignIn(resident, now)) return null;

        // Scoped coarsely in SQL (this resident, this licence); the fine-grained
        // "is the link currently valid" rule is then applied in memory via the exact
        // predicate ResidentAuthorizationServiceTests exercises directly - no separate
        // Where(...) reimplementation of the rule exists to drift from it.
        var links = await _context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.ResidentId == residentId && x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);

        var unitIds = ResolveAccessibleUnitIds(links, now);

        return new ResidentAccessGrant(
            residentId,
            licenseId,
            unitIds,
            resident.AccessType,
            resident.AccessType == ResidentAccessTypeEnum.Responsible);
    }

    public async Task<bool> CanAccessUnitAsync(ClaimsPrincipal principal, Guid unitId, CancellationToken cancellationToken = default)
    {
        var grant = await GetGrantAsync(principal, cancellationToken);
        return grant is not null && grant.UnitIds.Contains(unitId);
    }

    /// <summary>
    /// principal_type must be "resident" (see <see cref="PrincipalTypes"/>); any other
    /// principal - or one missing the claims a resident token always carries - yields no grant.
    /// Internal (not private) so ResidentAuthorizationServiceTests can exercise the parsing
    /// rule directly without a database.
    /// </summary>
    internal static bool TryResident(ClaimsPrincipal principal, out Guid residentId, out Guid licenseId)
    {
        residentId = Guid.Empty;
        licenseId = Guid.Empty;

        if (principal.FindFirstValue(PrincipalTypes.Claim) != PrincipalTypes.Resident) return false;

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out residentId) &&
            Guid.TryParse(principal.FindFirstValue("license_id"), out licenseId);
    }

    /// <summary>
    /// The resident must be active, and if temporary, must not yet have expired. Pure and
    /// takes <paramref name="now"/> explicitly so the Expire boundary is deterministic in tests.
    /// </summary>
    internal static bool ResidentCanSignIn(ResidentAccessDTO resident, DateTime now)
    {
        if (!resident.IsActive) return false;
        if (resident.Temporary && resident.Expire <= now) return false;
        return true;
    }

    /// <summary>
    /// A link counts only while IsActive, StartsAt has already been reached (StartsAt &lt;= now),
    /// and it has not ended (EndsAt is null or strictly in the future). Pure and takes
    /// <paramref name="now"/> explicitly for deterministic boundary testing.
    /// </summary>
    internal static bool LinkIsCurrentlyValid(ResidentUnitLinkDTO link, DateTime now)
    {
        if (!link.IsActive) return false;
        if (link.StartsAt > now) return false;
        if (link.EndsAt.HasValue && link.EndsAt.Value <= now) return false;
        return true;
    }

    internal static IReadOnlyCollection<Guid> ResolveAccessibleUnitIds(IEnumerable<ResidentUnitLinkDTO> links, DateTime now) =>
        links.Where(x => LinkIsCurrentlyValid(x, now)).Select(x => x.UnitId).ToList();

    internal static bool CanAccessUnit(IReadOnlyCollection<ResidentUnitLinkDTO> links, Guid unitId, DateTime now) =>
        links.Any(x => x.UnitId == unitId && LinkIsCurrentlyValid(x, now));
}
