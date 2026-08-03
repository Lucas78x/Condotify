using CondotifyAPI.Domain.DTO.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using CondotifyAPI.Infrastructure.Security;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class CFTVDeviceConfiguration : IEntityTypeConfiguration<CFTVDeviceDTO>
    {
        public void Configure(EntityTypeBuilder<CFTVDeviceDTO> builder)
        {
            builder.ToTable("CFTVDevices");

            // PK
            builder.HasKey(x => x.Id);

            // Properties
            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(x => x.Username)
                .IsRequired()
                .HasMaxLength(80);

            builder.Property(x => x.Password)
                .HasConversion(new EquipmentSecretConverter())
                .HasMaxLength(512);

            builder.Property(x => x.IpAddress)
                .IsRequired()
                .HasMaxLength(45); // IPv4 / IPv6

            builder.Property(x => x.HTTPPort)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(x => x.RTSPPort)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(x => x.MaxChannels)
                .IsRequired()
                .HasDefaultValue(1);

            // Enums
            builder.Property(x => x.DeviceType)
                .HasConversion<int>()
                .HasDefaultValue(CFTVDeviceTypeEnum.Camera)
                .IsRequired();

            builder.Property(x => x.IpType)
                .HasConversion<int>()
                .HasDefaultValue(IpTypeEnum.None)
                .IsRequired();

            builder.Property(x => x.Proportion)
                .HasConversion<int>()
                .HasDefaultValue(ScreenProportionEnum.None)
                .IsRequired();

            builder.Property(x => x.Mark)
                .HasConversion<int>()
                .HasDefaultValue(MarkEnum.None)
                .IsRequired();

            builder.Property(x => x.IsActive)
                .HasDefaultValue(false);

            builder.Property(x => x.ResidentVisible)
                .HasDefaultValue(false);

            builder.Property(x => x.HealthMessage)
                .HasMaxLength(300)
                .HasDefaultValue(string.Empty);

            builder.HasMany(x => x.Channels)
                .WithOne(c => c.Device)
                .HasForeignKey(c => c.CFTVDeviceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => new { x.LicenseId, x.IpAddress, x.HTTPPort, x.RTSPPort })
                .IsUnique();
        }
    }
}
