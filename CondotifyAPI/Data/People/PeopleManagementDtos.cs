using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;

namespace CondotifyAPI.Data.People;

public class UnitDetailOut
{
    public Guid Id { get; set; }
    public Guid BlockId { get; set; }
    public string BlockName { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Floor { get; set; } = string.Empty;
    public List<PersonSummaryOut> People { get; set; } = new();
    public List<VehicleOut> Vehicles { get; set; } = new();
}

public class PersonSummaryOut
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int CredentialCount { get; set; }
    public int VehicleCount { get; set; }
    public bool HasFaceCredential { get; set; }
    public bool HasActiveFaceCredential { get; set; }
}

public class PersonProfileOut : PersonSummaryOut
{
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string CPF { get; set; } = string.Empty;
    public string RG { get; set; } = string.Empty;
    public string BirthDate { get; set; } = string.Empty;
    public string CommercialPhone { get; set; } = string.Empty;
    public bool NotifyAccess { get; set; }
    public List<PersonCredentialOut> Credentials { get; set; } = new();
    public List<VehicleOut> Vehicles { get; set; } = new();
    public List<RegistrationInviteOut> Invites { get; set; } = new();
}

public class PersonCredentialOut
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsTemporary { get; set; }
    public int RenewalCount { get; set; }
    public int MaxRenewals { get; set; }
    public int DeviceCount { get; set; }
    public int SyncedDeviceCount { get; set; }
    public int UseCount { get; set; }
    public int? MaxUses { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public List<PersonCredentialDeviceOut> Devices { get; set; } = [];
}

public class PersonCredentialDeviceOut
{
    public Guid DeviceId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RouteNames { get; set; } = string.Empty;
    public string PortalNumbers { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime LastSyncAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}

public class VehicleOut
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TagIdentifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class UpdatePersonProfileIn
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? CommercialPhone { get; set; }
    public string? CPF { get; set; }
    public string? RG { get; set; }
    public string? BirthDate { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public bool NotifyAccess { get; set; }
    public bool IsActive { get; set; } = true;
    public ResidentUnitRelationshipEnum Relationship { get; set; }
}

public class CreateVehicleIn
{
    public Guid UnitId { get; set; }
    public string? Plate { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public string? Type { get; set; }
    public string? TagIdentifier { get; set; }
}

public class UpdateVehicleIn : CreateVehicleIn
{
    public bool IsActive { get; set; } = true;
}

public class CreateRegistrationInviteIn
{
    public string? Contact { get; set; }
    public RegistrationInviteChannelEnum Channel { get; set; } = RegistrationInviteChannelEnum.Link;
    public int ValidDays { get; set; } = 7;
}

public class RegistrationInviteOut
{
    public Guid Id { get; set; }
    public Guid ResidentId { get; set; }
    public string ResidentName { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public int SendCount { get; set; }
    public DateTime SentAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? InviteUrl { get; set; }
}

public class PublicRegistrationInviteOut
{
    public string ResidentName { get; set; } = string.Empty;
    public string LicenseName { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public string UnitNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool ExistingAccount { get; set; }
}

public class CompleteRegistrationInviteIn
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public string? CPF { get; set; }
    public string? RG { get; set; }
    public string? BirthDate { get; set; }

    /// <summary>
    /// Senha em texto claro do morador. Obrigatoria: um convite completado sem senha
    /// deixaria o morador sem meio de acesso. Nunca gravada, logada ou devolvida como
    /// veio — ver <c>ResidentPasswordSetter</c>.
    /// </summary>
    public string? Password { get; set; }
    public bool ConfirmExistingAccount { get; set; }
}
