using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Infrastructure.ContextConfiguration.Ticket;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<TicketDTO> Tickets { get; set; }

    internal static void TicketsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new TicketConfiguration());
    }
}