using Condotify.Models;

namespace Condotify.ApiClient.Tests;

public sealed class MobileDeepLinksTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Theory]
    [InlineData("/home", "/home")]
    [InlineData("profile", "/profile")]
    [InlineData("https://app.condotify.com.br/app/home", "/home")]
    [InlineData("condotify://app/profile", "/profile")]
    public void TryNormalize_AcceptsKnownStaticRoutes(string input, string expected)
    {
        Assert.True(MobileDeepLinks.TryNormalize(input, out var route));
        Assert.Equal(expected, route);
    }

    [Fact]
    public void TryNormalize_NormalizesEntityRouteAndGuid()
    {
        Assert.True(MobileDeepLinks.TryNormalize($"/VISITORS/{Id:N}", out var route));
        Assert.Equal($"/visitors/{Id:D}", route);
    }

    [Fact]
    public void ToCanonicalUrl_ProducesVerifiedHttpsHost()
    {
        Assert.Equal(
            $"https://app.condotify.com.br/app/deliveries/{Id:D}",
            MobileDeepLinks.ToCanonicalUrl($"/deliveries/{Id}"));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///home")]
    [InlineData("https://evil.example/app/home")]
    [InlineData("https://app.condotify.com.br:8443/app/home")]
    [InlineData("https://user@app.condotify.com.br/app/home")]
    [InlineData("/../../seguranca")]
    [InlineData("/visitors/not-a-guid")]
    [InlineData("/unknown")]
    [InlineData("/home?redirect=https://evil.example")]
    public void TryNormalize_RejectsUntrustedOrUnknownRoutes(string input)
    {
        Assert.False(MobileDeepLinks.TryNormalize(input, out _));
    }
}
