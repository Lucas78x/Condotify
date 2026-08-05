using CondotifyAPI.Domain.Enums.Equipments;

namespace CondotifyAPI.Data.Equipments;

public sealed class LprConfigurationIn
{
    public Guid? LprCameraId { get; set; }
    public int? LprCameraChannel { get; set; }
    public int? LprDoorChannel { get; set; }
    public LprModeEnum? LprMode { get; set; }
}

public sealed class LprConfigurationOut
{
    public Guid? LprCameraId { get; set; }
    public int? LprCameraChannel { get; set; }
    public int? LprDoorChannel { get; set; }
    public LprModeEnum? LprMode { get; set; }
}
