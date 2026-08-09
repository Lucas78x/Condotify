using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;

namespace CondotifyAPI.Tests;

public sealed class CurrentTenantAccessorTests
{
    [Fact]
    public void CurrentTenantAccessor_StartsWithNullScope()
    {
        var accessor = new CurrentTenantAccessor();

        Assert.Null(accessor.AccessibleLicenseIds);
        Assert.Null(accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void CurrentTenantAccessor_SetAccessibleScope_StoresBothValues()
    {
        var accessor = new CurrentTenantAccessor();
        var licenseIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var enterpriseId = Guid.NewGuid();

        accessor.SetAccessibleScope(licenseIds, enterpriseId);

        Assert.Equal(licenseIds, accessor.AccessibleLicenseIds);
        Assert.Equal(enterpriseId, accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void NullCurrentTenantAccessor_AlwaysReturnsNullScope()
    {
        var accessor = NullCurrentTenantAccessor.Instance;

        Assert.Null(accessor.AccessibleLicenseIds);
        Assert.Null(accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void NullCurrentTenantAccessor_SetAccessibleScope_Throws()
    {
        var accessor = NullCurrentTenantAccessor.Instance;

        Assert.Throws<InvalidOperationException>(() =>
            accessor.SetAccessibleScope([Guid.NewGuid()], Guid.NewGuid()));
    }
}
