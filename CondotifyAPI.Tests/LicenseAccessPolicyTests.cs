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
    }

    [Fact]
    public void Viewer_ShouldNeverReceiveMutationPermissions()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Viewer);
        var mutationPermissions = LicensePermissionEnum.ManageStructure | LicensePermissionEnum.ManagePeople |
            LicensePermissionEnum.ManageCredentials | LicensePermissionEnum.ManageDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ManageDeliveries |
            LicensePermissionEnum.ManageUsers | LicensePermissionEnum.ManageSettings;

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
}
