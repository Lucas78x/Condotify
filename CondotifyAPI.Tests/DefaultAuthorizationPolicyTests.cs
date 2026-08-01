using System.Security.Claims;
using CondotifyAPI.Jwt;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace CondotifyAPI.Tests;

/// <summary>
/// Task 2's entire purpose is that every existing bare [Authorize] route - none of
/// which are edited - becomes staff-only because the DEFAULT policy now requires
/// principal_type=user. JwtPrincipalTypeTests proves the token side (which claim
/// each principal type gets); this file proves the policy side: given the exact
/// policy shape wired in Program.cs, a resident-shaped principal is rejected and a
/// staff-shaped principal is accepted. It is evaluated directly against
/// AuthorizationPolicyBuilder/AuthorizationHandlerContext rather than through a
/// hosted server, since the test project has no WebApplicationFactory-style harness;
/// this still exercises the real policy evaluation pipeline (the requirement
/// objects ARE their own IAuthorizationHandler), not a re-implementation of it.
/// </summary>
public class DefaultAuthorizationPolicyTests
{
    private static readonly AuthorizationPolicy StaffDefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(PrincipalTypes.Claim, PrincipalTypes.User)
        .Build();

    [Fact]
    public async Task ResidentPrincipal_FailsTheDefaultPolicy()
    {
        var principal = BuildAuthenticatedPrincipal(PrincipalTypes.Resident);

        var succeeded = await Evaluate(StaffDefaultPolicy, principal);

        Assert.False(succeeded);
    }

    [Fact]
    public async Task StaffPrincipal_PassesTheDefaultPolicy()
    {
        var principal = BuildAuthenticatedPrincipal(PrincipalTypes.User);

        var succeeded = await Evaluate(StaffDefaultPolicy, principal);

        Assert.True(succeeded);
    }

    [Fact]
    public async Task UnauthenticatedPrincipal_FailsTheDefaultPolicy()
    {
        // No AllowAnonymous route ever reaches this policy in practice, but the
        // policy itself must still deny a principal with no authenticated identity.
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        var succeeded = await Evaluate(StaffDefaultPolicy, principal);

        Assert.False(succeeded);
    }

    private static ClaimsPrincipal BuildAuthenticatedPrincipal(string principalType)
    {
        // authenticationType must be non-null for ClaimsIdentity.IsAuthenticated to be
        // true, matching a principal that already passed JWT bearer authentication.
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
