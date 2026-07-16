using CondotifyAPI.Domain.DTO.Resident;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Equipments
{
    public class ResidentAccessCredentialConfiguration : IEntityTypeConfiguration<ResidentAccessCredentialDTO>
    {
        public void Configure(EntityTypeBuilder<ResidentAccessCredentialDTO> builder)
        {
            builder.ToTable("ResidentAccessCredentials");

            builder.HasKey(x => x.Id);

            builder.Property(x => x.ResidentId)
                   .IsRequired();

            builder.HasOne(x => x.Resident)
                    .WithMany(r => r.AccessCredentials)
                   .HasForeignKey(x => x.ResidentId);

            builder.Property(x => x.CredentialType)
                   .HasConversion(new EnumToNumberConverter<AccessCredentialTypeEnum, int>())
                   .IsRequired();

            builder.Property(x => x.Identifier)
                   .HasMaxLength(150)
                   .IsRequired();

            builder.Property(x => x.IsActive)
                   .IsRequired()
                   .HasDefaultValue(true);

            builder.Property(x => x.IsTemporary).IsRequired().HasDefaultValue(false);
            builder.Property(x => x.RenewalCount).IsRequired().HasDefaultValue(0);
            builder.Property(x => x.MaxRenewals).IsRequired().HasDefaultValue(0);
            builder.Property(x => x.UseCount).IsRequired().HasDefaultValue(0);
            builder.Property(x => x.MaxUses);

            builder.Property(x => x.ValidFrom)
                   .IsRequired();

            builder.Property(x => x.ValidTo)
                   .IsRequired();

            builder.Property(x => x.CreatedAt)
                   .IsRequired()
                   .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(x => x.UpdatedAt);
        }
    }
}
