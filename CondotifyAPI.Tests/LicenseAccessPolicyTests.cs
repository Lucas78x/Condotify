using Xunit;

namespace CondotifyAPI.Tests;

public class LicenseAccessPolicyTests
{
    [Fact]
    public void Concierge_ShouldOperateWithoutManagingInfrastructureOrUsers()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Concierge);

        Assert.True(permissions.HasFlag(LicensePermissionEnum.OperateDevices));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageCredentials));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageDeliveries));
        Assert.False(permissions.HasFlag(LicensePermissionEnum.ManageDevices));
        Assert.False(permissions.HasFlag(LicensePermissionEnum.ManageUsers));
        Assert.False(permissions.HasFlag(LicensePermissionEnum.ManageSettings));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageIncidents));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageEmergency));
        Assert.False(permissions.HasFlag(LicensePermissionEnum.ManageAutomations));
    }

    [Fact]
    public void Viewer_ShouldNeverReceiveMutationPermissions()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Viewer);
        var mutationPermissions = LicensePermissionEnum.ManageStructure | LicensePermissionEnum.ManagePeople |
            LicensePermissionEnum.ManageCredentials | LicensePermissionEnum.ManageDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ManageDeliveries |
            LicensePermissionEnum.ManageUsers | LicensePermissionEnum.ManageSettings |
            LicensePermissionEnum.ManageIncidents | LicensePermissionEnum.ManageAutomations |
            LicensePermissionEnum.ManageEmergency;

        Assert.Equal(LicensePermissionEnum.None, permissions & mutationPermissions);
    }

    [Fact]
    public void Administrator_ShouldReceiveEveryDefinedPermission()
    {
        Assert.Equal(LicensePermissionEnum.All, LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Administrator));
    }

    [Fact]
    public void CustomMutationPermissions_ShouldIncludeRequiredReadAccess()
    {
        var permissions = LicenseAccessDefaults.Normalize(
            LicensePermissionEnum.ManagePeople |
            LicensePermissionEnum.ManageCredentials |
            LicensePermissionEnum.OperateDevices);

        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewPeople));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewStructure));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewCredentials));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewDevices));
    }

    [Fact]
    public void ManageBookings_ShouldImplyViewBookings()
    {
        var normalized = LicenseAccessDefaults.Normalize(LicensePermissionEnum.ManageBookings);
        Assert.True(normalized.HasFlag(LicensePermissionEnum.ViewBookings));
    }

    [Fact]
    public void Concierge_ShouldBeAbleToManageBookings()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Concierge);
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewBookings));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageBookings));
    }

    [Fact]
    public void SafetyManagement_ShouldImplyEveryRequiredReadPermission()
    {
        var permissions = LicenseAccessDefaults.Normalize(
            LicensePermissionEnum.ManageIncidents |
            LicensePermissionEnum.ManageAutomations |
            LicensePermissionEnum.ManageEmergency);

        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewIncidents));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewAutomations));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewEmergency));
    }

    [Fact]
    public void Operator_ShouldTreatIncidentsWithoutActivatingEmergency()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Operator);

        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageIncidents));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewEmergency));
        Assert.False(permissions.HasFlag(LicensePermissionEnum.ManageEmergency));
    }
}
