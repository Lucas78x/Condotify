using CondotifyAPI.Domain.DTO.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.User;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshTokenDTO>
{
    public void Configure(EntityTypeBuilder<RefreshTokenDTO> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SubjectType).IsRequired().HasMaxLength(20);
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ReplacedByHash).HasMaxLength(64);
        builder.Property(x => x.DeviceLabel).HasMaxLength(200);
        builder.Property(x => x.CreatedIp).HasMaxLength(64);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.SubjectId, x.SubjectType });
    }
}
