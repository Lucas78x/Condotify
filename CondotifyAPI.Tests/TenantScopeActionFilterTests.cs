using System.Security.Claims;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace CondotifyAPI.Tests;

public sealed class TenantScopeActionFilterTests
{
    private sealed class FakeLicenseAuthorizationService(HashSet<Guid> ids) : ILicenseAuthorizationService
    {
        public Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(ids);
        public Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(ClaimsPrincipal principal, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeResidentAuthorizationService(ResidentAccessGrant? grant) : IResidentAuthorizationService
    {
        public Task<ResidentAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(grant);
        public Task<bool> CanAccessUnitAsync(ClaimsPrincipal principal, Guid unitId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static ActionExecutingContext BuildContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);
    }

    private static ClaimsPrincipal StaffPrincipal(Guid enterpriseId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("enterprise_id", enterpriseId.ToString()),
        new Claim("principal_type", "user")
    ], "TestAuth"));

    private static ClaimsPrincipal ResidentPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("principal_type", "resident")
    ], "TestAuth"));

    [Fact]
    public async Task Staff_PopulatesAccessorFromLicenseAuthorizationService()
    {
        var enterpriseId = Guid.NewGuid();
        var licenseIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService(licenseIds), new FakeResidentAuthorizationService(null));
        var context = BuildContext(StaffPrincipal(enterpriseId));
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () => { nextCalled = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.True(nextCalled);
        Assert.Equal(licenseIds, tenant.AccessibleLicenseIds);
        Assert.Equal(enterpriseId, tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_PopulatesAccessorWithOwnLicenseOnly()
    {
        var grant = new ResidentAccessGrant(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], ResidentAccessTypeEnum.Responsible, true);
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(grant));
        var context = BuildContext(ResidentPrincipal());

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Single(tenant.AccessibleLicenseIds!);
        Assert.Contains(grant.LicenseId, tenant.AccessibleLicenseIds!);
        Assert.Null(tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_WithNoGrant_PopulatesEmptySet_NotNull()
    {
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(null));
        var context = BuildContext(ResidentPrincipal());

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.NotNull(tenant.AccessibleLicenseIds);
        Assert.Empty(tenant.AccessibleLicenseIds!);
    }

    [Fact]
    public async Task UnauthenticatedRequest_LeavesAccessorUnpopulated()
    {
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([Guid.NewGuid()]), new FakeResidentAuthorizationService(null));
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()));

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Null(tenant.AccessibleLicenseIds);
    }
}
