using CondotifyAPI.Services.Security;
using Microsoft.Extensions.Configuration;

namespace CondotifyAPI.Tests;

public sealed class WalletSecretProtectorTests
{
    private static WalletSecretProtector Create() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WalletEncryption:Secret"] = "wallet-test-master-secret-with-more-than-32-characters"
        })
        .Build());

    [Fact]
    public void Protect_ShouldEncryptAndRoundTripInsideTheSameEnterpriseAndPurpose()
    {
        var enterpriseId = Guid.NewGuid();
        var protector = Create();

        var encrypted = protector.Protect("private-material", enterpriseId, "google-private-key");

        Assert.StartsWith("wallet:v1:", encrypted, StringComparison.Ordinal);
        Assert.DoesNotContain("private-material", encrypted, StringComparison.Ordinal);
        Assert.Equal("private-material", protector.Unprotect(encrypted, enterpriseId, "google-private-key"));
    }

    [Fact]
    public void Unprotect_ShouldRejectCiphertextMovedToAnotherEnterprise()
    {
        var protector = Create();
        var encrypted = protector.Protect("private-material", Guid.NewGuid(), "google-private-key");

        Assert.Throws<InvalidOperationException>(() =>
            protector.Unprotect(encrypted, Guid.NewGuid(), "google-private-key"));
    }
}
