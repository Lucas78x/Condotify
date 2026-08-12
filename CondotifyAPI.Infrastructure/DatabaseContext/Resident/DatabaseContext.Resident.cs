using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Infrastructure.ContextConfiguration.Resident;
using CondotifyAPI.Domain.Enums.Resident;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Infrastructure.ContextConfiguration.Vehicle;
using CondotifyAPI.Infrastructure.ContextConfiguration.Invitation;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<ResidentAccessDTO> Residents { get; set; }
    public DbSet<ResidentAccessCredentialDTO> ResidentAccessCredentials { get; set; }
    public DbSet<ResidentAccessDeviceDTO> ResidentAccessDevices { get; set; }
    public DbSet<ResidentUnitLinkDTO> ResidentUnitLinks { get; set; }
    public DbSet<VehicleDTO> Vehicles { get; set; }
    public DbSet<VehicleAccessAuditDTO> VehicleAccessAudits { get; set; }
    public DbSet<RegistrationInviteDTO> RegistrationInvites { get; set; }
    public DbSet<AccessVisitDTO> AccessVisits { get; set; }
    public DbSet<VisitFacialInviteDTO> VisitFacialInvites { get; set; }
    public DbSet<AccessWatchlistEntryDTO> AccessWatchlistEntries { get; set; }
    public DbSet<ResidentPasswordRecoveryTokenDTO> ResidentPasswordRecoveryTokens { get; set; }

    internal static void ResidentsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ResidentAccessConfiguration());
        builder.ApplyConfiguration(new ResidentUnitLinkConfiguration());
        builder.ApplyConfiguration(new VehicleConfiguration());
        builder.ApplyConfiguration(new VehicleAccessAuditConfiguration());
        builder.ApplyConfiguration(new RegistrationInviteConfiguration());
        builder.ApplyConfiguration(new AccessVisitConfiguration());
        builder.ApplyConfiguration(new VisitFacialInviteConfiguration());
        builder.ApplyConfiguration(new AccessWatchlistEntryConfiguration());
        builder.ApplyConfiguration(new ResidentPasswordRecoveryTokenConfiguration());
    }
}
