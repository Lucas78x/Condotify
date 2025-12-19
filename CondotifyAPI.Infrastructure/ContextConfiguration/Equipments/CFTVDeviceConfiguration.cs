using CondotifyAPI.Domain.DTO.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class CFTVDeviceConfiguration : IEntityTypeConfiguration<CFTVDeviceDTO>
    {
        public void Configure(EntityTypeBuilder<CFTVDeviceDTO> builder)
        {
            builder.ToTable("CFTVDevices");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(x => x.Password)
                .HasMaxLength(100);

            builder.Property(x => x.IpAddress)
                .IsRequired()
                .HasMaxLength(45); // IPv4 / IPv6

            builder.Property(x => x.HTTPPort)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(x => x.RTSPPort)
                .IsRequired()
                .HasMaxLength(10);

            builder
                .Property(x => x.IpType)
                .HasConversion(new ValueConverter<IpTypeEnum, int>(
                    x => (int)x,
                    x => (IpTypeEnum)x))
                .HasDefaultValue(IpTypeEnum.None)
                .IsRequired();

            builder
                .Property(x => x.Proportion)
                .HasConversion(new ValueConverter<ScreenProportionEnum, int>(
                    x => (int)x,
                    x => (ScreenProportionEnum)x))
                .HasDefaultValue(ScreenProportionEnum.None)
                .IsRequired();

            builder
                .Property(x => x.Mark)
                .HasConversion(new ValueConverter<MarkEnum, int>(
                    x => (int)x,
                    x => (MarkEnum)x))
                .HasDefaultValue(MarkEnum.None)
                .IsRequired();

            builder
                .HasOne(x => x.License)
                .WithMany(l => l.CFTVDevices)
                .HasForeignKey(x => x.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
