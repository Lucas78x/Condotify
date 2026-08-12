using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Invitation;

namespace CondotifyAPI.Domain.DTO.Invitation;

public class AccessVisitDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid HostResidentId { get; set; }
    public ResidentAccessDTO HostResident { get; set; } = null!;
    public Guid GuestResidentId { get; set; }
    public ResidentAccessDTO GuestResident { get; set; } = null!;
    public Guid CredentialId { get; set; }
    public ResidentAccessCredentialDTO Credential { get; set; } = null!;
    public string VisitorName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public AccessVisitStatusEnum Status { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public DateTime? CheckedInAt { get; set; }
    public DateTime? CheckedOutAt { get; set; }
    public bool ApprovalRequired { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public string ApprovalNotes { get; set; } = string.Empty;
    public DateTime? ExpectedCheckoutAt { get; set; }
    public DateTime? OverstayAcknowledgedAt { get; set; }
    public Guid? RecurrenceGroupId { get; set; }
    public int RecurrenceSequence { get; set; } = 1;
    public int RecurrenceCount { get; set; } = 1;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public VisitFacialInviteDTO? FacialInvite { get; set; }
}

public class VisitFacialInviteDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid VisitId { get; set; }
    public AccessVisitDTO Visit { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public VisitFacialInviteStatusEnum Status { get; set; }
    public int UploadAttempts { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
