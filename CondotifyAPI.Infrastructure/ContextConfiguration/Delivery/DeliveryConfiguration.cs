using CondotifyAPI.Domain.DTO.Delivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Delivery
{
    public class DeliveryConfiguration : IEntityTypeConfiguration<DeliveryDTO>
    {
        public void Configure(EntityTypeBuilder<DeliveryDTO> builder)
        {
            builder.ToTable("Deliveries");

            builder.HasKey(d => d.Id);

            builder
                .Property(d => d.Type)
                .HasConversion(new ValueConverter<DeliveryTypeEnum, int>(
                    x => (int)x,
                    x => (DeliveryTypeEnum)x))
                .IsRequired();

            builder
                .Property(d => d.Status)
                .HasConversion(new ValueConverter<DeliveryStatusEnum, int>(
                    x => (int)x,
                    x => (DeliveryStatusEnum)x))
                .IsRequired();

            builder.Property(d => d.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(d => d.Description)
                .HasMaxLength(500);

            builder.Property(d => d.TrackingCode)
                .HasMaxLength(100);

            builder.Property(d => d.PhotoUrl)
                .HasMaxLength(500);

            builder.Property(d => d.DeliveryProofUrl)
                .HasMaxLength(500);

            builder.Property(d => d.ReceivedBy)
                .HasMaxLength(200);

            builder.Property(d => d.DeliveredTo)
                .HasMaxLength(200);

            builder.Property(d => d.ReceivedAt);
            builder.Property(d => d.DeliveredAt);

            builder.Property(d => d.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(d => d.UpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(d => d.ReceivedId)
                .IsRequired(false);

            builder.Property(d => d.DeliveredToId)
                .IsRequired(false);

            builder.HasIndex(d => new { d.LicenseId, d.Status, d.CreatedAt });
            builder.HasIndex(d => new { d.LicenseId, d.TrackingCode });
        }
    }
}
