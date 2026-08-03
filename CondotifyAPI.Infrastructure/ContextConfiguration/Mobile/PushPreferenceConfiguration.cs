using CondotifyAPI.Domain.DTO.Mobile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Mobile;

public sealed class PushPreferenceConfiguration : IEntityTypeConfiguration<PushPreferenceDTO>
{
    public void Configure(EntityTypeBuilder<PushPreferenceDTO> builder)
    {
        builder.ToTable("PushPreferences");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Category).HasConversion<int>().IsRequired();
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.Category }).IsUnique();
    }
}
