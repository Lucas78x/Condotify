using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Infrastructure.ContextConfiguration.User;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<UserAccessDTO> Users { get; set; }

    internal static void UsersEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new UserAccessConfiguration());
    }
}