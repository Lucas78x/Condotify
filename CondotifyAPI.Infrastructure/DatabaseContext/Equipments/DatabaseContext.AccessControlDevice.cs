using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure.ContextConfiguration.Equipments;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AccessControlDeviceDTO> Devices { get; set; }
    public DbSet<CFTVDeviceDTO> CFTVDevices { get; set; }
    public DbSet<ResidentAccessControlDTO> ResidentDevices { get; set; }

    internal static void DevicesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AccessControlDeviceConfiguration());
        builder.ApplyConfiguration(new CFTVDeviceConfiguration());
        builder.ApplyConfiguration(new CFTVChannelConfiguration());
        builder.ApplyConfiguration(new ResidentAccessCredentialConfiguration());
        builder.ApplyConfiguration(new ResidentAccessDeviceConfiguration());
    }
}