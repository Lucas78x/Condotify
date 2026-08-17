using System.Security.Claims;
using CondotifyAPI.Jwt;

namespace CondotifyAPI.Services.Security;

public sealed class FirstAccessPasswordChangeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (MustChangePassword(context.User) && !IsAuthenticationEndpoint(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                Result = "PasswordChangeRequired",
                Errors = "Altere a senha temporária antes de continuar."
            });
            return;
        }

        await next(context);
    }

    internal static bool MustChangePassword(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true &&
        user.FindFirstValue(PrincipalTypes.Claim) == PrincipalTypes.User &&
        string.Equals(user.FindFirstValue("first_access"), "true", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAuthenticationEndpoint(PathString path) =>
        path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);
}
