using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Condotify.Models;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Models.Users;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Operations;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/wallet-integrations")]
public sealed class WalletIntegrationsController(
    DatabaseContext context,
    IWalletSecretProtector protector,
    IWalletIntegrationStore store,
    IGoogleWalletJwtSigner googleSigner,
    IHttpClientFactory httpClientFactory) : ControllerBase
{
    private const string AppleWwdrG4Url = "https://www.apple.com/certificateauthority/AppleWWDRCAG4.cer";

    [HttpGet]
    public async Task<IActionResult> Get(Guid licenseId, CancellationToken cancellationToken)
    {
        var scope = await RequireAdministratorAsync(licenseId, cancellationToken);
        if (scope is null) return Forbid();
        var rows = await context.WalletIntegrations.AsNoTracking()
            .Where(x => x.EnterpriseId == scope.Value.EnterpriseId)
            .ToListAsync(cancellationToken);
        var google = rows.FirstOrDefault(x => x.Provider == WalletProviderEnum.Google);
        var apple = rows.FirstOrDefault(x => x.Provider == WalletProviderEnum.Apple);
        return Ok(new WalletIntegrationsViewModel
        {
            EncryptionReady = protector.IsConfigured,
            Google = google is null
                ? await LegacyGoogleStatusAsync(scope.Value.EnterpriseId, cancellationToken)
                : ToStatus(google),
            Apple = apple is null
                ? await LegacyAppleStatusAsync(scope.Value.EnterpriseId, cancellationToken)
                : ToStatus(apple)
        });
    }

    [HttpPut("google")]
    public async Task<IActionResult> SaveGoogle(Guid licenseId, [FromBody] GoogleWalletConfigurationViewModel input, CancellationToken cancellationToken)
    {
        var scope = await RequireAdministratorAsync(licenseId, cancellationToken);
        if (scope is null) return Forbid();
        if (!protector.IsConfigured) return BadRequest(new { Errors = "O cofre de carteiras ainda nao possui uma chave mestra valida." });
        if (!Enum.TryParse<WalletAuthenticationModeEnum>(input.AuthenticationMode, true, out var mode) ||
            mode is not (WalletAuthenticationModeEnum.PrivateKey or WalletAuthenticationModeEnum.ManagedIdentity))
            return BadRequest(new { Errors = "Selecione um modo de autenticacao valido." });

        var row = await FindOrCreateAsync(scope.Value.EnterpriseId, WalletProviderEnum.Google, cancellationToken);
        var privateKey = string.Empty;
        if (mode == WalletAuthenticationModeEnum.PrivateKey)
        {
            privateKey = string.IsNullOrWhiteSpace(input.PrivateKey)
                ? ExistingSecret(row, "google-private-key")
                : input.PrivateKey.Trim().Replace("\\n", "\n", StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(privateKey))
                return BadRequest(new { Errors = "Informe a chave privada PEM da conta de servico." });
        }

        row.AuthenticationMode = mode;
        row.IssuerId = input.IssuerId.Trim();
        row.ServiceAccountEmail = input.ServiceAccountEmail.Trim().ToLowerInvariant();
        row.ClassSuffix = input.ClassSuffix.Trim();
        row.CredentialSecret = mode == WalletAuthenticationModeEnum.PrivateKey
            ? protector.Protect(privateKey, scope.Value.EnterpriseId, "google-private-key")
            : string.Empty;
        row.CredentialPassword = string.Empty;
        row.IntermediateCertificate = string.Empty;
        row.CredentialFingerprint = mode == WalletAuthenticationModeEnum.ManagedIdentity ? "Chave gerenciada pelo Google" : string.Empty;
        row.CredentialExpiresAt = null;
        row.IsActive = false;
        row.IsValidated = false;
        row.LastValidatedAt = null;
        row.LastValidationMessage = "Validacao pendente.";
        Touch(row, scope.Value.UserId, scope.Value.UserName);

        try
        {
            var settings = new GoogleWalletSettings(
                scope.Value.EnterpriseId, row.IssuerId, row.ServiceAccountEmail, row.ClassSuffix,
                row.AuthenticationMode, privateKey, true);
            await googleSigner.SignAsync(ValidationPayload(row.ServiceAccountEmail), settings, cancellationToken);
            if (mode == WalletAuthenticationModeEnum.PrivateKey)
                row.CredentialFingerprint = PrivateKeyFingerprint(privateKey);
            row.IsValidated = true;
            row.LastValidatedAt = DateTime.UtcNow;
            row.LastValidationMessage = mode == WalletAuthenticationModeEnum.ManagedIdentity
                ? "Identidade do servidor e assinatura remota validadas. Pronto para ativar."
                : "Chave privada e assinatura JWT validadas. Pronto para ativar.";
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException or InvalidOperationException)
        {
            row.LastValidationMessage = SafeValidationError(exception);
        }
        finally
        {
            if (!string.IsNullOrEmpty(privateKey))
            {
                var bytes = Encoding.UTF8.GetBytes(privateKey);
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        Audit(licenseId, row, scope.Value.UserId, scope.Value.UserName, "Configured");
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToStatus(row));
    }

    [HttpPut("apple")]
    public async Task<IActionResult> SaveApple(Guid licenseId, [FromBody] AppleWalletConfigurationViewModel input, CancellationToken cancellationToken)
    {
        var scope = await RequireAdministratorAsync(licenseId, cancellationToken);
        if (scope is null) return Forbid();
        if (!protector.IsConfigured) return BadRequest(new { Errors = "O cofre de carteiras ainda nao possui uma chave mestra valida." });

        var row = await FindOrCreateAsync(scope.Value.EnterpriseId, WalletProviderEnum.Apple, cancellationToken);
        var certificateBase64 = string.IsNullOrWhiteSpace(input.CertificateBase64)
            ? ExistingSecret(row, "apple-pfx")
            : input.CertificateBase64.Trim();
        var certificatePassword = string.IsNullOrEmpty(input.CertificateBase64)
            ? ExistingSecret(row, "apple-pfx-password")
            : input.CertificatePassword;
        if (string.IsNullOrWhiteSpace(certificateBase64))
            return BadRequest(new { Errors = "Selecione o certificado .p12 ou .pfx do Pass Type ID." });

        row.AuthenticationMode = WalletAuthenticationModeEnum.Certificate;
        row.PassTypeIdentifier = input.PassTypeIdentifier.Trim();
        row.TeamIdentifier = input.TeamIdentifier.Trim();
        row.CredentialSecret = protector.Protect(certificateBase64, scope.Value.EnterpriseId, "apple-pfx");
        row.CredentialPassword = protector.Protect(certificatePassword, scope.Value.EnterpriseId, "apple-pfx-password");
        row.IsActive = false;
        row.IsValidated = false;
        row.LastValidatedAt = null;
        row.LastValidationMessage = "Validacao pendente.";
        Touch(row, scope.Value.UserId, scope.Value.UserName);

        try
        {
            var pfx = Convert.FromBase64String(certificateBase64);
            try
            {
                using var signer = new X509Certificate2(pfx, certificatePassword, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
                if (!signer.HasPrivateKey) throw new CryptographicException("O certificado nao possui chave privada.");
                if (signer.NotAfter.ToUniversalTime() <= DateTime.UtcNow) throw new CryptographicException("O certificado Apple esta expirado.");
                row.CredentialFingerprint = signer.Thumbprint;
                row.CredentialExpiresAt = signer.NotAfter.ToUniversalTime();

                var wwdr = await ResolveWwdrAsync(input.WwdrCertificateBase64, row.IntermediateCertificate, cancellationToken);
                using var intermediate = LoadCertificate(wwdr);
                ValidateAppleSignature(signer, intermediate);
                row.IntermediateCertificate = wwdr;
                row.IsValidated = true;
                row.LastValidatedAt = DateTime.UtcNow;
                row.LastValidationMessage = "Certificado, chave privada e cadeia WWDR validados. Pronto para ativar.";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or InvalidOperationException or HttpRequestException)
        {
            row.LastValidationMessage = SafeValidationError(exception);
        }

        Audit(licenseId, row, scope.Value.UserId, scope.Value.UserName, "Configured");
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToStatus(row));
    }

    [HttpPatch("{provider}/activation")]
    public async Task<IActionResult> SetActivation(
        Guid licenseId,
        string provider,
        [FromBody] WalletIntegrationActivationViewModel input,
        CancellationToken cancellationToken)
    {
        var scope = await RequireAdministratorAsync(licenseId, cancellationToken);
        if (scope is null) return Forbid();
        if (!Enum.TryParse<WalletProviderEnum>(provider, true, out var parsed)) return NotFound();
        var row = await context.WalletIntegrations.FirstOrDefaultAsync(
            x => x.EnterpriseId == scope.Value.EnterpriseId && x.Provider == parsed,
            cancellationToken);
        if (row is null) return NotFound();
        if (input.IsActive && !row.IsValidated)
            return Conflict(new { Errors = "Valide a configuracao antes de ativar a carteira." });
        if (input.IsActive && row.CredentialExpiresAt is not null && row.CredentialExpiresAt <= DateTime.UtcNow)
            return Conflict(new { Errors = "Substitua o certificado expirado antes de ativar a carteira." });

        row.IsActive = input.IsActive;
        Touch(row, scope.Value.UserId, scope.Value.UserName, incrementVersion: false);
        Audit(licenseId, row, scope.Value.UserId, scope.Value.UserName, input.IsActive ? "Activated" : "Deactivated");
        await context.SaveChangesAsync(cancellationToken);
        return Ok(ToStatus(row));
    }

    private async Task<(Guid EnterpriseId, Guid UserId, string UserName)?> RequireAdministratorAsync(Guid licenseId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId)) return null;
        var licenseEnterprise = await context.Licenses.AsNoTracking().Where(x => x.Id == licenseId).Select(x => (Guid?)x.EnterpriseId).FirstOrDefaultAsync(cancellationToken);
        if (licenseEnterprise != enterpriseId) return null;
        var user = await context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId && x.EnterpriseId == enterpriseId, cancellationToken);
        return user?.AccessType is AccessTypeEnum.Admin or AccessTypeEnum.Developer
            ? (enterpriseId, userId, user.Name)
            : null;
    }

    private async Task<WalletIntegrationDTO> FindOrCreateAsync(Guid enterpriseId, WalletProviderEnum provider, CancellationToken cancellationToken)
    {
        var existing = await context.WalletIntegrations.FirstOrDefaultAsync(x => x.EnterpriseId == enterpriseId && x.Provider == provider, cancellationToken);
        if (existing is not null) return existing;
        var created = new WalletIntegrationDTO
        {
            Id = Guid.NewGuid(), EnterpriseId = enterpriseId, Provider = provider,
            CreatedAt = DateTime.UtcNow
        };
        context.WalletIntegrations.Add(created);
        return created;
    }

    private string ExistingSecret(WalletIntegrationDTO row, string purpose) =>
        string.IsNullOrWhiteSpace(row.CredentialSecret) && purpose != "apple-pfx-password"
            ? string.Empty
            : protector.Unprotect(
                purpose == "apple-pfx-password" ? row.CredentialPassword : row.CredentialSecret,
                row.EnterpriseId,
                purpose);

    private static IReadOnlyDictionary<string, object> ValidationPayload(string issuer) => new Dictionary<string, object>
    {
        ["iss"] = issuer,
        ["aud"] = "google",
        ["typ"] = "savetowallet",
        ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        ["payload"] = new Dictionary<string, object> { ["genericObjects"] = Array.Empty<object>() }
    };

    private async Task<string> ResolveWwdrAsync(string supplied, string existing, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(supplied)) return NormalizeCertificate(supplied);
        if (!string.IsNullOrWhiteSpace(existing)) return existing;
        var bytes = await httpClientFactory.CreateClient().GetByteArrayAsync(AppleWwdrG4Url, cancellationToken);
        try { using var certificate = new X509Certificate2(bytes); return Convert.ToBase64String(certificate.Export(X509ContentType.Cert)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static string NormalizeCertificate(string value)
    {
        using var certificate = LoadCertificate(value);
        return Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    }

    private static X509Certificate2 LoadCertificate(string value) => value.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal)
        ? X509Certificate2.CreateFromPem(value.Replace("\\n", "\n", StringComparison.Ordinal))
        : new X509Certificate2(Convert.FromBase64String(value));

    private static void ValidateAppleSignature(X509Certificate2 signer, X509Certificate2 intermediate)
    {
        var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes("condotify-wallet-validation")), detached: true);
        var cmsSigner = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, signer)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
            DigestAlgorithm = new Oid("1.3.14.3.2.26")
        };
        cmsSigner.Certificates.Add(intermediate);
        cms.ComputeSignature(cmsSigner);
        if (cms.Encode().Length == 0) throw new CryptographicException("A assinatura de teste da Apple nao foi gerada.");
    }

    private static string PrivateKeyFingerprint(string value)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(value);
        return Convert.ToHexString(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo()));
    }

    private static void Touch(WalletIntegrationDTO row, Guid userId, string userName, bool incrementVersion = true)
    {
        if (incrementVersion && row.UpdatedAt != default) row.Version++;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = userId;
        row.UpdatedByName = userName;
    }

    private void Audit(Guid licenseId, WalletIntegrationDTO row, Guid userId, string userName, string action) =>
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "WalletIntegration", EntityId = row.Id,
            Action = action, Status = row.IsValidated ? "Success" : "Warning",
            Summary = $"Integracao {row.Provider} {(action == "Activated" ? "ativada" : action == "Deactivated" ? "desativada" : "atualizada")}.",
            DetailsJson = $"{{\"provider\":\"{row.Provider}\",\"version\":{row.Version},\"validated\":{row.IsValidated.ToString().ToLowerInvariant()}}}",
            UserId = userId, UserName = userName, CreatedAt = DateTime.UtcNow
        });

    private async Task<WalletIntegrationStatusViewModel> LegacyGoogleStatusAsync(Guid enterpriseId, CancellationToken cancellationToken)
    {
        var settings = await store.GetGoogleAsync(enterpriseId, cancellationToken);
        return settings is null ? new() { Provider = "Google" } : new()
        {
            Provider = "Google", Source = "Configuracao legada", AuthenticationMode = settings.AuthenticationMode.ToString(),
            IsConfigured = true, IsValidated = true, IsActive = true, IssuerId = settings.IssuerId,
            ServiceAccountEmail = settings.ServiceAccountEmail, ClassSuffix = settings.ClassSuffix, HasCredential = true,
            LastValidationMessage = "Em uso pelas variaveis antigas. Salve nesta tela para migrar ao cofre."
        };
    }

    private async Task<WalletIntegrationStatusViewModel> LegacyAppleStatusAsync(Guid enterpriseId, CancellationToken cancellationToken)
    {
        var settings = await store.GetAppleAsync(enterpriseId, cancellationToken);
        return settings is null ? new() { Provider = "Apple" } : new()
        {
            Provider = "Apple", Source = "Configuracao legada", AuthenticationMode = "Certificate",
            IsConfigured = true, IsValidated = true, IsActive = true, PassTypeIdentifier = settings.PassTypeIdentifier,
            TeamIdentifier = settings.TeamIdentifier, HasCredential = true,
            LastValidationMessage = "Em uso pelas variaveis antigas. Salve nesta tela para migrar ao cofre."
        };
    }

    private static WalletIntegrationStatusViewModel ToStatus(WalletIntegrationDTO row) => new()
    {
        Provider = row.Provider.ToString(), Source = "Cofre Condotify", AuthenticationMode = row.AuthenticationMode.ToString(),
        IsConfigured = true, IsValidated = row.IsValidated, IsActive = row.IsActive, Version = row.Version,
        IssuerId = row.IssuerId, ServiceAccountEmail = row.ServiceAccountEmail, ClassSuffix = row.ClassSuffix,
        PassTypeIdentifier = row.PassTypeIdentifier, TeamIdentifier = row.TeamIdentifier,
        HasCredential = row.AuthenticationMode == WalletAuthenticationModeEnum.ManagedIdentity || !string.IsNullOrWhiteSpace(row.CredentialSecret),
        CredentialFingerprint = MaskFingerprint(row.CredentialFingerprint), CredentialExpiresAt = row.CredentialExpiresAt,
        LastValidatedAt = row.LastValidatedAt, LastValidationMessage = row.LastValidationMessage,
        UpdatedAt = row.UpdatedAt, UpdatedByName = row.UpdatedByName
    };

    private static string MaskFingerprint(string value) => value.Length <= 16 ? value : $"{value[..8]}...{value[^8..]}";
    private static string SafeValidationError(Exception exception)
    {
        var message = exception.Message.Replace("\r", " ").Replace("\n", " ").Trim();
        return $"Validacao falhou: {(message.Length <= 400 ? message : message[..400])}";
    }
}
