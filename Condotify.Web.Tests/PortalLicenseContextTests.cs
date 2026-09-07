using Condotify.Models;
using Condotify.Services;

namespace Condotify.Web.Tests;

public sealed class PortalLicenseContextTests
{
    [Fact]
    public void SwitchingLicense_DropsPreviousPermissionsAndUpdatesDestinations()
    {
        var context = new PortalLicenseContext();
        var notifications = 0;
        context.Changed += () => notifications++;
        var first = new LicenseViewModel { Id = Guid.NewGuid().ToString(), UrlKey = "primeiro" };
        var second = new LicenseViewModel { Id = Guid.NewGuid().ToString(), UrlKey = "segundo" };
        context.Select(first);
        context.Select(second);
        Assert.Null(context.Administration);
        Assert.False(context.Has(LicensePermission.ManageCredentials));
        Assert.Equal("/condominios/segundo/credenciais", context.Workspace("credenciais"));
        Assert.Equal($"/portaria?licenseId={second.Id}", context.Concierge);
        Assert.Equal(2, notifications);
        context.Select(null);
        Assert.Equal("/licencas", context.Workspace("credenciais"));
        Assert.Equal("/portaria", context.Concierge);
    }
}
