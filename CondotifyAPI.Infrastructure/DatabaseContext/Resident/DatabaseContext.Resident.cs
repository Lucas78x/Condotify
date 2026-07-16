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
    public DbSet<RegistrationInviteDTO> RegistrationInvites { get; set; }

    internal static void ResidentsEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ResidentAccessConfiguration());
        builder.ApplyConfiguration(new ResidentUnitLinkConfiguration());
        builder.ApplyConfiguration(new VehicleConfiguration());
        builder.ApplyConfiguration(new RegistrationInviteConfiguration());
    }
}
