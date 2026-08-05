using CondotifyAPI.Domain.DTO.Equipments;

namespace CondotifyAPI.Domain.DTO.Vehicle;

public enum VehicleAccessAuditAction
{
    NoRead = 0,
    Opened = 1,
    DetectedOnly = 2,
    AlertRaised = 3
}

public class VehicleAccessAuditDTO
{
    public Guid Id { get; set; }
    public Guid AccessControlDeviceId { get; set; }
    public AccessControlDeviceDTO Device { get; set; } = null!;
    public string? PlateRead { get; set; }
    public double Confidence { get; set; }
    public Guid? MatchedVehicleId { get; set; }
    public VehicleAccessAuditAction Action { get; set; }
    public string? SnapshotReference { get; set; }
    public DateTime Timestamp { get; set; }
}
