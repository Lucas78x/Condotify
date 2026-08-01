using CondotifyAPI.Services.Security;
using Xunit;

namespace CondotifyAPI.Tests;

public class InternalRouteIsolationTests
{
    private const int Public = 8080;
    private const int Internal = 8081;

    [Theory]
    [InlineData("/api/internal/media-auth")]
    [InlineData("/api/internal/media-auth/")]
    [InlineData("/API/INTERNAL/MEDIA-AUTH")]
    [InlineData("/api/internal/qualquer-rota-futura")]
    public void InternalRoutes_AreRejected_OnThePublicPort(string path)
    {
        Assert.False(InternalRouteGuard.IsAllowed(path, Public, Internal));
    }

    [Theory]
    [InlineData("/api/internal/media-auth")]
    [InlineData("/API/INTERNAL/MEDIA-AUTH")]
    public void InternalRoutes_AreAllowed_OnTheInternalPort(string path)
    {
        Assert.True(InternalRouteGuard.IsAllowed(path, Internal, Internal));
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/access/licenses")]
    [InlineData("/")]
    [InlineData("/swagger")]
    public void PublicRoutes_AreAllowed_OnBothPorts(string path)
    {
        Assert.True(InternalRouteGuard.IsAllowed(path, Public, Internal));
        Assert.True(InternalRouteGuard.IsAllowed(path, Internal, Internal));
    }

    [Fact]
    public void ARouteMerelyContainingTheWordInternal_IsNotTreatedAsInternal()
    {
        // "internal" no meio do caminho nao deve acionar a regra: so o prefixo.
        Assert.True(InternalRouteGuard.IsAllowed("/api/access/internal-notes", Public, Internal));
    }

    [Fact]
    public void WhenBothPortsAreTheSame_InternalRoutesStillWork()
    {
        // Desenvolvimento local com uma unica porta nao pode ficar impossivel.
        Assert.True(InternalRouteGuard.IsAllowed("/api/internal/media-auth", 5000, 5000));
    }
}
