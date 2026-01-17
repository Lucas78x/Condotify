using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Location;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.License
{
    public class LicenseConfiguration : IEntityTypeConfiguration<LicenseDTO>
    {
        public void Configure(EntityTypeBuilder<LicenseDTO> builder)
        {
            builder.ToTable("Licenses");

            builder.HasKey(l => l.Id);

            builder.Property(l => l.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(l => l.CNPJ)
                .HasMaxLength(18);

            builder.Property(l => l.ExpireDate)
                .IsRequired();

            builder.Property(l => l.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(l => l.Organization)
                .IsRequired();

            builder
               .Property(x => x.Organization)
               .HasConversion(new ValueConverter<OrganizationTypeEnum, int>(
                   x => (int)x,
                   x => (OrganizationTypeEnum)x))
               .HasDefaultValue(OrganizationTypeEnum.Default)
               .IsRequired();

            builder
               .Property(x => x.Building)
               .HasConversion(new ValueConverter<BuildingTypeEnum, int>(
                   x => (int)x,
                   x => (BuildingTypeEnum)x))
               .HasDefaultValue(BuildingTypeEnum.Default)
               .IsRequired();

            builder
                 .Property(x => x.Type)
                 .HasConversion(new ValueConverter<LicenseTypeEnum, int>(
                     x => (int)x,
                     x => (LicenseTypeEnum)x))
                 .HasDefaultValue(LicenseTypeEnum.Default)
                 .IsRequired();

            builder.HasOne(l => l.Enterprise)
                .WithMany(e => e.Licenses)
                .HasForeignKey(l => l.EnterpriseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(l => l.Blocks)
                .WithOne(b => b.License)
                .HasForeignKey(b => b.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(l => l.Devices)
                .WithOne(d => d.License)
                .HasForeignKey(d => d.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(l => l.CFTVDevices)
              .WithOne(b => b.License)
              .HasForeignKey(b => b.LicenseId)
              .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(l => l.Deliveries)
             .WithOne(b => b.License)
             .HasForeignKey(b => b.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(l => l.Tickets)
             .WithOne(b => b.License)
             .HasForeignKey(b => b.LicenseId)
             .OnDelete(DeleteBehavior.Cascade);

            builder.OwnsOne(l => l.Location, loc =>
            {
                loc.Property(p => p.X).HasColumnName("LocationX");
                loc.Property(p => p.Y).HasColumnName("LocationY");
            });
        }
    }
}
