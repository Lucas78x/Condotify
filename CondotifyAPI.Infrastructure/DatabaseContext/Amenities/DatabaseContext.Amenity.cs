using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Infrastructure.ContextConfiguration.Amenities;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AmenityDTO> Amenities { get; set; }
    public DbSet<AmenityScheduleSlotDTO> AmenityScheduleSlots { get; set; }
    public DbSet<AmenityBlackoutDTO> AmenityBlackouts { get; set; }
    public DbSet<AmenityBookingDTO> AmenityBookings { get; set; }

    internal static void AmenitiesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AmenityConfiguration());
        builder.ApplyConfiguration(new AmenityScheduleSlotConfiguration());
        builder.ApplyConfiguration(new AmenityBlackoutConfiguration());
        builder.ApplyConfiguration(new AmenityBookingConfiguration());
    }
}
