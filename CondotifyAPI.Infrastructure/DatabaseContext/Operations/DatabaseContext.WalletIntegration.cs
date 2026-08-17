using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure.ContextConfiguration.Operations;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<WalletIntegrationDTO> WalletIntegrations { get; set; }

    internal static void WalletIntegrationEntityConfiguration(ModelBuilder builder) =>
        builder.ApplyConfiguration(new WalletIntegrationConfiguration());
}
