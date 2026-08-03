using CondotifyAPI.Domain.DTO.Mobile;
using CondotifyAPI.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Mobile;

public sealed class PushInstallationConfiguration : IEntityTypeConfiguration<PushInstallationDTO>
{
    public void Configure(EntityTypeBuilder<PushInstallationDTO> builder)
    {
        builder.ToTable("PushInstallations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.InstallationId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.PushToken).HasConversion(new PushTokenConverter()).HasMaxLength(4096).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.Platform).HasConversion<int>().IsRequired();
        builder.Property(x => x.DeviceName).HasMaxLength(160);
        builder.Property(x => x.AppVersion).HasMaxLength(40);
        builder.Property(x => x.Locale).HasMaxLength(20);
        builder.Property(x => x.TimeZone).HasMaxLength(100);
        builder.HasIndex(x => x.InstallationId).IsUnique();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.IsActive });
    }
}
