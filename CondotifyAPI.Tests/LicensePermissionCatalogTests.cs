using Condotify.Models;
using DomainPermission = global::LicensePermissionEnum;

namespace CondotifyAPI.Tests;

public sealed class LicensePermissionCatalogTests
{
    [Fact]
    public void ContractPermissionValues_MatchDomainPermissionValues()
    {
        foreach (var domainName in Enum.GetNames<DomainPermission>())
        {
            Assert.True(Enum.TryParse<LicensePermission>(domainName, out var contractPermission), $"Permissão ausente no contrato: {domainName}");
            Assert.Equal((long)Enum.Parse<DomainPermission>(domainName), (long)contractPermission);
        }
    }

    [Theory]
    [InlineData(LicensePermission.ManageFinance, LicensePermission.ViewFinance)]
    [InlineData(LicensePermission.ManageDocuments, LicensePermission.ViewDocuments)]
    [InlineData(LicensePermission.ManageVehicles, LicensePermission.ViewVehicles)]
    public void Normalize_ManagementPermissionAlsoGrantsReading(LicensePermission management, LicensePermission reading)
    {
        var normalized = LicensePermissionCatalog.Normalize((long)management);

        Assert.Equal((long)reading, normalized & (long)reading);
    }

    [Theory]
    [InlineData(LicensePermission.ViewFinance, LicensePermission.ManageFinance)]
    [InlineData(LicensePermission.ViewDocuments, LicensePermission.ManageDocuments)]
    [InlineData(LicensePermission.ViewVehicles, LicensePermission.ManageVehicles)]
    [InlineData(LicensePermission.ViewPeople, LicensePermission.ManageVehicles)]
    [InlineData(LicensePermission.ViewStructure, LicensePermission.ManageVehicles)]
    public void RemovingReadingPermission_AlsoRemovesDependentManagement(LicensePermission reading, LicensePermission management)
    {
        var permissions = (long)(reading | management);

        var result = LicensePermissionCatalog.RemoveDependents(permissions, reading);

        Assert.Equal(0, result & (long)management);
    }

    [Theory]
    [InlineData(LicensePermission.ViewFinance)]
    [InlineData(LicensePermission.ManageFinance)]
    [InlineData(LicensePermission.ViewDocuments)]
    [InlineData(LicensePermission.ManageDocuments)]
    [InlineData(LicensePermission.ManageAnnouncements)]
    [InlineData(LicensePermission.ViewVehicles)]
    [InlineData(LicensePermission.ManageVehicles)]
    public void ConfigurablePermission_IsVisibleInCatalog(LicensePermission permission) =>
        Assert.Contains(LicensePermissionCatalog.Options, option => option.Permission == permission);

    [Theory]
    [InlineData(0, LicensePermission.ManageVehicles)]
    [InlineData(1, LicensePermission.ManageVehicles)]
    [InlineData(2, LicensePermission.ManageVehicles)]
    [InlineData(3, LicensePermission.ViewVehicles)]
    [InlineData(4, LicensePermission.ViewVehicles)]
    public void DefaultProfiles_IncludeExpectedVehicleAccess(int role, LicensePermission permission)
    {
        var defaults = LicensePermissionCatalog.Defaults(role);

        Assert.Equal((long)permission, defaults & (long)permission);
    }
}
