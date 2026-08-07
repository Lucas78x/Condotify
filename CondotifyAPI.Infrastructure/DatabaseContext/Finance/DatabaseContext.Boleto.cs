using CondotifyAPI.Domain.DTO.Finance;
using CondotifyAPI.Infrastructure.ContextConfiguration.Finance;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<BoletoBatchDTO> BoletoBatches { get; set; }
    public DbSet<BoletoDocumentDTO> BoletoDocuments { get; set; }

    internal static void BoletoEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new BoletoBatchConfiguration());
        builder.ApplyConfiguration(new BoletoDocumentConfiguration());
    }
}
