namespace CondotifyAPI.Domain.DTO.Operations;

public enum WalletProviderEnum
{
    Google = 1,
    Apple = 2
}

public enum WalletAuthenticationModeEnum
{
    PrivateKey = 1,
    ManagedIdentity = 2,
    Certificate = 3
}

/// <summary>
/// Configuracao de carteira digital pertencente a uma empresa. Os campos
/// CredentialSecret e CredentialPassword sempre recebem valores cifrados;
/// nenhum consumidor deve gravar segredo em texto puro nesta entidade.
/// </summary>
public sealed class WalletIntegrationDTO
{
    public Guid Id { get; set; }
    public Guid EnterpriseId { get; set; }
    public WalletProviderEnum Provider { get; set; }
    public WalletAuthenticationModeEnum AuthenticationMode { get; set; }
    public bool IsActive { get; set; }
    public bool IsValidated { get; set; }
    public int Version { get; set; } = 1;

    public string IssuerId { get; set; } = string.Empty;
    public string ServiceAccountEmail { get; set; } = string.Empty;
    public string ClassSuffix { get; set; } = string.Empty;
    public string PassTypeIdentifier { get; set; } = string.Empty;
    public string TeamIdentifier { get; set; } = string.Empty;

    public string CredentialSecret { get; set; } = string.Empty;
    public string CredentialPassword { get; set; } = string.Empty;
    public string IntermediateCertificate { get; set; } = string.Empty;

    public string CredentialFingerprint { get; set; } = string.Empty;
    public DateTime? CredentialExpiresAt { get; set; }
    public DateTime? LastValidatedAt { get; set; }
    public string LastValidationMessage { get; set; } = string.Empty;
    public Guid? UpdatedByUserId { get; set; }
    public string UpdatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
