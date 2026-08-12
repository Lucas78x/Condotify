using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure.ContextConfiguration.AccessControl;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<OfflineAccessDeviceDTO> OfflineAccessDevices { get; set; }
    public DbSet<OfflineAccessOperationDTO> OfflineAccessOperations { get; set; }

    internal static void OfflineOperationsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new OfflineAccessDeviceConfiguration());
        builder.ApplyConfiguration(new OfflineAccessOperationConfiguration());
    }
}
