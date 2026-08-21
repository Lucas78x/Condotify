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
    public bool AllowResidentDigitalPass { get; set; } = true;
    public long EnabledModules { get; set; }
    public string GroupLabelSingular { get; set; } = "Bloco";
    public string GroupLabelPlural { get; set; } = "Blocos";
    public string UnitLabelSingular { get; set; } = "Unidade";
    public string UnitLabelPlural { get; set; } = "Unidades";
    public IReadOnlyCollection<ResidentUnitOut> Units { get; set; } = [];
}

public sealed class ResidentDigitalPassStatusOut
{
    public bool HasActivePass { get; set; }
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
    public AccessCredentialTypeEnum CredentialType { get; set; } = AccessCredentialTypeEnum.QrCode;
    public bool CreateFacialInvite { get; set; }
    public List<Guid> RouteIds { get; set; } = [];
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class ResidentVisitOptionsOut
{
    public bool FacialInviteAvailable { get; set; }
    public List<ResidentVisitRouteOut> Routes { get; set; } = [];
}

public sealed class ResidentVisitRouteOut
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DaysOfWeekMask { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public int DeviceCount { get; set; }
    public int OnlineDeviceCount { get; set; }
    public bool SupportsFace { get; set; }
}
