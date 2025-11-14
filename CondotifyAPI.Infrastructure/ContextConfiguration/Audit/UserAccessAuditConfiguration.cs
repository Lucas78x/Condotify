using CondotifyAPI.Domain.DTO.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Audit
{
    public class UserAccessAuditConfiguration : IEntityTypeConfiguration<UserAccessAuditDTO>
    {
        public void Configure(EntityTypeBuilder<UserAccessAuditDTO> builder)
        {
            builder.ToTable("UserAccessAudits");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.MacAddress)
                .HasMaxLength(17);

            builder.Property(a => a.IPAddress)
                .HasMaxLength(45); 

            builder.Property(a => a.CPU)
                .HasMaxLength(150);

            builder.Property(a => a.GPU)
                .HasMaxLength(150);

            builder.Property(a => a.RAM)
                .HasMaxLength(50);

            builder.Property(a => a.Action)
                .IsRequired();

            builder.Property(a => a.UserId)
                .IsRequired();
        }
    }
}
