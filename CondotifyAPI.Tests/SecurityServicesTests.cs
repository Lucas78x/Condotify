using CondotifyAPI.Services.Security;
using DigitalWorldOnline.Management.Api.Controllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using DigitalWorldOnline.Management.Api.Data;

namespace CondotifyAPI.Tests;

[CollectionDefinition("Security services", DisableParallelization = true)]
public sealed class SecurityServicesCollection
{
    public const string Name = "Security services";
}

[Collection(SecurityServicesCollection.Name)]
public sealed class SecurityServicesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("configured", null)]
    [InlineData(null, "supplied")]
    [InlineData("configured", "different")]
    public void ApiKey_ShouldRejectMissingOrDifferentValues(string? configured, string? supplied)
    {
        Assert.False(ApiKeySecurity.IsValid(configured, supplied));
    }

    [Fact]
    public void ApiKey_ShouldAcceptOnlyTheExactConfiguredValue()
    {
        Assert.True(ApiKeySecurity.IsValid("private-integration-key", "private-integration-key"));
        Assert.False(ApiKeySecurity.IsValid("private-integration-key", "Private-Integration-Key"));
        Assert.False(ApiKeySecurity.IsValid(" private-integration-key", "private-integration-key"));
    }

    [Fact]
    public void UserAccessController_ShouldBeDiscoverableAsATopLevelApiController()
    {
        var type = typeof(UserAccessController);

        Assert.False(type.IsNested);
        Assert.True(type.IsPublic);
        Assert.NotNull(type.GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true).SingleOrDefault());
    }

    [Fact]
    public void UserCreationInput_ShouldLetValidatorHandleInvalidEnterpriseId()
    {
        var input = new CreateUserAccessByEnterpriseIn
        {
            EnterpriseId = "not-a-guid",
            Name = "Test",
            Email = "test@example.com",
            Password = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("secret123"))
        };

        var command = input.ToCommand();

        Assert.Equal(Guid.Empty, command.EnterpriseId);
    }

    [Fact]
    public void UserCreationConverters_ShouldNotSwapCpfAndRg()
    {
        var input = new CreateUserAccessIn
        {
            EnterpriseId = Guid.NewGuid().ToString(),
            Name = "Test",
            Email = "test@example.com",
            Password = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("sênha123")),
            CPF = "12345678901",
            RG = "11223344"
        };
        var byEnterprise = new CreateUserAccessByEnterpriseIn
        {
            EnterpriseId = Guid.NewGuid().ToString(),
            Name = input.Name,
            Email = input.Email,
            Password = input.Password,
            CPF = input.CPF,
            RG = input.RG
        };

        var command = input.ToCommand();
        var enterpriseCommand = byEnterprise.ToCommand();

        Assert.Equal("12345678901", command.CPF);
        Assert.Equal("11223344", command.RG);
        Assert.Equal("sênha123", command.Password);
        Assert.Equal("12345678901", enterpriseCommand.CPF);
        Assert.Equal("11223344", enterpriseCommand.RG);
    }

    [Fact]
    public void Totp_ShouldMatchRfc6238VectorAndAcceptAdjacentWindow()
    {
        var service = new TotpService();
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var instant = DateTimeOffset.FromUnixTimeSeconds(59).UtcDateTime;

        Assert.True(service.Verify(secret, "287082", instant));
        Assert.True(service.Verify(secret, "287082", instant.AddSeconds(30)));
        Assert.False(service.Verify(secret, "000000", instant));
    }

    [Fact]
    public void TotpUri_ShouldEncodeAccountAndDeclareExpectedParameters()
    {
        var uri = new TotpService().BuildUri("ABCDEF234567", "user+access@example.com");

        Assert.Contains("Condotify:user%2Baccess%40example.com", uri);
        Assert.Contains("issuer=Condotify", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
    }

    [Fact]
    public async Task PrivateMedia_ShouldEncryptRoundTripAndDeleteFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"condotify-media-{Guid.NewGuid():N}");
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", "test-media-secret-with-enough-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", root);
            var configuration = new ConfigurationBuilder().Build();
            var store = new PrivateMediaStore(configuration);
            var licenseId = Guid.NewGuid();
            const string content = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB";

            var reference = await store.StoreDataUriAsync(licenseId, content);
            Assert.StartsWith($"/private-media/{licenseId:D}/", reference);

            var mediaId = Guid.Parse(reference.Split('/').Last());
            var encryptedPath = Path.Combine(root, licenseId.ToString("N"), $"{mediaId:N}.bin");
            Assert.True(File.Exists(encryptedPath));
            Assert.DoesNotContain("iVBORw0KGgo", Convert.ToBase64String(await File.ReadAllBytesAsync(encryptedPath)));

            var resolved = await store.ResolveDataUriAsync(licenseId, reference);
            Assert.Equal(content, resolved);

            await store.DeleteAsync(licenseId, reference);
            Assert.False(File.Exists(encryptedPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", previousPath);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task PrivateMedia_ShouldRejectTamperedCiphertextWithoutThrowing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"condotify-media-{Guid.NewGuid():N}");
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", "test-media-secret-with-enough-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", root);
            var store = new PrivateMediaStore(new ConfigurationBuilder().Build());
            var licenseId = Guid.NewGuid();
            var reference = await store.StoreDataUriAsync(
                licenseId,
                "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB");
            var mediaId = Guid.Parse(reference.Split('/').Last());
            var encryptedPath = Path.Combine(root, licenseId.ToString("N"), $"{mediaId:N}.bin");
            var payload = await File.ReadAllBytesAsync(encryptedPath);
            payload[^1] ^= 0xFF;
            await File.WriteAllBytesAsync(encryptedPath, payload);

            var result = await store.ReadAsync(licenseId, mediaId);

            Assert.Null(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", previousPath);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PrivateMedia_ShouldUseStableContentRootByDefault()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"condotify-root-{Guid.NewGuid():N}");
        var previousSecret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET");
        var previousPath = Environment.GetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH");
        try
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", "test-media-secret-with-enough-entropy");
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", null);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [HostDefaults.ContentRootKey] = contentRoot
                })
                .Build();

            _ = new PrivateMediaStore(configuration);

            Assert.True(Directory.Exists(Path.Combine(contentRoot, "App_Data", "private-media")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", previousSecret);
            Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", previousPath);
            if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, true);
        }
    }
}
