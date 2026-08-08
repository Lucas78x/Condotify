using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class MobileNavigationTests
{
    [Fact]
    public void For_Staff_WithAllModulesEnabled_IncludesCameras()
    {
        var items = MobileNavigation.For(MobilePrincipalKind.Staff, (long)LicenseModuleEnum.All);

        Assert.Contains(items, x => x.Route == "/cameras");
    }

    [Fact]
    public void For_Staff_WithCamerasDisabled_ExcludesCamerasButKeepsCore()
    {
        var enabled = (long)(LicenseModuleEnum.All & ~LicenseModuleEnum.Cameras);

        var items = MobileNavigation.For(MobilePrincipalKind.Staff, enabled);

        Assert.DoesNotContain(items, x => x.Route == "/cameras");
        Assert.Contains(items, x => x.Route == "/home");
        Assert.Contains(items, x => x.Route == "/concierge");
        Assert.Contains(items, x => x.Route == "/more");
    }

    [Fact]
    public void For_Resident_WithBookingsDisabled_ExcludesBookingsButKeepsCore()
    {
        var enabled = (long)(LicenseModuleEnum.All & ~LicenseModuleEnum.Bookings);

        var items = MobileNavigation.For(MobilePrincipalKind.Resident, enabled);

        Assert.DoesNotContain(items, x => x.Route == "/bookings");
        Assert.Contains(items, x => x.Route == "/home");
        Assert.Contains(items, x => x.Route == "/visitors");
    }

    [Fact]
    public void For_DefaultsToAllModulesWhenOverloadOmitted()
    {
        var items = MobileNavigation.For(MobilePrincipalKind.Staff);

        Assert.Contains(items, x => x.Route == "/cameras");
    }
}
