using System.Security.Claims;
using CondotifyAPI.Services.Security;
using CondotifyAPI.Jwt;
using Microsoft.AspNetCore.Http;

namespace CondotifyAPI.Tests;

public sealed class FirstAccessPasswordChangeMiddlewareTests
{
    [Fact]
    public async Task FirstAccessUser_IsBlockedFromOperationalEndpoints()
    {
        var nextCalled = false;
        var middleware = new FirstAccessPasswordChangeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = Context("/api/access/licenses", firstAccess: true);

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task FirstAccessUser_CanChangePassword()
    {
        var nextCalled = false;
        var middleware = new FirstAccessPasswordChangeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = Context("/api/auth/password/change", firstAccess: true);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task RegularUser_IsNotBlocked()
    {
        var nextCalled = false;
        var middleware = new FirstAccessPasswordChangeMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var context = Context("/api/access/licenses", firstAccess: false);

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }

    private static DefaultHttpContext Context(string path, bool firstAccess)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(PrincipalTypes.Claim, PrincipalTypes.User),
            new Claim("first_access", firstAccess ? "true" : "false")
        ], "Tests"));
        return context;
    }
}
