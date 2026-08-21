using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public class AmenityViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; }
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; }
    public int CancellationCutoffHours { get; set; }
    public List<AmenityScheduleSlotViewModel> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutViewModel> Blackouts { get; set; } = [];
}

public class AmenityScheduleSlotViewModel
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; }
}

public class AmenityBlackoutViewModel
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AmenityFormViewModel
{
    [Required] public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; } = true;
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; } = 60;
    public int CancellationCutoffHours { get; set; }
    public List<AmenityScheduleSlotFormViewModel> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutFormViewModel> Blackouts { get; set; } = [];
}

public class AmenityScheduleSlotFormViewModel
{
    public Guid? Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; } = new(8, 0, 0);
    public TimeSpan EndTime { get; set; } = new(14, 0, 0);
    public bool Active { get; set; } = true;
}

public class AmenityBlackoutFormViewModel
{
    public Guid? Id { get; set; }
    public DateTime StartDate { get; set; } = CondotifyTime.Today;
    public DateTime EndDate { get; set; } = CondotifyTime.Today;
    public string Reason { get; set; } = string.Empty;
}

public class AmenitySlotAvailabilityViewModel
{
    public Guid SlotId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Available { get; set; }
    public string? OccupiedByUnitNumber { get; set; }
    public string? OccupiedStatus { get; set; }
    public Guid? BookingId { get; set; }
}

public class AmenityBookingViewModel
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public string AmenityName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
    public string? ResidentName { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public TimeSpan SlotStartTime { get; set; }
    public TimeSpan SlotEndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? TermsAcceptedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
}

public class AmenityBookingFormViewModel
{
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public DateTime Date { get; set; } = CondotifyTime.Today;
    public Guid SlotId { get; set; }
    public bool TermsAccepted { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class AmenityUnitSearchViewModel
{
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public List<AmenityUnitSearchResidentViewModel> Residents { get; set; } = [];
}

public class AmenityUnitSearchResidentViewModel
{
    public Guid ResidentId { get; set; }
    public string Name { get; set; } = string.Empty;
}
