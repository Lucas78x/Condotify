using CondotifyAPI.Domain.DTO.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class CFTVChannelConfiguration : IEntityTypeConfiguration<CFTVChannelDTO>
    {
        public void Configure(EntityTypeBuilder<CFTVChannelDTO> builder)
        {
            builder.ToTable("CFTVChannels");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ChannelNumber)
                .IsRequired();

            builder.Property(x => x.Name)
                .HasMaxLength(100);

            builder.Property(x => x.IsEnabled)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(x => x.ResidentVisible)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(x => x.RtspPath)
                .HasMaxLength(300);

            builder.HasIndex(x => new { x.CFTVDeviceId, x.ChannelNumber })
                .IsUnique(); 
        }
    }
}
