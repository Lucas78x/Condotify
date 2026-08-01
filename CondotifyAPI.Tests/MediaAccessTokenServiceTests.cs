using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

public class MediaAccessTokenServiceTests
{
    private const string Secret = "test-secret-com-comprimento-suficiente-para-derivar-chave";

    private static MediaAccessTokenService CreateService() => new(Secret);

    private static MediaAccessGrant Grant(DateTime? expiresAt = null) => new(
        LicenseId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DeviceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Channel: 1,
        UserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        ExpiresAt: expiresAt ?? DateTime.UtcNow.AddSeconds(120));

    [Fact]
    public void Validate_AcceptsAFreshToken_ForTheMatchingPath()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);

        var result = service.Validate(service.Issue(grant), path);

        Assert.NotNull(result);
        Assert.Equal(grant.DeviceId, result!.DeviceId);
        Assert.Equal(grant.UserId, result.UserId);
    }

    [Fact]
    public void Validate_RejectsAnExpiredToken()
    {
        var service = CreateService();
        var grant = Grant(DateTime.UtcNow.AddSeconds(-1));
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);

        Assert.Null(service.Validate(service.Issue(grant), path));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherCamera()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            grant.LicenseId, Guid.Parse("44444444-4444-4444-4444-444444444444"), 1);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherLicense()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            Guid.Parse("55555555-5555-5555-5555-555555555555"), grant.DeviceId, 1);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATamperedToken()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);
        var token = service.Issue(grant);
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.Null(service.Validate(tampered, path));
    }

    [Fact]
    public void Validate_RejectsATokenSignedWithAnotherSecret()
    {
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);
        var token = new MediaAccessTokenService("outro-segredo-completamente-diferente-do-primeiro").Issue(grant);

        Assert.Null(CreateService().Validate(token, path));
    }

    [Fact]
    public void Validate_RejectsGarbage()
    {
        Assert.Null(CreateService().Validate("nao-e-um-token", "qualquer-path"));
        Assert.Null(CreateService().Validate("", "qualquer-path"));
    }

    [Fact]
    public void Issue_ProducesADifferentTokenEachTime_ForTheSameGrant()
    {
        var service = CreateService();
        var grant = Grant();

        Assert.NotEqual(service.Issue(grant), service.Issue(grant));
    }

    [Fact]
    public void PathFor_ProducesAMediaMtxSafeName()
    {
        var path = MediaAccessTokenService.PathFor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            2);

        Assert.DoesNotContain("-", path);
        Assert.Matches("^[A-Za-z0-9_]+$", path);
    }
}
