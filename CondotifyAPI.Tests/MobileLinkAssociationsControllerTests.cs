using System.Text.Json;
using CondotifyAPI.Controllers;

namespace CondotifyAPI.Tests;

public sealed class MobileLinkAssociationsControllerTests
{
    private const string Fingerprint = "AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99:AA:BB:CC:DD:EE:FF:00:11:22:33:44:55:66:77:88:99";

    [Fact]
    public void BuildAndroid_UsesConfiguredIdentityOnly()
    {
        var value = MobileLinkAssociationsController.BuildAndroid("br.com.condotify.app", [Fingerprint]);
        var json = JsonSerializer.Serialize(value);

        Assert.Contains("br.com.condotify.app", json);
        Assert.Contains(Fingerprint, json);
        Assert.Contains("delegate_permission/common.handle_all_urls", json);
    }

    [Theory]
    [InlineData("", "AA")]
    [InlineData("invalid", Fingerprint)]
    [InlineData("br.com.condotify.app", "invalid")]
    public void BuildAndroid_RejectsMissingOrInvalidProductionValues(string packageName, string fingerprint) =>
        Assert.Null(MobileLinkAssociationsController.BuildAndroid(packageName, [fingerprint]));

    [Fact]
    public void BuildApple_RestrictsAssociationToAppRoutes()
    {
        var value = MobileLinkAssociationsController.BuildApple("ABCDE12345", "br.com.condotify.app");
        var json = JsonSerializer.Serialize(value);

        Assert.Contains("ABCDE12345.br.com.condotify.app", json);
        Assert.Contains("/app/*", json);
    }

    [Theory]
    [InlineData("", "br.com.condotify.app")]
    [InlineData("ABCDE12345", "invalid")]
    public void BuildApple_RejectsIncompleteIdentity(string teamId, string bundleId) =>
        Assert.Null(MobileLinkAssociationsController.BuildApple(teamId, bundleId));
}
