using System.Security.Claims;
using CondotifyAPI.Jwt;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Task 7's session routes (logout, logout/all, sessions) must work for BOTH principal
/// types, but the default policy (proven staff-only by DefaultAuthorizationPolicyTests)
/// must stay untouched. The fix is a second named policy - "AuthenticatedAnyPrincipal",
/// SessionController.AnyPrincipalPolicy - requiring only RequireAuthenticatedUser(), no
/// principal_type claim at all. This proves it accepts both a resident-shaped and a
/// staff-shaped principal (unlike the default policy) while still rejecting an
/// unauthenticated one, evaluated the same way DefaultAuthorizationPolicyTests evaluates
/// the default/staff policy - directly against AuthorizationPolicyBuilder/
/// AuthorizationHandlerContext, since there is no WebApplicationFactory-style harness here.
/// </summary>
public class AnyPrincipalPolicyTests
{
    // Mirrors exactly the policy shape wired for CondotifyAPI.Controllers.SessionController
    // .AnyPrincipalPolicy in Program.cs.
    private static readonly AuthorizationPolicy AnyPrincipalPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    [Fact]
    public async Task ResidentPrincipal_PassesThePolicy()
    {
        var principal = BuildAuthenticatedPrincipal(PrincipalTypes.Resident);

        Assert.True(await Evaluate(AnyPrincipalPolicy, principal));
    }

    [Fact]
    public async Task StaffPrincipal_PassesThePolicy()
    {
        var principal = BuildAuthenticatedPrincipal(PrincipalTypes.User);

        Assert.True(await Evaluate(AnyPrincipalPolicy, principal));
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_FailsThePolicy()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(await Evaluate(AnyPrincipalPolicy, principal));
    }

    private static ClaimsPrincipal BuildAuthenticatedPrincipal(string principalType)
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        identity.AddClaim(new Claim(PrincipalTypes.Claim, principalType));
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> Evaluate(AuthorizationPolicy policy, ClaimsPrincipal principal)
    {
        var context = new AuthorizationHandlerContext(policy.Requirements, principal, resource: null);
        foreach (var requirement in policy.Requirements)
        {
            if (requirement is IAuthorizationHandler handler)
                await handler.HandleAsync(context);
        }

        return context.HasSucceeded;
    }
}
