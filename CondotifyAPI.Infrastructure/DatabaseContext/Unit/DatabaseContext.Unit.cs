using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Infrastructure.ContextConfiguration.Unit;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<UnitDTO> Units { get; set; }

    internal static void UnitsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new UnitConfiguration());
    }
}