using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class EndpointAuthorizationContractTests
{
    [Fact]
    public void EveryControllerAction_DeclaresAuthorizationOrAnonymousAccess()
    {
        var assembly = typeof(CondotifyAPI.Controllers.AccessRoutesController).Assembly;
        var unclassified = assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(true).Any())
                .Where(method => !HasSecurityMetadata(type, method))
                .Select(method => $"{type.FullName}.{method.Name}"))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(unclassified.Length == 0,
            $"Actions sem [Authorize] ou [AllowAnonymous]: {string.Join(", ", unclassified)}");
    }

    private static bool HasSecurityMetadata(Type controller, MethodInfo action) =>
        controller.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any()
        || action.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>().Any()
        || controller.IsDefined(typeof(AllowAnonymousAttribute), inherit: true)
        || action.IsDefined(typeof(AllowAnonymousAttribute), inherit: true);
}
