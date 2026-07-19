using CondotifyAPI.Domain.Enums.Amenities;

namespace CondotifyAPI.Data.Amenities;

public sealed class SaveAmenityIn
{
    public string Name { get; set; } = string.Empty;
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
    public List<SaveAmenityScheduleSlotIn> ScheduleSlots { get; set; } = [];
    public List<SaveAmenityBlackoutIn> Blackouts { get; set; } = [];
}

public sealed class SaveAmenityScheduleSlotIn
{
    public Guid? Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class SaveAmenityBlackoutIn
{
    public Guid? Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenityOut
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
    public List<AmenityScheduleSlotOut> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutOut> Blackouts { get; set; } = [];
}

public sealed class AmenityScheduleSlotOut
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; }
}

public sealed class AmenityBlackoutOut
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenitySlotAvailabilityOut
{
    public Guid SlotId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Available { get; set; }
    public string? OccupiedByUnitNumber { get; set; }
    public AmenityBookingStatusEnum? OccupiedStatus { get; set; }
    public Guid? BookingId { get; set; }
}

public sealed class CreateAmenityBookingIn
{
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public bool TermsAccepted { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class RejectAmenityBookingIn
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class CancelAmenityBookingIn
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenityBookingOut
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

public sealed class AmenityUnitSearchOut
{
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public List<AmenityUnitSearchResidentOut> Residents { get; set; } = [];
}

public sealed class AmenityUnitSearchResidentOut
{
    public Guid ResidentId { get; set; }
    public string Name { get; set; } = string.Empty;
}
