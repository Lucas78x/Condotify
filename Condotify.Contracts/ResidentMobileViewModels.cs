namespace Condotify.Models;

public sealed class ResidentProfileViewModel
{
    public Guid ResidentId { get; set; }
    public Guid LicenseId { get; set; }
    public string LicenseName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string AccessType { get; set; } = string.Empty;
    public bool IsResponsible { get; set; }
    public List<ResidentUnitViewModel> Units { get; set; } = [];
}

public sealed class ResidentUnitViewModel
{
    public Guid UnitId { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public Guid BlockId { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
}

public sealed class CftvStreamSessionViewModel
{
    public Guid SessionId { get; set; }
    public string PlaybackUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public string Protocol { get; set; } = string.Empty;
}

public sealed class CftvStatusViewModel
{
    public Guid DeviceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Online { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string HealthMessage { get; set; } = string.Empty;
    public int MaxChannels { get; set; }
}

public sealed class ResidentCameraViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public int MaxChannels { get; set; }
    public bool Online { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public string HealthMessage { get; set; } = string.Empty;
}

public sealed class ResidentVisitFormViewModel
{
    public Guid UnitId { get; set; }
    public string VisitorName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string VehiclePlate { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; } = DateTime.Now;
    public DateTime ValidTo { get; set; } = DateTime.Now.AddHours(4);
    public int? MaxUses { get; set; } = 1;
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
}
