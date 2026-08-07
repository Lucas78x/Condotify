using CondotifyAPI.Domain.DTO.Documents;
using CondotifyAPI.Infrastructure.ContextConfiguration.Documents;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<ResourceDocumentDTO> ResourceDocuments { get; set; }

    internal static void ResourceDocumentEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ResourceDocumentConfiguration());
    }
}
