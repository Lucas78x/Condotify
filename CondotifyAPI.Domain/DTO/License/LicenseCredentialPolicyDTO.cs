namespace CondotifyAPI.Domain.DTO.License;

public class LicenseCredentialPolicyDTO
{
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public int QrCodeValidityMinutes { get; set; } = 1440;
    public bool AllowQrCodeRenewal { get; set; } = true;
    public int MaxQrCodeRenewals { get; set; } = 2;
    public int QrCodeRenewalMinutes { get; set; } = 1440;
    public int TemporaryFaceValidityMinutes { get; set; } = 1440;
    public int MaxTemporaryFaceValidityMinutes { get; set; } = 10080;
    public bool RequireFacePhoto { get; set; } = true;
    public bool AutoDeactivateExpiredCredentials { get; set; } = true;
    public bool RemoveExpiredCredentialsFromDevices { get; set; } = true;
    public bool AllowResidentDigitalPass { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
