using CondotifyAPI.Domain.DTO.Resident;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Resident;

public class ResidentUnitLinkConfiguration : IEntityTypeConfiguration<ResidentUnitLinkDTO>
{
    public void Configure(EntityTypeBuilder<ResidentUnitLinkDTO> builder)
    {
        builder.ToTable("ResidentUnitLinks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Relationship).HasConversion<int>().IsRequired();
        builder.Property(x => x.Description).HasMaxLength(200);
        builder.Property(x => x.IsPrimary).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => new { x.ResidentId, x.UnitId }).IsUnique();
        builder.HasIndex(x => new { x.UnitId, x.IsActive });
    }
}
