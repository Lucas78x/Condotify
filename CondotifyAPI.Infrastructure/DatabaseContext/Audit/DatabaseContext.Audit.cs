using CondotifyAPI.Domain.DTO.Audit;
using CondotifyAPI.Infrastructure.ContextConfiguration.Audit;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<UserAccessAuditDTO> UserAudits { get; set; }
    public DbSet<DeviceAuditDTO> DeviceAudits { get; set; }
    public DbSet<TicketAuditDTO> TicketsAudits { get; set; }

    internal static void AuditsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new UserAccessAuditConfiguration());
        builder.ApplyConfiguration(new DeviceAuditConfiguration());
        builder.ApplyConfiguration(new TicketAuditConfiguration());
    }
}