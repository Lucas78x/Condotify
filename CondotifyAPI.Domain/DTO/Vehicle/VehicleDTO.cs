using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;

namespace CondotifyAPI.Domain.DTO.Vehicle;

public class VehicleDTO
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid? ResidentId { get; set; }
    public ResidentAccessDTO? Resident { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = "Carro";
    public string TagIdentifier { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
