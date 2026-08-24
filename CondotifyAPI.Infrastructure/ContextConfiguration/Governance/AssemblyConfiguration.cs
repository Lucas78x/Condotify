using CondotifyAPI.Domain.DTO.Governance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Governance;

public sealed class CondominiumAssemblyConfiguration : IEntityTypeConfiguration<CondominiumAssemblyDTO>
{
    public void Configure(EntityTypeBuilder<CondominiumAssemblyDTO> builder)
    {
        builder.ToTable("CondominiumAssemblies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(8000);
        builder.Property(x => x.Location).HasMaxLength(300);
        builder.Property(x => x.MeetingUrl).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.Property(x => x.Type).HasConversion<int>();
        builder.Property(x => x.Format).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.VoteVisibility).HasConversion<int>();
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.StartsAt });
    }
}

public sealed class AssemblyAgendaItemConfiguration : IEntityTypeConfiguration<AssemblyAgendaItemDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyAgendaItemDTO> builder)
    {
        builder.ToTable("AssemblyAgendaItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(240);
        builder.Property(x => x.Description).IsRequired().HasMaxLength(6000);
        builder.Property(x => x.QuorumPercentage).HasPrecision(7, 4);
        builder.Property(x => x.ApprovalPercentage).HasPrecision(7, 4);
        builder.HasOne(x => x.Assembly).WithMany(x => x.AgendaItems).HasForeignKey(x => x.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AssemblyId, x.Order }).IsUnique();
    }
}

public sealed class AssemblyVoteOptionConfiguration : IEntityTypeConfiguration<AssemblyVoteOptionDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyVoteOptionDTO> builder)
    {
        builder.ToTable("AssemblyVoteOptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Label).IsRequired().HasMaxLength(180);
        builder.HasOne(x => x.AgendaItem).WithMany(x => x.Options).HasForeignKey(x => x.AgendaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AgendaItemId, x.Order }).IsUnique();
    }
}

public sealed class AssemblyEligibleUnitConfiguration : IEntityTypeConfiguration<AssemblyEligibleUnitDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyEligibleUnitDTO> builder)
    {
        builder.ToTable("AssemblyEligibleUnits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Weight).HasPrecision(12, 4);
        builder.Property(x => x.IneligibilityReason).HasMaxLength(300);
        builder.HasOne(x => x.Assembly).WithMany(x => x.EligibleUnits).HasForeignKey(x => x.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AssemblyId, x.UnitId }).IsUnique();
    }
}

public sealed class AssemblyAttendanceConfiguration : IEntityTypeConfiguration<AssemblyAttendanceDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyAttendanceDTO> builder)
    {
        builder.ToTable("AssemblyAttendances");
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Assembly).WithMany(x => x.Attendances).HasForeignKey(x => x.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Resident).WithMany().HasForeignKey(x => x.ResidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AssemblyId, x.UnitId }).IsUnique();
    }
}

public sealed class AssemblyVoteConfiguration : IEntityTypeConfiguration<AssemblyVoteDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyVoteDTO> builder)
    {
        builder.ToTable("AssemblyVotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Weight).HasPrecision(12, 4);
        builder.HasOne(x => x.Assembly).WithMany(x => x.Votes).HasForeignKey(x => x.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.AgendaItem).WithMany(x => x.Votes).HasForeignKey(x => x.AgendaItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Option).WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Resident).WithMany().HasForeignKey(x => x.ResidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.AgendaItemId, x.UnitId }).IsUnique();
        builder.HasIndex(x => new { x.AssemblyId, x.CastAt });
    }
}

public sealed class AssemblyAuditConfiguration : IEntityTypeConfiguration<AssemblyAuditDTO>
{
    public void Configure(EntityTypeBuilder<AssemblyAuditDTO> builder)
    {
        builder.ToTable("AssemblyAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(80);
        builder.Property(x => x.ActorType).IsRequired().HasMaxLength(30);
        builder.Property(x => x.ActorName).HasMaxLength(150);
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");
        builder.HasOne(x => x.Assembly).WithMany(x => x.Audits).HasForeignKey(x => x.AssemblyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AssemblyId, x.CreatedAt });
    }
}
