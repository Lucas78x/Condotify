using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Governance;
using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.DTO.Governance;

public sealed class CondominiumAssemblyDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AssemblyTypeEnum Type { get; set; }
    public AssemblyFormatEnum Format { get; set; }
    public AssemblyStatusEnum Status { get; set; } = AssemblyStatusEnum.Draft;
    public AssemblyVoteVisibilityEnum VoteVisibility { get; set; } = AssemblyVoteVisibilityEnum.Secret;
    public string Location { get; set; } = string.Empty;
    public string MeetingUrl { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime VotingStartsAt { get; set; }
    public DateTime VotingEndsAt { get; set; }
    public bool AllowVoteChange { get; set; } = true;
    public bool ShowResultsBeforeClose { get; set; }
    public bool RequireResponsibleResident { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AssemblyAgendaItemDTO> AgendaItems { get; set; } = new List<AssemblyAgendaItemDTO>();
    public ICollection<AssemblyEligibleUnitDTO> EligibleUnits { get; set; } = new List<AssemblyEligibleUnitDTO>();
    public ICollection<AssemblyAttendanceDTO> Attendances { get; set; } = new List<AssemblyAttendanceDTO>();
    public ICollection<AssemblyVoteDTO> Votes { get; set; } = new List<AssemblyVoteDTO>();
    public ICollection<AssemblyAuditDTO> Audits { get; set; } = new List<AssemblyAuditDTO>();
}

public sealed class AssemblyAgendaItemDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AssemblyId { get; set; }
    public CondominiumAssemblyDTO Assembly { get; set; } = null!;
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal QuorumPercentage { get; set; }
    public decimal ApprovalPercentage { get; set; } = 50m;
    public bool AbstentionCountsForQuorum { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AssemblyVoteOptionDTO> Options { get; set; } = new List<AssemblyVoteOptionDTO>();
    public ICollection<AssemblyVoteDTO> Votes { get; set; } = new List<AssemblyVoteDTO>();
}

public sealed class AssemblyVoteOptionDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AgendaItemId { get; set; }
    public AssemblyAgendaItemDTO AgendaItem { get; set; } = null!;
    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsApproval { get; set; }
    public bool IsAbstention { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Snapshot das unidades que podem votar. Criado na publicação para que o denominador
/// do quórum não mude silenciosamente durante a assembleia.
/// </summary>
public sealed class AssemblyEligibleUnitDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AssemblyId { get; set; }
    public CondominiumAssemblyDTO Assembly { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public decimal Weight { get; set; } = 1m;
    public bool IsEligible { get; set; } = true;
    public string IneligibilityReason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class AssemblyAttendanceDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AssemblyId { get; set; }
    public CondominiumAssemblyDTO Assembly { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public DateTime JoinedAt { get; set; }
}

public sealed class AssemblyVoteDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AssemblyId { get; set; }
    public CondominiumAssemblyDTO Assembly { get; set; } = null!;
    public Guid AgendaItemId { get; set; }
    public AssemblyAgendaItemDTO AgendaItem { get; set; } = null!;
    public Guid OptionId { get; set; }
    public AssemblyVoteOptionDTO Option { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid ResidentId { get; set; }
    public ResidentAccessDTO Resident { get; set; } = null!;
    public decimal Weight { get; set; } = 1m;
    public int Revision { get; set; } = 1;
    public DateTime CastAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AssemblyAuditDTO : ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public Guid AssemblyId { get; set; }
    public CondominiumAssemblyDTO Assembly { get; set; } = null!;
    public string EventType { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
