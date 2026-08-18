using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public sealed class WalletIntegrationsViewModel
{
    public bool EncryptionReady { get; set; }
    public WalletIntegrationStatusViewModel Google { get; set; } = new() { Provider = "Google" };
    public WalletIntegrationStatusViewModel Apple { get; set; } = new() { Provider = "Apple" };
}

public sealed class WalletIntegrationStatusViewModel
{
    public string Provider { get; set; } = string.Empty;
    public string Source { get; set; } = "Cofre F&F Access";
    public string AuthenticationMode { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
    public bool IsValidated { get; set; }
    public bool IsActive { get; set; }
    public int Version { get; set; }
    public string IssuerId { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string ClassSuffix { get; set; } = string.Empty;
    public string PassTypeIdentifier { get; set; } = string.Empty;
    public string TeamIdentifier { get; set; } = string.Empty;
    public bool HasCredential { get; set; }
    public string CredentialFingerprint { get; set; } = string.Empty;
    public DateTime? CredentialExpiresAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string LastValidationMessage { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string UpdatedByName { get; set; } = string.Empty;
}

public sealed class GoogleWalletConfigurationViewModel
{
    [Required, MaxLength(80)] public string IssuerId { get; set; } = string.Empty;
    [Required, EmailAddress, MaxLength(320)] public string ServiceAccountEmail { get; set; } = string.Empty;
    [Required, RegularExpression("^[A-Za-z0-9._-]+$")] public string ClassSuffix { get; set; } = "condotify_access";
    public string AuthenticationMode { get; set; } = "PrivateKey";
    public string PrivateKey { get; set; } = string.Empty;
}

public sealed class AppleWalletConfigurationViewModel
{
    [Required, MaxLength(180)] public string PassTypeIdentifier { get; set; } = string.Empty;
    [Required, MaxLength(80)] public string TeamIdentifier { get; set; } = string.Empty;
    public string CertificateBase64 { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
    public string WwdrCertificateBase64 { get; set; } = string.Empty;
    public string CertificateFileName { get; set; } = string.Empty;
}

public sealed class WalletIntegrationActivationViewModel
{
    public bool IsActive { get; set; }
}
