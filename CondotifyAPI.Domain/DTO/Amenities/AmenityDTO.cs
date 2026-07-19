using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Amenities;

namespace CondotifyAPI.Domain.DTO.Amenities;

public class AmenityDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AmenityScheduleSlotDTO> ScheduleSlots { get; set; } = new List<AmenityScheduleSlotDTO>();
    public ICollection<AmenityBlackoutDTO> Blackouts { get; set; } = new List<AmenityBlackoutDTO>();
    public ICollection<AmenityBookingDTO> Bookings { get; set; } = new List<AmenityBookingDTO>();
}

public class AmenityScheduleSlotDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; } = true;
}

public class AmenityBlackoutDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AmenityBookingDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid? ResidentId { get; set; }
    public ResidentAccessDTO? Resident { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public AmenityScheduleSlotDTO Slot { get; set; } = null!;
    public AmenityBookingStatusEnum Status { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
}
