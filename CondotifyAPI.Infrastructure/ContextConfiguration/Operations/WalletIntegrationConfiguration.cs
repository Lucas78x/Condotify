using CondotifyAPI.Domain.DTO.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Operations;

public sealed class WalletIntegrationConfiguration : IEntityTypeConfiguration<WalletIntegrationDTO>
{
    public void Configure(EntityTypeBuilder<WalletIntegrationDTO> builder)
    {
        builder.ToTable("WalletIntegrations");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.EnterpriseId, x.Provider }).IsUnique();
        builder.HasIndex(x => new { x.EnterpriseId, x.IsActive });

        builder.Property(x => x.Provider).HasConversion<int>();
        builder.Property(x => x.AuthenticationMode).HasConversion<int>();
        builder.Property(x => x.IssuerId).HasMaxLength(80);
        builder.Property(x => x.ServiceAccountEmail).HasMaxLength(320);
        builder.Property(x => x.ClassSuffix).HasMaxLength(120);
        builder.Property(x => x.PassTypeIdentifier).HasMaxLength(180);
        builder.Property(x => x.TeamIdentifier).HasMaxLength(80);
        builder.Property(x => x.CredentialSecret).HasColumnType("text");
        builder.Property(x => x.CredentialPassword).HasColumnType("text");
        builder.Property(x => x.IntermediateCertificate).HasColumnType("text");
        builder.Property(x => x.CredentialFingerprint).HasMaxLength(160);
        builder.Property(x => x.LastValidationMessage).HasMaxLength(500);
        builder.Property(x => x.UpdatedByName).HasMaxLength(200);
    }
}
