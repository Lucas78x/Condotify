using CondotifyAPI.Domain.DTO.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Audit
{
    public class TicketAuditConfiguration : IEntityTypeConfiguration<TicketAuditDTO>
    {
        public void Configure(EntityTypeBuilder<TicketAuditDTO> builder)
        {
            builder.ToTable("TicketAudits");

            builder.HasKey(a => a.Id);

            builder.Property(a => a.Action)
                .HasConversion(new ValueConverter<ActionTypeEnum, int>(
                    x => (int)x,
                    x => (ActionTypeEnum)x))
                .IsRequired();

            builder.Property(a => a.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // 🔗 User
            builder.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
