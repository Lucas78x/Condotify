using CondotifyAPI.Domain.DTO.License;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.License;

public class LicenseUserAccessConfiguration : IEntityTypeConfiguration<LicenseUserAccessDTO>
{
    public void Configure(EntityTypeBuilder<LicenseUserAccessDTO> builder)
    {
        builder.ToTable("LicenseUserAccesses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion(new EnumToNumberConverter<LicenseAccessRoleEnum, int>());
        builder.Property(x => x.Permissions).HasConversion<long>().IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithMany(x => x.UserAccesses).HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.User).WithMany(x => x.LicenseAccesses).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.UserId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.IsActive });
    }
}

public class LicenseCredentialPolicyConfiguration : IEntityTypeConfiguration<LicenseCredentialPolicyDTO>
{
    public void Configure(EntityTypeBuilder<LicenseCredentialPolicyDTO> builder)
    {
        builder.ToTable("LicenseCredentialPolicies");
        builder.HasKey(x => x.LicenseId);
        builder.Property(x => x.QrCodeValidityMinutes).HasDefaultValue(1440);
        builder.Property(x => x.AllowQrCodeRenewal).HasDefaultValue(true);
        builder.Property(x => x.MaxQrCodeRenewals).HasDefaultValue(2);
        builder.Property(x => x.QrCodeRenewalMinutes).HasDefaultValue(1440);
        builder.Property(x => x.TemporaryFaceValidityMinutes).HasDefaultValue(1440);
        builder.Property(x => x.MaxTemporaryFaceValidityMinutes).HasDefaultValue(10080);
        builder.Property(x => x.RequireFacePhoto).HasDefaultValue(true);
        builder.Property(x => x.AutoDeactivateExpiredCredentials).HasDefaultValue(true);
        builder.Property(x => x.RemoveExpiredCredentialsFromDevices).HasDefaultValue(true);
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.License).WithOne(x => x.CredentialPolicy).HasForeignKey<LicenseCredentialPolicyDTO>(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
    }
}
