using CondotifyAPI.Domain.DTO.Resident;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Resident
{
    public class ResidentAccessConfiguration : IEntityTypeConfiguration<ResidentAccessDTO>
    {
        public void Configure(EntityTypeBuilder<ResidentAccessDTO> builder)
        {
            builder.ToTable("Resident");

            builder.HasKey(r => r.Id);

            builder.Property(r => r.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(r => r.Email)
                .HasMaxLength(150);

            builder.Property(r => r.Password)
                .HasMaxLength(150);

            builder.Property(r => r.PhoneNumber)
                .HasMaxLength(20);

            builder.Property(r => r.CPF)
                .HasMaxLength(14);

            builder.Property(r => r.RG)
                .HasMaxLength(12);

            builder.Property(r => r.BirthDate)
                .HasMaxLength(20);

            builder.Property(r => r.ApartmentNumber)
                .HasMaxLength(20);

            builder
                .Property(x => x.AccessType)
                .HasConversion(new ValueConverter<ResidentAccessTypeEnum, int>(
                    x => (int)x,
                    x => (ResidentAccessTypeEnum)x))
                .HasDefaultValue(ResidentAccessTypeEnum.Default)
                .IsRequired();

            builder.Property(r => r.FirstAccess)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(r => r.Temporary)
                .IsRequired()
                .HasDefaultValue(false);

            builder.Property(r => r.Expire)
                .IsRequired();

            builder.Property(r => r.LastAccess)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(r => r.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.HasMany(r => r.AccessCredentials)
                .WithOne(d => d.Resident)   
                .HasForeignKey(d => d.ResidentId); 

        }
    }
}
