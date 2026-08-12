using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class OfflineOperationsSecurityContractTests
{
    [Fact]
    public void Model_EncryptsDeviceSecretAndEnforcesIdempotency()
    {
        using var context = CreateContext();
        var device = context.Model.FindEntityType(typeof(OfflineAccessDeviceDTO));
        var operation = context.Model.FindEntityType(typeof(OfflineAccessOperationDTO));

        Assert.NotNull(device);
        Assert.NotNull(operation);
        Assert.NotNull(device!.FindProperty(nameof(OfflineAccessDeviceDTO.DeviceSecret))!.GetValueConverter());
        Assert.Contains(device.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(OfflineAccessDeviceDTO.LicenseId), nameof(OfflineAccessDeviceDTO.InstallationId)]));
        Assert.Contains(operation!.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(OfflineAccessOperationDTO.DeviceId), nameof(OfflineAccessOperationDTO.ClientOperationId)]));
    }

    [Theory]
    [InlineData(nameof(OfflineOperationsController.RegisterDevice), LicensePermissionEnum.ManagePeople)]
    [InlineData(nameof(OfflineOperationsController.Sync), LicensePermissionEnum.ManagePeople)]
    [InlineData(nameof(OfflineOperationsController.Devices), LicensePermissionEnum.ViewSettings)]
    [InlineData(nameof(OfflineOperationsController.UpdateDevice), LicensePermissionEnum.ManageSettings)]
    [InlineData(nameof(OfflineOperationsController.Operations), LicensePermissionEnum.ViewSettings)]
    public void Endpoints_RequireExpectedLicensePermission(string methodName, LicensePermissionEnum permission)
    {
        var method = typeof(OfflineOperationsController).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        var attribute = Assert.Single(method!.GetCustomAttributes<RequireLicensePermissionAttribute>());
        Assert.Equal(permission, Assert.IsType<LicensePermissionEnum>(Assert.Single(attribute.Arguments!)));
    }

    [Fact]
    public void Controller_IsNeverAnonymous()
    {
        var route = typeof(OfflineOperationsController).GetCustomAttribute<RouteAttribute>();

        Assert.Equal("api/access/licenses/{licenseId:guid}/offline", route?.Template);
        Assert.NotNull(typeof(OfflineOperationsController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
    }

    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_Offline_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        return new DatabaseContext(options);
    }
}
