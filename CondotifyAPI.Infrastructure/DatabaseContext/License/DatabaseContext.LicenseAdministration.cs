using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Infrastructure.ContextConfiguration.License;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<LicenseUserAccessDTO> LicenseUserAccesses { get; set; }
    public DbSet<LicenseCredentialPolicyDTO> LicenseCredentialPolicies { get; set; }

    internal static void LicenseAdministrationEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new LicenseUserAccessConfiguration());
        builder.ApplyConfiguration(new LicenseCredentialPolicyConfiguration());
    }
}
