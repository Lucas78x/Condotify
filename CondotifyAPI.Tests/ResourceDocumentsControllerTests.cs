using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResourceDocumentsControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var authorize = typeof(ResourceDocumentsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.Single(authorize);
    }

    [Theory]
    [InlineData(nameof(ResourceDocumentsController.Upload), typeof(HttpPostAttribute), null, LicensePermissionEnum.ManageDocuments)]
    [InlineData(nameof(ResourceDocumentsController.List), typeof(HttpGetAttribute), null, LicensePermissionEnum.ViewDocuments)]
    [InlineData(nameof(ResourceDocumentsController.GetFile), typeof(HttpGetAttribute), "{documentId:guid}/file", LicensePermissionEnum.ViewDocuments)]
    [InlineData(nameof(ResourceDocumentsController.Delete), typeof(HttpDeleteAttribute), "{documentId:guid}", LicensePermissionEnum.ManageDocuments)]
    public void Actions_UseExpectedRouteVerbAndPermission(string actionName, Type httpAttributeType, string? route, LicensePermissionEnum expectedPermission)
    {
        var method = typeof(ResourceDocumentsController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);

        var permission = Assert.Single(method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(expectedPermission, Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }
}
