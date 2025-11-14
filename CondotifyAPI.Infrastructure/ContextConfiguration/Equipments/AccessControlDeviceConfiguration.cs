using CondotifyAPI.Domain.DTO.Equipments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class AccessControlDeviceConfiguration : IEntityTypeConfiguration<AccessControlDeviceDTO>
    {
        public void Configure(EntityTypeBuilder<AccessControlDeviceDTO> builder)
        {
            builder.ToTable("AccessControlDevices");

            builder.HasKey(d => d.Id);

            builder.Property(d => d.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(d => d.IPAddress)
                .IsRequired()
                .HasMaxLength(45); 

            builder.Property(d => d.Port)
                .IsRequired();

            builder.Property(d => d.Username)
                .HasMaxLength(100);

            builder.Property(d => d.Password)
                .HasMaxLength(150);

            builder.Property(d => d.MACAddress)
                .HasMaxLength(17);

            builder.Property(d => d.Model)
                .HasMaxLength(100);

            builder.Property(d => d.SerialNumber)
                .HasMaxLength(100);

            builder.Property(d => d.FirmwareVersion)
                .HasMaxLength(50);

            builder.Property(d => d.Type)
                .IsRequired();

            builder.Property(d => d.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(d => d.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(d => d.LastUpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.OwnsOne(l => l.Location, loc =>
            {
                loc.Property(p => p.X).HasColumnName("LocationX");
                loc.Property(p => p.Y).HasColumnName("LocationY");
            });

            builder.HasMany(d => d.Audit)
                .WithOne(a => a.Device)
                .HasForeignKey(a => a.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
