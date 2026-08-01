using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

public class MediaAccessTokenServiceTests
{
    private const string Secret = "test-secret-com-comprimento-suficiente-para-derivar-chave";

    private static MediaAccessTokenService CreateService() => new(Secret);

    private static MediaAccessGrant Grant(DateTime? expiresAt = null, StreamQuality quality = StreamQuality.Main) => new(
        LicenseId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DeviceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Channel: 1,
        UserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        ExpiresAt: expiresAt ?? DateTime.UtcNow.AddSeconds(120),
        Quality: quality);

    [Fact]
    public void Validate_AcceptsAFreshToken_ForTheMatchingPath()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel, grant.Quality);

        var result = service.Validate(service.Issue(grant), path);

        Assert.NotNull(result);
        Assert.Equal(grant.DeviceId, result!.DeviceId);
        Assert.Equal(grant.UserId, result.UserId);
        Assert.Equal(grant.Quality, result.Quality);
    }

    [Fact]
    public void Validate_RejectsAnExpiredToken()
    {
        var service = CreateService();
        var grant = Grant(DateTime.UtcNow.AddSeconds(-1));
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel, grant.Quality);

        Assert.Null(service.Validate(service.Issue(grant), path));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherCamera()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            grant.LicenseId, Guid.Parse("44444444-4444-4444-4444-444444444444"), 1, grant.Quality);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherLicense()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            Guid.Parse("55555555-5555-5555-5555-555555555555"), grant.DeviceId, 1, grant.Quality);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherQuality()
    {
        // Um token emitido para a qualidade principal nao pode autorizar leitura
        // do caminho secundario, e vice-versa: a qualidade fica dentro do payload
        // cifrado, entao o cliente nao pode trocar de caminho sem novo token.
        var service = CreateService();
        var grant = Grant(quality: StreamQuality.Main);
        var secondaryPath = MediaAccessTokenService.PathFor(
            grant.LicenseId, grant.DeviceId, grant.Channel, StreamQuality.Secondary);

        Assert.Null(service.Validate(service.Issue(grant), secondaryPath));
    }

    [Fact]
    public void Validate_RejectsATamperedToken()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel, grant.Quality);
        var token = service.Issue(grant);
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.Null(service.Validate(tampered, path));
    }

    [Fact]
    public void Validate_RejectsATokenSignedWithAnotherSecret()
    {
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel, grant.Quality);
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
            2,
            StreamQuality.Main);

        Assert.DoesNotContain("-", path);
        Assert.Matches("^[A-Za-z0-9_]+$", path);
    }

    [Theory]
    [InlineData(StreamQuality.Main, "_m")]
    [InlineData(StreamQuality.Secondary, "_s")]
    public void PathFor_EndsWithTheQualitySuffix_SoDifferentQualitiesNeverCollide(StreamQuality quality, string suffix)
    {
        var path = MediaAccessTokenService.PathFor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            2,
            quality);

        Assert.EndsWith(suffix, path);
    }

    [Fact]
    public void PathFor_ProducesDifferentNames_ForMainAndSecondaryQuality()
    {
        var licenseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var deviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var mainPath = MediaAccessTokenService.PathFor(licenseId, deviceId, 1, StreamQuality.Main);
        var secondaryPath = MediaAccessTokenService.PathFor(licenseId, deviceId, 1, StreamQuality.Secondary);

        Assert.NotEqual(mainPath, secondaryPath);
    }
}
