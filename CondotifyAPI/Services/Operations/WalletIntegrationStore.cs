using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public sealed record GoogleWalletSettings(
    Guid EnterpriseId,
    string IssuerId,
    string ServiceAccountEmail,
    string ClassSuffix,
    WalletAuthenticationModeEnum AuthenticationMode,
    string PrivateKey,
    bool IsPersisted);

public sealed record AppleWalletSettings(
    Guid EnterpriseId,
    string PassTypeIdentifier,
    string TeamIdentifier,
    string CertificateBase64,
    string CertificatePassword,
    string WwdrCertificate,
    bool IsPersisted);

public interface IWalletIntegrationStore
{
    Task<GoogleWalletSettings?> GetGoogleAsync(Guid enterpriseId, CancellationToken cancellationToken = default);
    Task<AppleWalletSettings?> GetAppleAsync(Guid enterpriseId, CancellationToken cancellationToken = default);
}

public sealed class WalletIntegrationStore(
    DatabaseContext context,
    IWalletSecretProtector protector,
    IConfiguration configuration) : IWalletIntegrationStore
{
    public async Task<GoogleWalletSettings?> GetGoogleAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
    {
        var integration = await context.WalletIntegrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnterpriseId == enterpriseId && x.Provider == WalletProviderEnum.Google, cancellationToken);
        if (integration is not null)
        {
            if (!integration.IsActive || !integration.IsValidated) return null;
            return new GoogleWalletSettings(
                enterpriseId,
                integration.IssuerId,
                integration.ServiceAccountEmail,
                integration.ClassSuffix,
                integration.AuthenticationMode,
                integration.AuthenticationMode == WalletAuthenticationModeEnum.PrivateKey
                    ? protector.Unprotect(integration.CredentialSecret, enterpriseId, "google-private-key")
                    : string.Empty,
                true);
        }

        var issuerId = FirstNonBlank(configuration["DigitalPass:GoogleWallet:IssuerId"], Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_ISSUER_ID"));
        var account = FirstNonBlank(configuration["DigitalPass:GoogleWallet:ServiceAccountEmail"], Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_SERVICE_ACCOUNT_EMAIL"));
        var privateKey = FirstNonBlank(configuration["DigitalPass:GoogleWallet:PrivateKey"], Environment.GetEnvironmentVariable("CONDOTIFY_GOOGLE_WALLET_PRIVATE_KEY"));
        var suffix = FirstNonBlank(configuration["DigitalPass:GoogleWallet:ClassSuffix"], "condotify_access");
        return string.IsNullOrWhiteSpace(issuerId) || string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(privateKey)
            ? null
            : new GoogleWalletSettings(enterpriseId, issuerId, account, suffix!, WalletAuthenticationModeEnum.PrivateKey, privateKey, false);
    }

    public async Task<AppleWalletSettings?> GetAppleAsync(Guid enterpriseId, CancellationToken cancellationToken = default)
    {
        var integration = await context.WalletIntegrations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EnterpriseId == enterpriseId && x.Provider == WalletProviderEnum.Apple, cancellationToken);
        if (integration is not null)
        {
            if (!integration.IsActive || !integration.IsValidated) return null;
            return new AppleWalletSettings(
                enterpriseId,
                integration.PassTypeIdentifier,
                integration.TeamIdentifier,
                protector.Unprotect(integration.CredentialSecret, enterpriseId, "apple-pfx"),
                protector.Unprotect(integration.CredentialPassword, enterpriseId, "apple-pfx-password"),
                integration.IntermediateCertificate,
                true);
        }

        var passType = Setting("PassTypeIdentifier", "CONDOTIFY_APPLE_PASS_TYPE_IDENTIFIER");
        var team = Setting("TeamIdentifier", "CONDOTIFY_APPLE_TEAM_IDENTIFIER");
        var certificate = Setting("SigningCertificatePfxBase64", "CONDOTIFY_APPLE_PASS_CERTIFICATE_PFX_BASE64");
        var password = Setting("SigningCertificatePassword", "CONDOTIFY_APPLE_PASS_CERTIFICATE_PASSWORD");
        var wwdr = Setting("WwdrCertificateBase64", "CONDOTIFY_APPLE_WWDR_CERTIFICATE_BASE64");
        return string.IsNullOrWhiteSpace(passType) || string.IsNullOrWhiteSpace(team) || string.IsNullOrWhiteSpace(certificate) || string.IsNullOrWhiteSpace(wwdr)
            ? null
            : new AppleWalletSettings(enterpriseId, passType, team, certificate, password, wwdr, false);
    }

    private string Setting(string key, string environmentName) =>
        FirstNonBlank(configuration[$"DigitalPass:AppleWallet:{key}"], Environment.GetEnvironmentVariable(environmentName)) ?? string.Empty;
    private static string? FirstNonBlank(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
