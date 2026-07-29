using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Data.People;
using CondotifyAPI.Data.Structure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Auditing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ManagementCrudEndpointTests
{
    public static TheoryData<Type, string, Type, string, LicensePermissionEnum> Endpoints => new()
    {
        { typeof(LicenseStructureController), nameof(LicenseStructureController.UpdateBlock), typeof(HttpPatchAttribute), "blocks/{blockId:guid}", LicensePermissionEnum.ManageStructure },
        { typeof(LicenseStructureController), nameof(LicenseStructureController.DeleteBlock), typeof(HttpDeleteAttribute), "blocks/{blockId:guid}", LicensePermissionEnum.ManageStructure },
        { typeof(LicenseStructureController), nameof(LicenseStructureController.UpdateUnit), typeof(HttpPatchAttribute), "units/{unitId:guid}", LicensePermissionEnum.ManageStructure },
        { typeof(LicenseStructureController), nameof(LicenseStructureController.DeleteUnit), typeof(HttpDeleteAttribute), "units/{unitId:guid}", LicensePermissionEnum.ManageStructure },
        { typeof(LicenseStructureController), nameof(LicenseStructureController.UpdateAccessDevice), typeof(HttpPatchAttribute), "devices/{deviceId:guid}", LicensePermissionEnum.ManageDevices },
        { typeof(LicenseStructureController), nameof(LicenseStructureController.DeleteAccessDevice), typeof(HttpDeleteAttribute), "devices/{deviceId:guid}", LicensePermissionEnum.ManageDevices },
        { typeof(PeopleManagementController), nameof(PeopleManagementController.UpdateVehicle), typeof(HttpPatchAttribute), "residents/{residentId:guid}/vehicles/{vehicleId:guid}", LicensePermissionEnum.ManagePeople },
        { typeof(PeopleManagementController), nameof(PeopleManagementController.DeleteVehicle), typeof(HttpDeleteAttribute), "residents/{residentId:guid}/vehicles/{vehicleId:guid}", LicensePermissionEnum.ManagePeople },
        { typeof(PeopleManagementController), nameof(PeopleManagementController.DeleteResident), typeof(HttpDeleteAttribute), "residents/{residentId:guid}", LicensePermissionEnum.ManagePeople }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void ManagementEndpoints_ShouldExposeExpectedRouteAndPermission(
        Type controller,
        string methodName,
        Type httpAttributeType,
        string route,
        LicensePermissionEnum permission)
    {
        var method = controller.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);

        var permissionAttribute = Assert.Single(
            method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(permission, Assert.IsType<LicensePermissionEnum>(Assert.Single(permissionAttribute.Arguments!)));
    }

    [Fact]
    public void DeviceUpdate_ShouldKeepPasswordOptional()
    {
        var input = new UpdateAccessDeviceIn
        {
            Name = "Portaria",
            IPAddress = "192.168.0.10",
            Port = 80,
            Username = "admin"
        };

        Assert.Null(input.Password);
    }

    [Fact]
    public void VehicleUpdate_ShouldReuseCreateFieldsAndCarryStatus()
    {
        var input = new UpdateVehicleIn
        {
            UnitId = Guid.NewGuid(),
            Plate = "ABC1D23",
            TagIdentifier = "TAG-100",
            IsActive = false
        };

        Assert.IsAssignableFrom<CreateVehicleIn>(input);
        Assert.False(input.IsActive);
        Assert.Equal("TAG-100", input.TagIdentifier);
    }

    [Fact]
    public void AuditChangeTracker_ShouldReturnOnlyFieldNames()
    {
        const string oldCpf = "11111111111";
        const string newCpf = "22222222222";

        var fields = AuditChangeTracker.GetChangedFieldNames(
            new { Name = "Ana", CPF = oldCpf, IsActive = true },
            new { Name = "Ana", CPF = newCpf, IsActive = false });
        var serialized = System.Text.Json.JsonSerializer.Serialize(fields);

        Assert.Equal(["CPF", "IsActive"], fields);
        Assert.DoesNotContain(oldCpf, serialized);
        Assert.DoesNotContain(newCpf, serialized);
    }

    [Theory]
    [InlineData(nameof(StructureImportsController.Preview), typeof(HttpPostAttribute), "structure/preview")]
    [InlineData(nameof(StructureImportsController.Execute), typeof(HttpPostAttribute), "structure/execute")]
    public void ImportEndpoints_ShouldRequireStructureAndPeoplePermissions(
        string methodName,
        Type httpAttributeType,
        string route)
    {
        var method = typeof(StructureImportsController).GetMethod(methodName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        var permissions = method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true)
            .Select(x => Assert.IsType<LicensePermissionEnum>(Assert.Single(x.Arguments!)))
            .ToHashSet();
        Assert.True(permissions.SetEquals(
            [LicensePermissionEnum.ManageStructure, LicensePermissionEnum.ManagePeople]));
    }

    [Theory]
    [InlineData(nameof(RecycleBinController.Get), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(RecycleBinController.Restore), typeof(HttpPostAttribute), "{itemId:guid}/restore")]
    [InlineData(nameof(RecycleBinController.Purge), typeof(HttpDeleteAttribute), "{itemId:guid}")]
    public void RecycleBinEndpoints_ShouldExposeExpectedContracts(
        string methodName,
        Type httpAttributeType,
        string? route)
    {
        var method = typeof(RecycleBinController).GetMethod(methodName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
    }

    [Theory]
    [InlineData(nameof(ConfigurationBackupsController.Create), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(ConfigurationBackupsController.Preview), typeof(HttpPostAttribute), "{backupId:guid}/preview")]
    [InlineData(nameof(ConfigurationBackupsController.Restore), typeof(HttpPostAttribute), "{backupId:guid}/restore")]
    [InlineData(nameof(ConfigurationBackupsController.Delete), typeof(HttpDeleteAttribute), "{backupId:guid}")]
    public void BackupMutationEndpoints_ShouldRequireManagePermission(
        string methodName,
        Type httpAttributeType,
        string? route)
    {
        var method = typeof(ConfigurationBackupsController).GetMethod(methodName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        var permission = Assert.Single(
            method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(
            LicensePermissionEnum.ManageBackups,
            Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }

    [Fact]
    public void BackupController_ShouldRequireViewPermission()
    {
        var permission = Assert.Single(
            typeof(ConfigurationBackupsController)
                .GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));

        Assert.Equal(
            LicensePermissionEnum.ViewBackups,
            Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }

    [Fact]
    public void BackupPermissions_ShouldBeIncludedForAdministratorsAndNormalized()
    {
        var administrator = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Administrator);
        var normalized = LicenseAccessDefaults.Normalize(LicensePermissionEnum.ManageBackups);

        Assert.True(administrator.HasFlag(LicensePermissionEnum.ViewBackups));
        Assert.True(administrator.HasFlag(LicensePermissionEnum.ManageBackups));
        Assert.True(normalized.HasFlag(LicensePermissionEnum.ViewBackups));
    }
}
