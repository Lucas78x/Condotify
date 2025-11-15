using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Domain.DTO.Equipments;


namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{

    public class ResidentAccessControlConfiguration : IEntityTypeConfiguration<ResidentAccessControlDTO>
    {
        public void Configure(EntityTypeBuilder<ResidentAccessControlDTO> builder)
        {
            builder.ToTable("ResidentAccessControl");

            builder.HasKey(e => e.Id);

            builder.Property(e => e.UserId)
                .IsRequired();

            builder.Property(e => e.CardNumber)
                .HasMaxLength(50);

            builder.Property(e => e.TagNumber)
                .HasMaxLength(50);

            builder.Property(e => e.Type)
                .HasConversion(new ValueConverter<DeviceTypeEnum, int>(
                    x => (int)x,
                    x => (DeviceTypeEnum)x))
                .IsRequired();

            builder.Property(e => e.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(e => e.ValidFrom)
                .IsRequired();

            builder.Property(e => e.ValidTo)
                .IsRequired();

            builder.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(e => e.UpdatedAt);
        }
    }
}
