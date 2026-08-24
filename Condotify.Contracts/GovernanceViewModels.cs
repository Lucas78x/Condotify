using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public sealed class AssemblyFormViewModel
{
    [Required(ErrorMessage = "Informe o título.")]
    [StringLength(180, ErrorMessage = "Use no máximo 180 caracteres.")]
    public string Title { get; set; } = string.Empty;
    [StringLength(8000)] public string Description { get; set; } = string.Empty;
    public int Type { get; set; } = 1;
    public int Format { get; set; } = 2;
    public int VoteVisibility { get; set; } = 1;
    [StringLength(300)] public string Location { get; set; } = string.Empty;
    [StringLength(1000)] public string MeetingUrl { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime VotingStartsAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime VotingEndsAt { get; set; } = DateTime.UtcNow.AddDays(8);
    public bool AllowVoteChange { get; set; } = true;
    public bool ShowResultsBeforeClose { get; set; }
    public bool RequireResponsibleResident { get; set; } = true;
    public List<AssemblyAgendaItemFormViewModel> AgendaItems { get; set; } = [];
}

public sealed class AssemblyAgendaItemFormViewModel
{
    [Required, StringLength(240)] public string Title { get; set; } = string.Empty;
    [StringLength(6000)] public string Description { get; set; } = string.Empty;
    [Range(0, 100)] public decimal QuorumPercentage { get; set; } = 50m;
    [Range(0, 100)] public decimal ApprovalPercentage { get; set; } = 50m;
    public bool AbstentionCountsForQuorum { get; set; } = true;
    public List<AssemblyVoteOptionFormViewModel> Options { get; set; } =
    [
        new() { Label = "Sim", IsApproval = true },
        new() { Label = "Não" },
        new() { Label = "Abstenção", IsAbstention = true }
    ];
}

public sealed class AssemblyVoteOptionFormViewModel
{
    [Required, StringLength(180)] public string Label { get; set; } = string.Empty;
    public bool IsApproval { get; set; }
    public bool IsAbstention { get; set; }
}

public class AssemblySummaryViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartsAt { get; set; }
    public DateTime VotingStartsAt { get; set; }
    public DateTime VotingEndsAt { get; set; }
    public int AgendaItemCount { get; set; }
    public int EligibleUnitCount { get; set; }
    public int AttendanceCount { get; set; }
    public int VoteCount { get; set; }
    public bool HasResidentVoted { get; set; }
}

public sealed class AssemblyDetailViewModel : AssemblySummaryViewModel
{
    public string Location { get; set; } = string.Empty;
    public string MeetingUrl { get; set; } = string.Empty;
    public string VoteVisibility { get; set; } = string.Empty;
    public bool AllowVoteChange { get; set; }
    public bool ShowResultsBeforeClose { get; set; }
    public bool RequireResponsibleResident { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? PublishedAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public bool ResultsVisible { get; set; }
    public decimal EligibleWeight { get; set; }
    public List<AssemblyAgendaItemViewModel> AgendaItems { get; set; } = [];
    public List<AssemblyResidentUnitViewModel> AvailableUnits { get; set; } = [];
    public List<AssemblyAttendanceViewModel> Attendances { get; set; } = [];
}

public sealed class AssemblyAgendaItemViewModel
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal QuorumPercentage { get; set; }
    public decimal ApprovalPercentage { get; set; }
    public bool AbstentionCountsForQuorum { get; set; }
    public decimal ParticipationWeight { get; set; }
    public decimal ParticipationPercentage { get; set; }
    public bool QuorumMet { get; set; }
    public bool ApprovalMet { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public Guid? SelectedUnitId { get; set; }
    public int VoteRevision { get; set; }
    public List<AssemblyResidentVoteViewModel> ResidentVotes { get; set; } = [];
    public List<AssemblyNamedVoteViewModel> NamedVotes { get; set; } = [];
    public List<AssemblyVoteOptionViewModel> Options { get; set; } = [];
}

public sealed class AssemblyNamedVoteViewModel
{
    public string UnitLabel { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public string OptionLabel { get; set; } = string.Empty;
    public DateTime CastAt { get; set; }
    public int Revision { get; set; }
}

public sealed class AssemblyResidentVoteViewModel
{
    public Guid UnitId { get; set; }
    public Guid OptionId { get; set; }
    public int Revision { get; set; }
}

public sealed class AssemblyVoteOptionViewModel
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsApproval { get; set; }
    public bool IsAbstention { get; set; }
    public int VoteCount { get; set; }
    public decimal VoteWeight { get; set; }
    public decimal Percentage { get; set; }
}

public sealed class AssemblyResidentUnitViewModel
{
    public Guid UnitId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Weight { get; set; }
    public bool CanVote { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AssemblyAttendanceViewModel
{
    public Guid UnitId { get; set; }
    public string UnitLabel { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }
}

public sealed class CastAssemblyVoteViewModel
{
    public Guid UnitId { get; set; }
    public Guid OptionId { get; set; }
}

public sealed class AssemblyAuditViewModel
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string ActorType { get; set; } = string.Empty;
    public string ActorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
