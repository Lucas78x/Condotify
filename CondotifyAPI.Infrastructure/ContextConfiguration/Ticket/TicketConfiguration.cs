using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.DTO.Unit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Ticket
{
    public class TicketConfiguration : IEntityTypeConfiguration<TicketDTO>
    {
        public void Configure(EntityTypeBuilder<TicketDTO> builder)
        {
            builder.ToTable("Tickets");

            builder.HasKey(t => t.Id);

            builder.Property(t => t.UnitId)
                .IsRequired();

            builder.Property(t => t.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(t => t.Barcode)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(t => t.BarcodeUrl)
                .HasMaxLength(500);

            builder.Property(t => t.CreatedDate)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(t => t.ExpiredDate)
                .IsRequired();

            builder
                .Property(t => t.Status)
                .HasConversion(new ValueConverter<TicketStatusTypeEnum, int>(
                    x => (int)x,
                    x => (TicketStatusTypeEnum)x))
                .IsRequired();

            builder.Property(t => t.IsSecondCopy)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(t => t.OriginalTicketId)
                .IsRequired(false);

            builder.HasMany(t => t.Audit)
                .WithOne(a => a.Ticket)
                .HasForeignKey(a => a.TicketId)
                .OnDelete(DeleteBehavior.Cascade);

            // UnitId nao tinha relacao configurada: era uma coluna solta sem
            // FK no banco. A camada de aplicacao ja valida a unidade antes de
            // criar o ticket (CondotifyCommandsRepository), isso so torna a
            // garantia explicita no schema. Cascade segue o mesmo padrao
            // usado por UnitConfiguration para o restante dos filhos da unidade.
            builder.HasOne<UnitDTO>()
                .WithMany()
                .HasForeignKey(t => t.UnitId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => new { t.LicenseId, t.Status, t.CreatedDate });
        }
    }
}
