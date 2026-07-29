using CondotifyAPI.Domain.DTO.RecycleBin;
using CondotifyAPI.Infrastructure.ContextConfiguration.RecycleBin;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<RecycleBinItemDTO> RecycleBinItems { get; set; }

    internal static void RecycleBinEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new RecycleBinItemConfiguration());
    }
}
