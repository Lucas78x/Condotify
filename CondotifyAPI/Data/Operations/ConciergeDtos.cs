using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Data.Operations;

public sealed class ConciergeDashboardOut
{
    public List<ConciergeVisitOut> Visits { get; set; } = [];
    public List<ConciergeEventOut> Events { get; set; } = [];
    public List<ConciergeDeviceOut> Devices { get; set; } = [];
    public int ExpectedToday { get; set; }
    public int InsideNow { get; set; }
    public int OfflineDevices { get; set; }
    public int DeniedToday { get; set; }
    public int PendingApprovals { get; set; }
    public int Overstays { get; set; }
    public List<AccessWatchlistEntryOut> Watchlist { get; set; } = [];
}

public sealed class CreateConciergeVisitIn
{
    public Guid HostResidentId { get; set; }
    public ResidentAccessTypeEnum AccessType { get; set; } = ResidentAccessTypeEnum.Guest;
    public string VisitorName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string ImageBase64 { get; set; } = string.Empty;
    public AccessCredentialTypeEnum CredentialType { get; set; } = AccessCredentialTypeEnum.QrCode;
    public bool CreateFacialInvite { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int? MaxUses { get; set; } = 1;
    public List<Guid> RouteIds { get; set; } = [];
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool RequireApproval { get; set; }
    public int RepeatCount { get; set; } = 1;
    public int RepeatEveryDays { get; set; } = 7;
}

public sealed class UpdateConciergeVisitStatusIn
{
    public AccessVisitStatusEnum Status { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ScanConciergeVisitIn
{
    public string Code { get; set; } = string.Empty;
}

public sealed class ConciergeVisitOut
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid HostResidentId { get; set; }
    public string HostName { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string VisitorName { get; set; } = string.Empty;
    public string AccessType { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CredentialType { get; set; } = string.Empty;
    public string CredentialCode { get; set; } = string.Empty;
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public bool ApprovalRequired { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
    public string ApprovalNotes { get; set; } = string.Empty;
    public DateTime? ExpectedCheckoutAt { get; set; }
    public bool IsOverstayed { get; set; }
    public int RecurrenceSequence { get; set; }
    public int RecurrenceCount { get; set; }
    public string FacialInviteStatus { get; set; } = string.Empty;
    public string FacialInviteUrl { get; set; } = string.Empty;
}

public sealed class PublicVisitFacialInviteOut
{
    public string VisitorName { get; set; } = string.Empty;
    public string LicenseName { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int? MaxUses { get; set; }
    public List<PublicVisitRouteOut> Routes { get; set; } = [];
}

public sealed class PublicVisitRouteOut
{
    public string Name { get; set; } = string.Empty;
    public int DaysOfWeekMask { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public sealed class CompleteVisitFacialInviteIn
{
    public string ImageBase64 { get; set; } = string.Empty;
    public bool Consent { get; set; }
}

public sealed class VisitFacialInviteIssuedOut
{
    public Guid VisitId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string InviteUrl { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}

public sealed class DecideVisitApprovalIn { public bool Approved { get; set; } public string Notes { get; set; } = string.Empty; }
public sealed class CreateWatchlistEntryIn { public string Name { get; set; } = string.Empty; public string Document { get; set; } = string.Empty; public string VehiclePlate { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; public int Severity { get; set; } = 2; public DateTime? ExpiresAt { get; set; } }
public sealed class AccessWatchlistEntryOut { public Guid Id { get; set; } public string Name { get; set; } = string.Empty; public string Document { get; set; } = string.Empty; public string VehiclePlate { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; public int Severity { get; set; } public DateTime? ExpiresAt { get; set; } }

public sealed class ConciergeEventOut
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid? CredentialId { get; set; }
    public Guid? ResidentId { get; set; }
    public Guid? UnitId { get; set; }
    public Guid? VisitId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string PersonName { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string CredentialType { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public bool? CredentialActive { get; set; }
    public DateTime? CredentialValidFrom { get; set; }
    public DateTime? CredentialValidTo { get; set; }
    public string Details { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public string HostPhoneNumber { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public bool Authorized { get; set; }
    public string Portal { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public bool RequiresAttention { get; set; }
    public DateTime? AttentionResolvedAt { get; set; }
    public string AttentionResolvedBy { get; set; } = string.Empty;
    public string AttentionResolutionNote { get; set; } = string.Empty;
}

public sealed class ResolveConciergeEventIn
{
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string Note { get; set; } = string.Empty;
}

public sealed class ConciergeDeviceOut
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string HealthMessage { get; set; } = string.Empty;
    public string DiscoveredPortalsJson { get; set; } = "[]";
}
