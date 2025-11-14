using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Infrastructure.ContextConfiguration.License;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<LicenseDTO> Licenses { get; set; }

    internal static void LicensesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new LicenseConfiguration());
    }
}