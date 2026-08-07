using System.Reflection;
using CondotifyAPI.Controllers;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class BoletosControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var authorize = typeof(BoletosController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.Single(authorize);
    }

    [Theory]
    [InlineData(nameof(BoletosController.UploadBatch), typeof(HttpPostAttribute), "batches")]
    [InlineData(nameof(BoletosController.ListBatches), typeof(HttpGetAttribute), "batches")]
    [InlineData(nameof(BoletosController.GetBatch), typeof(HttpGetAttribute), "batches/{batchId:guid}")]
    [InlineData(nameof(BoletosController.GetDocumentFile), typeof(HttpGetAttribute), "documents/{documentId:guid}/file")]
    [InlineData(nameof(BoletosController.UpdateDocument), typeof(HttpPutAttribute), "documents/{documentId:guid}")]
    [InlineData(nameof(BoletosController.Publish), typeof(HttpPostAttribute), "batches/{batchId:guid}/publish")]
    [InlineData(nameof(BoletosController.Cancel), typeof(HttpPostAttribute), "batches/{batchId:guid}/cancel")]
    [InlineData(nameof(BoletosController.DeleteDocument), typeof(HttpDeleteAttribute), "documents/{documentId:guid}")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string route)
    {
        var method = typeof(BoletosController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);

        var permission = Assert.Single(method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(LicensePermissionEnum.ManageFinance, Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }
}
