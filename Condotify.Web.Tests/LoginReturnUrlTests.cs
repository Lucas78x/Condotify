using Condotify.Controllers;

namespace Condotify.Web.Tests;

public sealed class LoginReturnUrlTests
{
    [Theory]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("https://example.com/steal", "/")]
    [InlineData("//example.com/steal", "/")]
    [InlineData("/\\example.com/steal", "/")]
    [InlineData("/", "/")]
    [InlineData("/operacoes?tab=offline#fila", "/operacoes?tab=offline#fila")]
    public void ResolveLocalReturnUrl_OnlyAcceptsLocalAbsolutePaths(string? value, string expected)
    {
        Assert.Equal(expected, LoginController.ResolveLocalReturnUrl(value));
    }
}
