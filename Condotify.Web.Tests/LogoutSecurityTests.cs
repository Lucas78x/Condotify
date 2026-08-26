using System.Reflection;
using Condotify.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Condotify.Web.Tests;

public sealed class LogoutSecurityTests
{
    [Fact]
    public void Logout_IsPostOnlyAndRequiresAntiforgery()
    {
        var action = typeof(LoginController).GetMethod(nameof(LoginController.Logout))!;

        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.Null(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }
}
