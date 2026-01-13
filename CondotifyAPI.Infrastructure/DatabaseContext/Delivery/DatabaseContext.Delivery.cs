using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Infrastructure.ContextConfiguration.Delivery;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<DeliveryDTO> Deliveries { get; set; }

    internal static void DeliveriesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new DeliveryConfiguration());
    }
}