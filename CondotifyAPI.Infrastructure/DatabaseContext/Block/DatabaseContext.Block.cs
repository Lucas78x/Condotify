using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Infrastructure.ContextConfiguration.Block;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<BlockDTO> Blocks { get; set; }

    internal static void BlocksEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new BlockConfiguration());
    }
}