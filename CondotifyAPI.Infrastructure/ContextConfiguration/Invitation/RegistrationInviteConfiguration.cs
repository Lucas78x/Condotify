using CondotifyAPI.Domain.DTO.Invitation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Invitation;

public class RegistrationInviteConfiguration : IEntityTypeConfiguration<RegistrationInviteDTO>
{
    public void Configure(EntityTypeBuilder<RegistrationInviteDTO> builder)
    {
        builder.ToTable("RegistrationInvites");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Contact).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.Property(x => x.CreatedBy).HasMaxLength(150);
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.SentAt });
        builder.HasIndex(x => x.TokenHash).IsUnique();
    }
}
