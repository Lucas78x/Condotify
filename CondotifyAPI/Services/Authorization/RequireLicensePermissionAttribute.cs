using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CondotifyAPI.Services.Authorization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireLicensePermissionAttribute : TypeFilterAttribute
{
    public RequireLicensePermissionAttribute(LicensePermissionEnum permission) : base(typeof(LicensePermissionFilter))
    {
        Arguments = [permission];
    }
}

public sealed class LicensePermissionFilter : IAsyncAuthorizationFilter
{
    private readonly LicensePermissionEnum _permission;
    private readonly ILicenseAuthorizationService _authorization;

    public LicensePermissionFilter(LicensePermissionEnum permission, ILicenseAuthorizationService authorization)
    {
        _permission = permission;
        _authorization = authorization;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.RouteData.Values.TryGetValue("licenseId", out var value) || !Guid.TryParse(value?.ToString(), out var licenseId))
        {
            context.Result = new BadRequestObjectResult(new { Errors = "A licenca nao foi informada na rota." });
            return;
        }

        if (!await _authorization.HasPermissionAsync(context.HttpContext.User, licenseId, _permission, context.HttpContext.RequestAborted))
            context.Result = new ForbidResult();
    }
}
