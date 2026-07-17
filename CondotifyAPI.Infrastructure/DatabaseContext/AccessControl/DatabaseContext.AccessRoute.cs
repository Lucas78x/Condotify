using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure.ContextConfiguration.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AccessRouteDTO> AccessRoutes { get; set; }
    public DbSet<AccessRouteDeviceDTO> AccessRouteDevices { get; set; }
    public DbSet<AccessRouteResidentOverrideDTO> AccessRouteResidentOverrides { get; set; }
    public DbSet<AccessOperationAuditDTO> AccessOperationAudits { get; set; }
    public DbSet<AccessBatchOperationDTO> AccessBatchOperations { get; set; }
    public DbSet<AccessOperationItemDTO> AccessOperationItems { get; set; }
    public DbSet<AccessInventoryItemDTO> AccessInventoryItems { get; set; }
    public DbSet<AccessEventRecordDTO> AccessEventRecords { get; set; }

    internal static void AccessRoutesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AccessRouteConfiguration());
        builder.ApplyConfiguration(new AccessRouteDeviceConfiguration());
        builder.ApplyConfiguration(new AccessRouteResidentOverrideConfiguration());
        builder.ApplyConfiguration(new AccessOperationAuditConfiguration());
        builder.ApplyConfiguration(new AccessBatchOperationConfiguration());
        builder.ApplyConfiguration(new AccessOperationItemConfiguration());
        builder.ApplyConfiguration(new AccessInventoryItemConfiguration());
        builder.ApplyConfiguration(new AccessEventRecordConfiguration());
    }
}
