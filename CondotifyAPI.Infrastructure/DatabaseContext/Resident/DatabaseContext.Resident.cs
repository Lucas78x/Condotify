using CondotifyAPI.DTO.Resident;
using CondotifyAPI.Infrastructure.ContextConfiguration.Resident;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<ResidentAccessDTO> Residents { get; set; }

    internal static void ResidentsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ResidentAccessConfiguration());
    }
}