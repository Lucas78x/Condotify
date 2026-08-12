using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public sealed class IncidentPageViewModel
{
    public int Total { get; set; }
    public int Open { get; set; }
    public int Critical { get; set; }
    public List<IncidentViewModel> Items { get; set; } = [];
}

public sealed class IncidentViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string RelatedResourceType { get; set; } = string.Empty;
    public Guid? RelatedResourceId { get; set; }
    public string AssignedToName { get; set; } = string.Empty;
    public string ReportedByName { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
    public DateTime? DueAt { get; set; }
    public DateTime? SlaResponseDueAt { get; set; }
    public DateTime? SlaResolutionDueAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool CanManage { get; set; }
    public List<IncidentTimelineEntryViewModel> Timeline { get; set; } = [];
    public List<IncidentAttachmentViewModel> Attachments { get; set; } = [];
    public List<WorkOrderViewModel> WorkOrders { get; set; } = [];
}

public sealed class IncidentTimelineEntryViewModel
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string ReferenceUrl { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class IncidentCreateViewModel
{
    [Required, MaxLength(180)] public string Title { get; set; } = string.Empty;
    [MaxLength(4000)] public string Description { get; set; } = string.Empty;
    public int Category { get; set; } = 6;
    public int Severity { get; set; } = 1;
    public DateTime? DueAt { get; set; }
    public string RelatedResourceType { get; set; } = string.Empty;
    public Guid? RelatedResourceId { get; set; }
    [MaxLength(240)] public string LocationLabel { get; set; } = string.Empty;
}

public sealed class IncidentCommentViewModel
{
    [Required, MaxLength(2000)] public string Message { get; set; } = string.Empty;
    public string ReferenceType { get; set; } = string.Empty;
    public Guid? ReferenceId { get; set; }
    public string ReferenceUrl { get; set; } = string.Empty;
    public bool VisibleToResident { get; set; }
}

public sealed class IncidentStatusUpdateViewModel
{
    public int Status { get; set; }
    [MaxLength(2000)] public string Note { get; set; } = string.Empty;
}

public sealed class IncidentAssignmentViewModel
{
    public Guid? UserId { get; set; }
    [MaxLength(150)] public string Name { get; set; } = string.Empty;
}

public sealed class AutomationRuleViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public int Threshold { get; set; }
    public int WindowMinutes { get; set; }
    public string Severity { get; set; } = string.Empty;
    public int Actions { get; set; }
    public bool IsEnabled { get; set; }
    public int CooldownMinutes { get; set; }
    public DateTime? LastEvaluatedAt { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int ExecutionCount { get; set; }
    public List<AutomationExecutionViewModel> RecentExecutions { get; set; } = [];
}

public sealed class AutomationExecutionViewModel
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public Guid? IncidentId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AutomationRuleFormViewModel
{
    [Required, MaxLength(160)] public string Name { get; set; } = string.Empty;
    [MaxLength(1000)] public string Description { get; set; } = string.Empty;
    public int TriggerType { get; set; }
    [Range(1, 10000)] public int Threshold { get; set; } = 5;
    [Range(1, 10080)] public int WindowMinutes { get; set; } = 15;
    public int Severity { get; set; } = 2;
    public int Actions { get; set; } = 3;
    public bool IsEnabled { get; set; } = true;
    [Range(5, 43200)] public int CooldownMinutes { get; set; } = 60;
}

public sealed class EmergencySessionViewModel
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string ActivatedByName { get; set; } = string.Empty;
    public DateTime ActivatedAt { get; set; }
    public string ResolvedByName { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public string Resolution { get; set; } = string.Empty;
    public Guid IncidentId { get; set; }
}

public sealed class EmergencyActivationViewModel
{
    public int Type { get; set; }
    [Required, MaxLength(180)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(3000)] public string Instructions { get; set; } = string.Empty;
    [Required] public string ConfirmationText { get; set; } = string.Empty;
}

public sealed class EmergencyResolveViewModel
{
    [Required, MaxLength(2000)] public string Resolution { get; set; } = string.Empty;
    [Required] public string ConfirmationText { get; set; } = string.Empty;
}

public sealed class DigitalPassViewModel
{
    public Guid Id { get; set; }
    public Guid VisitId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string LicenseName { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string CredentialCode { get; set; } = string.Empty;
    public string PublicUrl { get; set; } = string.Empty;
    public string GoogleWalletUrl { get; set; } = string.Empty;
    public string AppleWalletUrl { get; set; } = string.Empty;
    public bool GoogleWalletConfigured { get; set; }
    public bool AppleWalletConfigured { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime IssuedAt { get; set; }
}

public sealed class ResidentDigitalPassStatusViewModel
{
    public bool HasActivePass { get; set; }
}
