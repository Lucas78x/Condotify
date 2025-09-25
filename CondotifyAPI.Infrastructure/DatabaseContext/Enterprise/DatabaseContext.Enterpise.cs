using CondotifyAPI.DTO.Enterprise;
using CondotifyAPI.Infrastructure.ContextConfiguration.Enterprise;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<EnterpriseDTO> Enterprises { get; set; }

    internal static void EnterprisesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new EnterpriseConfiguration());
    }
}