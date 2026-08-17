using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.IO.Compression;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Services.Operations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CondotifyAPI.Tests;

public sealed class DigitalPassProviderServiceTests
{
    [Fact]
    public async Task Build_ShouldAlwaysReturnTheSecureWebPass()
    {
        var service = new DigitalPassProviderService(new StubStore(), new StubSigner());

        var output = await service.BuildAsync(Pass(), "secret-token", "https://app.condotify.test/passe/secret-token");

        Assert.Equal("https://app.condotify.test/passe/secret-token", output.PublicUrl);
        Assert.Equal("VIS-ABC123", output.CredentialCode);
        Assert.False(output.GoogleWalletConfigured);
        Assert.False(output.AppleWalletConfigured);
    }

    [Fact]
    public async Task Build_ShouldCreateGoogleSaveUrlOnlyWhenSigningConfigurationIsComplete()
    {
        using var rsa = RSA.Create(2048);
        var store = new StubStore(new GoogleWalletSettings(Guid.Empty, "3388000000022000000", "wallet@test.iam.gserviceaccount.com", "condotify_access", WalletAuthenticationModeEnum.PrivateKey, rsa.ExportRSAPrivateKeyPem(), true));
        var service = new DigitalPassProviderService(store, new GoogleWalletJwtSigner(new StubHttpClientFactory()));

        var output = await service.BuildAsync(Pass(), "secret-token", "https://app.condotify.test/passe/secret-token");

        Assert.True(output.GoogleWalletConfigured);
        Assert.StartsWith("https://pay.google.com/gp/v/save/", output.GoogleWalletUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Build_ShouldWorkTwiceInARowWithTheSameConfiguredKey()
    {
        using var rsa = RSA.Create(2048);
        var store = new StubStore(new GoogleWalletSettings(Guid.Empty, "3388000000022000000", "wallet@test.iam.gserviceaccount.com", "condotify_access", WalletAuthenticationModeEnum.PrivateKey, rsa.ExportRSAPrivateKeyPem(), true));
        var service = new DigitalPassProviderService(store, new GoogleWalletJwtSigner(new StubHttpClientFactory()));

        var first = await service.BuildAsync(Pass(), "token-one", "https://app.condotify.test/passe/token-one");
        var second = await service.BuildAsync(Pass(), "token-two", "https://app.condotify.test/passe/token-two");

        Assert.True(first.GoogleWalletConfigured);
        Assert.True(second.GoogleWalletConfigured);
        Assert.StartsWith("https://pay.google.com/gp/v/save/", second.GoogleWalletUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppleWallet_ShouldBuildASignedPkpassWhenCertificatesAreConfigured()
    {
        using var signerKey = RSA.Create(2048);
        var signerRequest = new CertificateRequest("CN=Pass Type ID", signerKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var signer = signerRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        using var wwdrKey = RSA.Create(2048);
        var wwdrRequest = new CertificateRequest("CN=Apple Worldwide Developer Relations", wwdrKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var wwdr = wwdrRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var settings = new AppleWalletSettings(Guid.Empty, "pass.com.condotify.access", "CONDOTIFY1", Convert.ToBase64String(signer.Export(X509ContentType.Pfx, "test")), "test", Convert.ToBase64String(wwdr.Export(X509ContentType.Cert)), true);
        var service = new AppleWalletPassService(new StubStore(apple: settings));

        var bytes = await service.BuildAsync(Pass());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("pass.json"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
        Assert.True(archive.GetEntry("signature")!.Length > 0);
        Assert.NotNull(archive.GetEntry("icon@2x.png"));
    }

    private static DigitalPassDTO Pass()
    {
        var visit = new AccessVisitDTO
        {
            Id = Guid.NewGuid(), VisitorName = "Maria Visitante", Purpose = "Visita",
            ValidFrom = DateTime.UtcNow, ValidTo = DateTime.UtcNow.AddHours(2),
            Credential = new ResidentAccessCredentialDTO { Identifier = "VIS-ABC123" }
        };
        return new DigitalPassDTO
        {
            Id = Guid.NewGuid(), VisitId = visit.Id, Visit = visit,
            License = new LicenseDTO { Name = "Condominio Aurora" },
            Status = DigitalPassStatusEnum.Active, IssuedAt = DateTime.UtcNow,
            ExpiresAt = visit.ValidTo
        };
    }

    private sealed class StubStore(GoogleWalletSettings? google = null, AppleWalletSettings? apple = null) : IWalletIntegrationStore
    {
        public Task<GoogleWalletSettings?> GetGoogleAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult(google);
        public Task<AppleWalletSettings?> GetAppleAsync(Guid enterpriseId, CancellationToken cancellationToken = default) => Task.FromResult(apple);
    }

    private sealed class StubSigner : IGoogleWalletJwtSigner
    {
        public Task<string> SignAsync(IReadOnlyDictionary<string, object> payload, GoogleWalletSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
