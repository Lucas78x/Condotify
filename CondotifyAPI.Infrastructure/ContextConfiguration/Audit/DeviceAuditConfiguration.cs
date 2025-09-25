using CondotifyAPI.DTO.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Audit
{
    public class DeviceAuditConfiguration : IEntityTypeConfiguration<DeviceAuditDTO>
    {
        public void Configure(EntityTypeBuilder<DeviceAuditDTO> builder)
        {
            builder.ToTable("DeviceAudits");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.Action)
                .IsRequired();

            builder.Property(a => a.ChangedFields)
                .HasMaxLength(500);

            builder.Property(a => a.Timestamp)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(a => a.UserId)
                .IsRequired();

            builder.Property(a => a.UserName)
                .HasMaxLength(150);
        }
    }
}
