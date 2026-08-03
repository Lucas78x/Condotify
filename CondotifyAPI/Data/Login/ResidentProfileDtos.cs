using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Data.Login;

public sealed class ResidentMeOut
{
    public Guid ResidentId { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public ResidentAccessTypeEnum AccessType { get; set; }
    public bool IsResponsible { get; set; }
    public IReadOnlyCollection<ResidentUnitOut> Units { get; set; } = [];
}

public sealed class ResidentUnitOut
{
    public Guid UnitId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public Guid BlockId { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public ResidentUnitRelationshipEnum Relationship { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public sealed class CreateResidentVisitIn
{
    public Guid UnitId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public int? MaxUses { get; set; } = 1;
    public string IdempotencyKey { get; set; } = string.Empty;
}
