using CondotifyAPI.Domain.DTO.Mobile;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Mobile;

public sealed class PushNotificationConfiguration : IEntityTypeConfiguration<PushNotificationDTO>
{
    public void Configure(EntityTypeBuilder<PushNotificationDTO> builder)
    {
        builder.ToTable("PushNotifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SubjectType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Category).HasConversion<int>().IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Route).HasMaxLength(300).IsRequired();
        builder.Property(x => x.DeepLink).HasMaxLength(500).IsRequired();
        builder.Property(x => x.DataJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(x => x.DeduplicationKey).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.DeduplicationKey }).IsUnique();
        builder.HasIndex(x => new { x.SubjectType, x.SubjectId, x.CreatedAt });
    }
}

public sealed class PushDeliveryConfiguration : IEntityTypeConfiguration<PushDeliveryDTO>
{
    public void Configure(EntityTypeBuilder<PushDeliveryDTO> builder)
    {
        builder.ToTable("PushDeliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.LeaseOwner).HasMaxLength(180);
        builder.Property(x => x.ProviderMessageId).HasMaxLength(300);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.HasIndex(x => new { x.NotificationId, x.InstallationId }).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptAt, x.CreatedAt });
        builder.HasOne(x => x.Notification).WithMany(x => x.Deliveries).HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Installation).WithMany(x => x.Deliveries).HasForeignKey(x => x.InstallationId).OnDelete(DeleteBehavior.Cascade);
    }
}
