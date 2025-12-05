using CondotifyAPI.Domain.Enums.Resident;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class ResidentAccessDeviceConfiguration : IEntityTypeConfiguration<ResidentAccessDeviceDTO>
    {
        public void Configure(EntityTypeBuilder<ResidentAccessDeviceDTO> builder)
        {
            builder.ToTable("ResidentAccessDevices");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ResidentAccessCredentialId)
                   .IsRequired();

            builder.Property(x => x.DeviceId)
                   .IsRequired();

            builder.Property(x => x.DeviceType)
                   .HasConversion(new EnumToNumberConverter<DeviceTypeEnum, int>())
                   .IsRequired();

            builder.Property(x => x.ExternalUserId)
                   .HasMaxLength(100);

            builder.Property(x => x.ExternalCredentialId)
                   .HasMaxLength(100);

            builder.Property(x => x.ExtraJson)
                   .HasColumnType("jsonb");

            builder.Property(x => x.IsSynced)
                   .HasDefaultValue(false);

            builder.Property(x => x.LastSyncAt);

            builder.HasOne(x => x.Credential)
                   .WithMany(c => c.Devices)
                   .HasForeignKey(x => x.ResidentAccessCredentialId);
        }
    }
}
