using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Authorization;

public interface ILicenseModuleService
{
    Task<bool> IsEnabledAsync(Guid licenseId, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum module, CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> KeepEnabledAsync(IEnumerable<Guid> licenseIds, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum module, CancellationToken cancellationToken = default);
}

public sealed class LicenseModuleService(DatabaseContext context) : ILicenseModuleService
{
    public Task<bool> IsEnabledAsync(Guid licenseId, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum module, CancellationToken cancellationToken = default) =>
        context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && (x.EnabledModules & module) == module, cancellationToken);

    public async Task<HashSet<Guid>> KeepEnabledAsync(IEnumerable<Guid> licenseIds, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum module, CancellationToken cancellationToken = default)
    {
        var ids = licenseIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        return (await context.Licenses.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && (x.EnabledModules & module) == module)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
    }
}
