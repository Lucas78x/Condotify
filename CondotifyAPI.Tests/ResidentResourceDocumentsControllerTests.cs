using CondotifyAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResidentResourceDocumentsControllerTests
{
    [Fact]
    public void Controller_RequiresTheResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentResourceDocumentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Resident", authorize.Policy);
        Assert.Empty(typeof(ResidentResourceDocumentsController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(ResidentResourceDocumentsController.List), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResidentResourceDocumentsController.Download), typeof(HttpGetAttribute), "{documentId:guid}/file")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string? route)
    {
        var method = typeof(ResidentResourceDocumentsController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }
}
