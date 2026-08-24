using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class MobileRouteAuthorizationTests
{
    [Theory]
    [InlineData("/concierge")]
    [InlineData("/access/events/149328be-3fe6-4510-a5f4-0df1ca947106")]
    [InlineData("/devices")]
    [InlineData("/people/149328be-3fe6-4510-a5f4-0df1ca947106")]
    public void Resident_CannotOpenStaffRoutes(string route) =>
        Assert.False(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Resident, route));

    [Theory]
    [InlineData("/financeiro")]
    [InlineData("/documentos")]
    [InlineData("/comunicados")]
    [InlineData("/assembleias")]
    [InlineData("/assembleias/149328be-3fe6-4510-a5f4-0df1ca947106")]
    public void Staff_CannotOpenResidentRoutes(string route) =>
        Assert.False(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Staff, route));

    [Theory]
    [InlineData("/home")]
    [InlineData("/visitors/149328be-3fe6-4510-a5f4-0df1ca947106")]
    [InlineData("/cameras")]
    [InlineData("/ocorrencias")]
    public void SharedRoutes_AreAvailableToBothProfiles(string route)
    {
        Assert.True(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Staff, route));
        Assert.True(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Resident, route));
    }

    [Fact]
    public void UnknownRoute_IsDenied() =>
        Assert.False(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Staff, "/admin"));

    [Theory]
    [InlineData("/cameras", LicenseModuleEnum.Cameras)]
    [InlineData("/bookings", LicenseModuleEnum.Bookings)]
    [InlineData("/ocorrencias", LicenseModuleEnum.Incidents)]
    [InlineData("/financeiro", LicenseModuleEnum.Finance)]
    [InlineData("/boletos", LicenseModuleEnum.Finance)]
    [InlineData("/assembleias", LicenseModuleEnum.Assemblies)]
    public void DisabledModule_CannotBeOpenedDirectly(string route, LicenseModuleEnum module)
    {
        var allExceptModule = (long)LicenseModuleEnum.All & ~(long)module;

        Assert.False(MobileRouteAuthorization.IsAllowed(MobilePrincipalKind.Resident, route, allExceptModule));
    }
}
