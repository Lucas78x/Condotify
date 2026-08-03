using CondotifyAPI.Domain.DTO.Resident;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Resident;

public class ResidentPasswordRecoveryTokenConfiguration : IEntityTypeConfiguration<ResidentPasswordRecoveryTokenDTO>
{
    public void Configure(EntityTypeBuilder<ResidentPasswordRecoveryTokenDTO> builder)
    {
        builder.ToTable("ResidentPasswordRecoveryTokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CreatedIp).HasMaxLength(64);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.ResidentId, x.UsedAt });
    }
}
