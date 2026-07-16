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

            builder.Property(x => x.SyncStatus).IsRequired().HasDefaultValue(CondotifyAPI.Domain.Enums.AccessControl.CredentialSyncStatusEnum.Pending);
            builder.Property(x => x.AttemptCount).IsRequired().HasDefaultValue(0);
            builder.Property(x => x.NextAttemptAt);
            builder.Property(x => x.LastSuccessAt);
            builder.Property(x => x.LastErrorAt);
            builder.Property(x => x.RouteNames).HasMaxLength(500);
            builder.Property(x => x.PortalNumbers).HasMaxLength(200);

            builder.HasOne(x => x.Credential)
                   .WithMany(c => c.Devices)
                   .HasForeignKey(x => x.ResidentAccessCredentialId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(x => x.Device)
                   .WithMany()
                   .HasForeignKey(x => x.DeviceId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(x => new { x.SyncStatus, x.NextAttemptAt });
        }
    }
}
