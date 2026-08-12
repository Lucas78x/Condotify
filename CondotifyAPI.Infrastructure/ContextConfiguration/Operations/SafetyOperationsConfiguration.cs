using CondotifyAPI.Domain.DTO.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Operations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<IncidentDTO>
{
    public void Configure(EntityTypeBuilder<IncidentDTO> builder)
    {
        builder.ToTable("Incidents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.RelatedResourceType).HasMaxLength(80);
        builder.Property(x => x.AssignedToName).HasMaxLength(150);
        builder.Property(x => x.ReportedByName).HasMaxLength(150);
        builder.Property(x => x.LocationLabel).HasMaxLength(240);
        builder.Property(x => x.Resolution).HasMaxLength(2000);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.CreatedAt });
        builder.HasIndex(x => new { x.RelatedResourceType, x.RelatedResourceId });
    }
}

public sealed class IncidentTimelineEntryConfiguration : IEntityTypeConfiguration<IncidentTimelineEntryDTO>
{
    public void Configure(EntityTypeBuilder<IncidentTimelineEntryDTO> builder)
    {
        builder.ToTable("IncidentTimelineEntries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.ReferenceType).HasMaxLength(80);
        builder.Property(x => x.ReferenceUrl).HasMaxLength(1000);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb").HasDefaultValue("{}");
        builder.Property(x => x.ActorName).HasMaxLength(150);
        builder.Property(x => x.VisibleToResident).HasDefaultValue(false);
        builder.HasOne(x => x.Incident).WithMany(x => x.Timeline).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.IncidentId, x.CreatedAt });
    }
}

public sealed class MaintenancePolicyConfiguration : IEntityTypeConfiguration<MaintenancePolicyDTO>
{
    public void Configure(EntityTypeBuilder<MaintenancePolicyDTO> builder)
    {
        builder.ToTable("MaintenancePolicies");
        builder.HasKey(x => x.LicenseId);
        builder.Property(x => x.UpdatedBy).HasMaxLength(150);
        builder.HasOne(x => x.License).WithOne().HasForeignKey<MaintenancePolicyDTO>(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MaintenanceProviderConfiguration : IEntityTypeConfiguration<MaintenanceProviderDTO>
{
    public void Configure(EntityTypeBuilder<MaintenanceProviderDTO> builder)
    {
        builder.ToTable("MaintenanceProviders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Specialty).HasMaxLength(150);
        builder.Property(x => x.ContactName).HasMaxLength(150);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.IsActive });
    }
}

public sealed class PreventiveMaintenancePlanConfiguration : IEntityTypeConfiguration<PreventiveMaintenancePlanDTO>
{
    public void Configure(EntityTypeBuilder<PreventiveMaintenancePlanDTO> builder)
    {
        builder.ToTable("PreventiveMaintenancePlans");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Description).HasMaxLength(3000);
        builder.Property(x => x.LocationLabel).HasMaxLength(240);
        builder.Property(x => x.DefaultAssignedToName).HasMaxLength(150);
        builder.Property(x => x.EstimatedCost).HasPrecision(14, 2);
        builder.Property(x => x.ChecklistTemplateJson).HasColumnType("jsonb").HasDefaultValue("[]");
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.DefaultProvider).WithMany().HasForeignKey(x => x.DefaultProviderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.IsActive, x.NextDueAt });
    }
}

public sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrderDTO>
{
    public void Configure(EntityTypeBuilder<WorkOrderDTO> builder)
    {
        builder.ToTable("WorkOrders");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.LocationLabel).HasMaxLength(240);
        builder.Property(x => x.AssignedToName).HasMaxLength(150);
        builder.Property(x => x.EstimatedCost).HasPrecision(14, 2);
        builder.Property(x => x.ActualCost).HasPrecision(14, 2);
        builder.Property(x => x.CompletionNotes).HasMaxLength(2000);
        builder.Property(x => x.CreatedByName).HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Incident).WithMany(x => x.WorkOrders).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.PreventivePlan).WithMany(x => x.WorkOrders).HasForeignKey(x => x.PreventivePlanId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.Provider).WithMany(x => x.WorkOrders).HasForeignKey(x => x.ProviderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.LicenseId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.DueAt });
        builder.HasIndex(x => new { x.PreventivePlanId, x.ScheduledFor }).IsUnique();
    }
}

public sealed class WorkOrderChecklistItemConfiguration : IEntityTypeConfiguration<WorkOrderChecklistItemDTO>
{
    public void Configure(EntityTypeBuilder<WorkOrderChecklistItemDTO> builder)
    {
        builder.ToTable("WorkOrderChecklistItems");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(300);
        builder.Property(x => x.CompletedByName).HasMaxLength(150);
        builder.HasOne(x => x.WorkOrder).WithMany(x => x.Checklist).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.WorkOrderId, x.SortOrder }).IsUnique();
    }
}

public sealed class WorkOrderActivityConfiguration : IEntityTypeConfiguration<WorkOrderActivityDTO>
{
    public void Configure(EntityTypeBuilder<WorkOrderActivityDTO> builder)
    {
        builder.ToTable("WorkOrderActivities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(2000);
        builder.Property(x => x.ActorName).HasMaxLength(150);
        builder.HasOne(x => x.WorkOrder).WithMany(x => x.Activities).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.WorkOrderId, x.CreatedAt });
    }
}

public sealed class IncidentAttachmentConfiguration : IEntityTypeConfiguration<IncidentAttachmentDTO>
{
    public void Configure(EntityTypeBuilder<IncidentAttachmentDTO> builder)
    {
        builder.ToTable("IncidentAttachments", table => table.HasCheckConstraint(
            "CK_IncidentAttachments_Target", "(\"IncidentId\" IS NOT NULL) OR (\"WorkOrderId\" IS NOT NULL)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MediaReference).IsRequired().HasMaxLength(300);
        builder.Property(x => x.FileName).HasMaxLength(260);
        builder.Property(x => x.ContentType).HasMaxLength(100);
        builder.Property(x => x.Caption).HasMaxLength(500);
        builder.Property(x => x.UploadedByName).HasMaxLength(150);
        builder.HasOne(x => x.Incident).WithMany(x => x.Attachments).HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.WorkOrder).WithMany(x => x.Attachments).HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}

public sealed class AutomationRuleConfiguration : IEntityTypeConfiguration<AutomationRuleDTO>
{
    public void Configure(EntityTypeBuilder<AutomationRuleDTO> builder)
    {
        builder.ToTable("AutomationRules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.CreatedByName).HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
        builder.HasIndex(x => new { x.IsEnabled, x.LastEvaluatedAt });
    }
}

public sealed class AutomationExecutionConfiguration : IEntityTypeConfiguration<AutomationExecutionDTO>
{
    public void Configure(EntityTypeBuilder<AutomationExecutionDTO> builder)
    {
        builder.ToTable("AutomationExecutions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Fingerprint).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.Summary).IsRequired().HasMaxLength(1000);
        builder.HasOne(x => x.Rule).WithMany(x => x.Executions).HasForeignKey(x => x.RuleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.Fingerprint).IsUnique();
        builder.HasIndex(x => new { x.RuleId, x.CreatedAt });
    }
}

public sealed class EmergencySessionConfiguration : IEntityTypeConfiguration<EmergencySessionDTO>
{
    public void Configure(EntityTypeBuilder<EmergencySessionDTO> builder)
    {
        builder.ToTable("EmergencySessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(180);
        builder.Property(x => x.Instructions).HasMaxLength(3000);
        builder.Property(x => x.ActivatedByName).HasMaxLength(150);
        builder.Property(x => x.ResolvedByName).HasMaxLength(150);
        builder.Property(x => x.Resolution).HasMaxLength(2000);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Incident).WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.ActivatedAt });
        builder.HasIndex(x => x.LicenseId).HasDatabaseName("IX_EmergencySessions_LicenseId_Active")
            .HasFilter("\"Status\" = 0").IsUnique();
    }
}

public sealed class DigitalPassConfiguration : IEntityTypeConfiguration<DigitalPassDTO>
{
    public void Configure(EntityTypeBuilder<DigitalPassDTO> builder)
    {
        builder.ToTable("DigitalPasses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.Visit).WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.VisitId).IsUnique();
        builder.HasIndex(x => new { x.LicenseId, x.Status, x.ExpiresAt });
    }
}
