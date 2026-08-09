using System.Security.Claims;
using CondotifyAPI.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CondotifyAPI.Services.Authorization;

// Global IAsyncActionFilter (registered in Program.cs) that populates the scoped
// ICurrentTenantAccessor for every authenticated HTTP request, before the controller
// action runs. Without this, the accessor sits null/unpopulated on the HTTP path and every
// tenant-filtered entity query returns zero rows (see docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md).
// Background workers do not go through this filter - they call MarkUnrestricted() directly.
public sealed class TenantScopeActionFilter(
    ICurrentTenantAccessor tenant,
    ILicenseAuthorizationService licenseAuth,
    IResidentAuthorizationService residentAuth) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var principalType = user.FindFirstValue("principal_type");
            if (principalType == "resident")
            {
                var grant = await residentAuth.GetGrantAsync(user, context.HttpContext.RequestAborted);
                tenant.SetAccessibleScope(grant is null ? [] : [grant.LicenseId], null);
            }
            else
            {
                var ids = await licenseAuth.GetAccessibleLicenseIdsAsync(user, context.HttpContext.RequestAborted);
                var enterpriseId = Guid.TryParse(user.FindFirstValue("enterprise_id"), out var eid) ? eid : (Guid?)null;
                tenant.SetAccessibleScope(ids, enterpriseId);
            }
        }

        await next();
    }
}
