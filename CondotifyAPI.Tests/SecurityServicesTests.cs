using CondotifyAPI.Services.Security;
using Microsoft.Extensions.Configuration;

namespace CondotifyAPI.Tests;

[CollectionDefinition("Security services", DisableParallelization = true)]
public sealed class SecurityServicesCollection
{
    public const string Name = "Security services";
}

[Collection(SecurityServicesCollection.Name)]
public sealed class SecurityServicesTests
{
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
}
