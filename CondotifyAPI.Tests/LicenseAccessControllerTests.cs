using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CondotifyAPI.Tests;

public sealed class LicenseAccessControllerTests
{
    [Fact]
    public void UpdateModules_HasExpectedRouteVerbAndAuthorization()
    {
        var method = typeof(LicenseAccessController).GetMethod(nameof(LicenseAccessController.UpdateModules));

        Assert.NotNull(method);
        var route = Assert.IsType<HttpPutAttribute>(
            Assert.Single(method!.GetCustomAttributes(typeof(HttpPutAttribute), inherit: true)));
        Assert.Equal("{id:guid}/modules", route.Template);
        Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    private static UserAccessDTO User(Guid enterpriseId, AccessTypeEnum type) => new()
    {
        Id = Guid.NewGuid(),
        EnterpriseId = enterpriseId,
        AccessType = type,
        Email = "user@condotify.local"
    };

    [Theory]
    [InlineData(AccessTypeEnum.Developer, true)]
    [InlineData(AccessTypeEnum.Admin, true)]
    [InlineData(AccessTypeEnum.Manager, false)]
    [InlineData(AccessTypeEnum.Editor, false)]
    [InlineData(AccessTypeEnum.Viewer, false)]
    [InlineData(AccessTypeEnum.Default, false)]
    public void CanManageModules_OnlyDeveloperOrAdminOfSameEnterprise(AccessTypeEnum type, bool expected)
    {
        var enterpriseId = Guid.NewGuid();
        var user = User(enterpriseId, type);

        Assert.Equal(expected, LicenseAccessController.CanManageModules(user, enterpriseId));
    }

    [Fact]
    public void CanManageModules_RejectsDifferentEnterprise()
    {
        var user = User(Guid.NewGuid(), AccessTypeEnum.Admin);

        Assert.False(LicenseAccessController.CanManageModules(user, Guid.NewGuid()));
    }

    [Fact]
    public void CanManageModules_RejectsNullUser()
    {
        Assert.False(LicenseAccessController.CanManageModules(null, Guid.NewGuid()));
    }
}
