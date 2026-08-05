namespace CondotifyAPI.Data.Vehicles;

public sealed class VehicleCreateIn
{
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = "Carro";
    public string TagIdentifier { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
}

public sealed class VehicleUpdateIn
{
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = "Carro";
    public string TagIdentifier { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class VehicleOut
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TagIdentifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
