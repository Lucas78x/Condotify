using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CondotifyAPI.DTO.Enterprise;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Enterprise;

public class EnterpriseConfiguration : IEntityTypeConfiguration<EnterpriseDTO>
{
    public void Configure(EntityTypeBuilder<EnterpriseDTO> builder)
    {
        builder.ToTable("Enterprise");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.CNPJ)
            .HasMaxLength(18);

        builder.Property(e => e.Email)
            .HasMaxLength(150);

        builder.Property(e => e.Phone)
            .HasMaxLength(20);

        builder.Property(e => e.Mobile)
            .HasMaxLength(20);

        builder.Property(e => e.Website)
            .HasMaxLength(200);

        builder.Property(e => e.Street).HasMaxLength(200);
        builder.Property(e => e.Number).HasMaxLength(20);
        builder.Property(e => e.Complement).HasMaxLength(100);
        builder.Property(e => e.Neighborhood).HasMaxLength(100);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.State).HasMaxLength(2);
        builder.Property(e => e.PostalCode).HasMaxLength(20);
        builder.Property(e => e.Country).HasMaxLength(100);

        builder.Property(e => e.ContactPerson).HasMaxLength(150);
        builder.Property(e => e.ContactEmail).HasMaxLength(150);
        builder.Property(e => e.ContactPhone).HasMaxLength(20);

        builder.Property(e => e.LogoUrl).HasMaxLength(300);
        builder.Property(e => e.Notes).HasMaxLength(1000);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.Property(e => e.UpdatedAt)
            .IsRequired(false);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasMany(e => e.Licenses)
               .WithOne(l => l.Enterprise)
               .HasForeignKey(l => l.EnterpriseId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
