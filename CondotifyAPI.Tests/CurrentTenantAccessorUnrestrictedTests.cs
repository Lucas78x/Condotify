using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class CurrentTenantAccessorUnrestrictedTests
{
    [Fact]
    public void CurrentTenantAccessor_StartsNotUnrestricted()
    {
        var accessor = new CurrentTenantAccessor();

        Assert.False(accessor.IsUnrestricted);
    }

    [Fact]
    public void CurrentTenantAccessor_MarkUnrestricted_SetsFlag()
    {
        var accessor = new CurrentTenantAccessor();

        accessor.MarkUnrestricted();

        Assert.True(accessor.IsUnrestricted);
    }

    [Fact]
    public void NullCurrentTenantAccessor_IsNeverUnrestricted()
    {
        Assert.False(NullCurrentTenantAccessor.Instance.IsUnrestricted);
    }

    [Fact]
    public void NullCurrentTenantAccessor_MarkUnrestricted_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => NullCurrentTenantAccessor.Instance.MarkUnrestricted());
    }
}
