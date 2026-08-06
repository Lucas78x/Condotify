using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public interface IVehicleLookupService
{
    Task<Guid?> FindActiveVehicleIdAsync(Guid licenseId, string normalizedPlate, CancellationToken cancellationToken = default);
}

public sealed class VehicleLookupService(DatabaseContext context) : IVehicleLookupService
{
    public async Task<Guid?> FindActiveVehicleIdAsync(Guid licenseId, string normalizedPlate, CancellationToken cancellationToken = default) =>
        await context.Vehicles
            .AsNoTracking()
            .Where(v => v.IsActive && v.Plate == normalizedPlate && v.Unit.Block.LicenseId == licenseId &&
                        (v.Resident == null || v.Resident.IsActive))
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
