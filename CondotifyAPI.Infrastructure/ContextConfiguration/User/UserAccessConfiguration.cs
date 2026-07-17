using CondotifyAPI.Domain.DTO.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using CondotifyAPI.Infrastructure.Security;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.User
{
    public class UserAccessConfiguration : IEntityTypeConfiguration<UserAccessDTO>
    {
        public void Configure(EntityTypeBuilder<UserAccessDTO> builder)
        {
            builder.ToTable("UserAccess");

            builder.HasKey(u => u.Id);

            builder.Property(u => u.Name)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(u => u.Email)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(u => u.PasswordHash)
                   .IsRequired()
                   .HasMaxLength(256);

            builder.Property(u => u.PhoneNumber)
                .HasMaxLength(20);

            builder.Property(u => u.CPF)
                .HasMaxLength(14);

            builder.Property(u => u.RG)
                .HasMaxLength(12);

            builder.Property(u => u.BirthDate)
                .HasMaxLength(20);

            builder.Property(u => u.AccessType)
                .IsRequired();

            builder.Property(u => u.FirstAccess)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(u => u.MfaSecret)
                .HasConversion(new EquipmentSecretConverter())
                .HasMaxLength(512);
            builder.Property(u => u.MfaEnabled).IsRequired().HasDefaultValue(false);
            builder.Property(u => u.MfaRecoveryCodeHashesJson).HasColumnType("jsonb").HasDefaultValue("[]");
            builder.Property(u => u.MfaChallengeHash).HasMaxLength(64);

            builder.Property(u => u.LastAccess)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.Property(u => u.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            builder.HasMany(u => u.Audit)
                .WithOne(a => a.User)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(u => u.Email)
                .IsUnique();

            builder.HasIndex(u => u.CPF)
                .IsUnique().HasFilter("\"CPF\" <> ''");

            builder.HasIndex(u => u.RG)
                .IsUnique().HasFilter("\"RG\" <> ''");

            builder.HasIndex(u => u.PhoneNumber)
                .IsUnique().HasFilter("\"PhoneNumber\" <> ''");
        }
    }
}
