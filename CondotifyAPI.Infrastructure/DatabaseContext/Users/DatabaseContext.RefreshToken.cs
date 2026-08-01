using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Infrastructure.ContextConfiguration.User;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<RefreshTokenDTO> RefreshTokens { get; set; }

    internal static void RefreshTokensEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
