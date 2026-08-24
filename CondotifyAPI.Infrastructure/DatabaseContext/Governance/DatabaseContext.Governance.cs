using CondotifyAPI.Domain.DTO.Governance;
using CondotifyAPI.Infrastructure.ContextConfiguration.Governance;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<CondominiumAssemblyDTO> CondominiumAssemblies { get; set; }
    public DbSet<AssemblyAgendaItemDTO> AssemblyAgendaItems { get; set; }
    public DbSet<AssemblyVoteOptionDTO> AssemblyVoteOptions { get; set; }
    public DbSet<AssemblyEligibleUnitDTO> AssemblyEligibleUnits { get; set; }
    public DbSet<AssemblyAttendanceDTO> AssemblyAttendances { get; set; }
    public DbSet<AssemblyVoteDTO> AssemblyVotes { get; set; }
    public DbSet<AssemblyAuditDTO> AssemblyAudits { get; set; }

    internal static void GovernanceEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new CondominiumAssemblyConfiguration());
        builder.ApplyConfiguration(new AssemblyAgendaItemConfiguration());
        builder.ApplyConfiguration(new AssemblyVoteOptionConfiguration());
        builder.ApplyConfiguration(new AssemblyEligibleUnitConfiguration());
        builder.ApplyConfiguration(new AssemblyAttendanceConfiguration());
        builder.ApplyConfiguration(new AssemblyVoteConfiguration());
        builder.ApplyConfiguration(new AssemblyAuditConfiguration());
    }
}
