using System.Security.Claims;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Http;

namespace CondotifyAPI.Tests;

public sealed class TenantScopePopulationMiddlewareTests
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

    private static async Task<(bool nextCalled, CurrentTenantAccessor tenant)> Run(
        ClaimsPrincipal user, ILicenseAuthorizationService licenseAuth, IResidentAuthorizationService residentAuth)
    {
        var tenant = new CurrentTenantAccessor();
        var httpContext = new DefaultHttpContext { User = user };
        var middleware = new TenantScopePopulationMiddleware(_ => Task.CompletedTask);
        var nextCalled = false;
        await middleware.InvokeAsync(httpContext, tenant, licenseAuth, residentAuth, async () => { nextCalled = true; await Task.CompletedTask; });
        return (nextCalled, tenant);
    }

    [Fact]
    public async Task Staff_PopulatesAccessorFromLicenseAuthorizationService()
    {
        var enterpriseId = Guid.NewGuid();
        var licenseIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };

        var (nextCalled, tenant) = await Run(StaffPrincipal(enterpriseId), new FakeLicenseAuthorizationService(licenseIds), new FakeResidentAuthorizationService(null));

        Assert.True(nextCalled);
        Assert.Equal(licenseIds, tenant.AccessibleLicenseIds);
        Assert.Equal(enterpriseId, tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_PopulatesAccessorWithOwnLicenseOnly()
    {
        var grant = new ResidentAccessGrant(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], ResidentAccessTypeEnum.Responsible, true);

        var (_, tenant) = await Run(ResidentPrincipal(), new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(grant));

        Assert.Single(tenant.AccessibleLicenseIds!);
        Assert.Contains(grant.LicenseId, tenant.AccessibleLicenseIds!);
        Assert.Null(tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_WithNoGrant_PopulatesEmptySet_NotNull()
    {
        var (_, tenant) = await Run(ResidentPrincipal(), new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(null));

        Assert.NotNull(tenant.AccessibleLicenseIds);
        Assert.Empty(tenant.AccessibleLicenseIds!);
    }

    [Fact]
    public async Task UnauthenticatedRequest_LeavesAccessorUnpopulated()
    {
        var (nextCalled, tenant) = await Run(new ClaimsPrincipal(new ClaimsIdentity()), new FakeLicenseAuthorizationService([Guid.NewGuid()]), new FakeResidentAuthorizationService(null));

        Assert.True(nextCalled);
        Assert.Null(tenant.AccessibleLicenseIds);
    }
}
