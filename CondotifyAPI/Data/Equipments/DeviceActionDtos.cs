using System.ComponentModel.DataAnnotations;

namespace CondotifyAPI.Data.Equipments;

public sealed class OpenDoorIn
{
    [Range(1, 4)]
    public int Channel { get; set; } = 1;

    [MaxLength(200)]
    public string Reason { get; set; } = string.Empty;

    public Guid? EventId { get; set; }
}

public sealed class DeviceActionOut
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
