namespace CondotifyAPI.Data.Administration;

public sealed class LicenseAdministrationOut
{
    public CurrentLicenseAccessOut CurrentAccess { get; set; } = new();
    public List<LicenseUserAccessOut> Users { get; set; } = [];
    public CredentialPolicyOut Policy { get; set; } = new();
}

public sealed class CurrentLicenseAccessOut
{
    public string Role { get; set; } = string.Empty;
    public long Permissions { get; set; }
    public bool IsEnterpriseAdministrator { get; set; }
}

public sealed class LicenseUserAccessOut
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public long Permissions { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastAccess { get; set; }
}

public sealed class CreateLicenseUserIn
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public LicenseAccessRoleEnum Role { get; set; } = LicenseAccessRoleEnum.Operator;
    public long? Permissions { get; set; }
}

public sealed class UpdateLicenseUserIn
{
    public LicenseAccessRoleEnum Role { get; set; }
    public long Permissions { get; set; }
    public bool IsActive { get; set; }
}

public class CredentialPolicyOut
{
    public int QrCodeValidityMinutes { get; set; }
    public bool AllowQrCodeRenewal { get; set; }
    public int MaxQrCodeRenewals { get; set; }
    public int QrCodeRenewalMinutes { get; set; }
    public int TemporaryFaceValidityMinutes { get; set; }
    public int MaxTemporaryFaceValidityMinutes { get; set; }
    public bool RequireFacePhoto { get; set; }
    public bool AutoDeactivateExpiredCredentials { get; set; }
    public bool RemoveExpiredCredentialsFromDevices { get; set; }
    public bool AllowResidentDigitalPass { get; set; }
}

public sealed class UpdateCredentialPolicyIn : CredentialPolicyOut;
