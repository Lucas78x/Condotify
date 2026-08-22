using System.Security.Claims;
using Condotify.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Condotify.ApiClient.Tests;

public class ClaimsSessionContextProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_ReturnsToken_WhenClaimPresent()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "jwt-abc-123"));

        Assert.Equal("jwt-abc-123", await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenClaimMissing()
    {
        var provider = CreateProvider(new Claim(ClaimTypes.Email, "user@condotify.local"));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenClaimIsWhitespace()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "   "));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenUserIsAnonymous()
    {
        var provider = new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsNull_WhenUserIsAnonymous()
    {
        var provider = new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Null(await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsValue_WhenClaimPresent()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.EnterpriseIdClaim, "8f1d0d3e-0000-4a1b-9c2d-000000000001"));

        Assert.Equal("8f1d0d3e-0000-4a1b-9c2d-000000000001", await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsNull_WhenClaimMissing()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "jwt-abc-123"));

        Assert.Null(await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsNull_WhenClaimIsWhitespace()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.EnterpriseIdClaim, "   "));

        Assert.Null(await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public void ClaimNames_KeepTheValuesTheCookieAlreadyStores()
    {
        Assert.Equal("condotify_access_token", ClaimsSessionContextProvider.AccessTokenClaim);
        Assert.Equal("condotify_refresh_token", ClaimsSessionContextProvider.RefreshTokenClaim);
        Assert.Equal("condotify_access_token_expires_at", ClaimsSessionContextProvider.AccessTokenExpiresAtClaim);
        Assert.Equal("enterprise_id", ClaimsSessionContextProvider.EnterpriseIdClaim);
    }

    [Fact]
    public async Task HandleUnauthorizedAsync_RequestsBrowserSessionRecovery()
    {
        var js = new RecordingJsRuntime();
        var provider = new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())), js);

        await provider.HandleUnauthorizedAsync();

        Assert.Equal("condotifySession.handleUnauthorized", js.Identifier);
    }

    private static ClaimsSessionContextProvider CreateProvider(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(identity)));
    }

    private sealed class StubAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public string? Identifier { get; private set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Identifier = identifier;
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Identifier = identifier;
            return ValueTask.FromResult(default(TValue)!);
        }
    }
}
