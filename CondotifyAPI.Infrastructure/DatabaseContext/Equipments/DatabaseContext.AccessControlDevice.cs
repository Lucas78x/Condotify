using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure.ContextConfiguration.Block;
using CondotifyAPI.Infrastructure.ContextConfiguration.Equipments;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AccessControlDeviceDTO> Devices { get; set; }

    internal static void DevicesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AccessControlDeviceConfiguration());
    }
}