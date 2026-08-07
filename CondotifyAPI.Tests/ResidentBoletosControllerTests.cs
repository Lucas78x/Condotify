using CondotifyAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResidentBoletosControllerTests
{
    [Fact]
    public void Controller_RequiresTheResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentBoletosController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Resident", authorize.Policy);
    }

    [Theory]
    [InlineData(nameof(ResidentBoletosController.List), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResidentBoletosController.Download), typeof(HttpGetAttribute), "{documentId:guid}/file")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string? route)
    {
        var method = typeof(ResidentBoletosController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }
}
