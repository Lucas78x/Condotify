namespace Condotify.Models;

public sealed class ResidentHomeSummaryViewModel
{
    public DateTime GeneratedAt { get; set; }
    public int DeliveriesAwaitingPickup { get; set; }
    public int VisitsToday { get; set; }
    public int UpcomingBookings { get; set; }
    public DateTime? NextBookingDate { get; set; }
    public string? NextBookingName { get; set; }
    public int OpenCharges { get; set; }
    public DateTime? NextChargeDueDate { get; set; }
    public long EnabledModules { get; set; }
}
