using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Infrastructure.ContextConfiguration.Announcements;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AnnouncementDTO> Announcements { get; set; }

    internal static void AnnouncementEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AnnouncementConfiguration());
    }
}
