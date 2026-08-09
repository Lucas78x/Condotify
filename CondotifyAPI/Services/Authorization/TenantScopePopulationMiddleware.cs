using System.Security.Claims;
using CondotifyAPI.Domain.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CondotifyAPI.Services.Authorization;

// Middleware global (registrado em Program.cs logo apos UseAuthentication(), antes de
// UseAuthorization()) que popula o ICurrentTenantAccessor escopado por requisicao.
// Substitui um IAsyncActionFilter anterior (TenantScopeActionFilter) que rodava tarde
// demais no pipeline: filtros de Authorization do MVC (ex.: RequireLicensePermissionAttribute)
// rodam ANTES de Action filters, entao qualquer consulta feita durante a autorizacao via MVC
// via um accessor ainda vazio. Middleware roda antes de QUALQUER filtro do MVC e antes de
// endpoints minimal API -- ver docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md, Task 7.
// Endpoints [AllowAnonymous] continuam sem escopo algum (accessor fica nulo) -- consultas que
// precisam rodar sem um principal autenticado (login de morador, aceitar convite, passe publico)
// usam .IgnoreQueryFilters() explicitamente no proprio controller, pela mesma razao que
// LicenseAuthorizationService usa: elas ESTABELECEM a identidade, nao podem depender de um
// escopo que so existe depois que a identidade e conhecida.
// Workers em background nao passam por aqui -- chamam MarkUnrestricted() diretamente.
public sealed class TenantScopePopulationMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context) =>
        InvokeAsync(
            context,
            context.RequestServices.GetRequiredService<ICurrentTenantAccessor>(),
            context.RequestServices.GetRequiredService<ILicenseAuthorizationService>(),
            context.RequestServices.GetRequiredService<IResidentAuthorizationService>(),
            () => next(context));

    internal async Task InvokeAsync(
        HttpContext context,
        ICurrentTenantAccessor tenant,
        ILicenseAuthorizationService licenseAuth,
        IResidentAuthorizationService residentAuth,
        Func<Task> next)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var principalType = user.FindFirstValue("principal_type");
            if (principalType == "resident")
            {
                var grant = await residentAuth.GetGrantAsync(user, context.RequestAborted);
                tenant.SetAccessibleScope(grant is null ? [] : [grant.LicenseId], null);
            }
            else
            {
                var ids = await licenseAuth.GetAccessibleLicenseIdsAsync(user, context.RequestAborted);
                var enterpriseId = Guid.TryParse(user.FindFirstValue("enterprise_id"), out var eid) ? eid : (Guid?)null;
                tenant.SetAccessibleScope(ids, enterpriseId);
            }
        }

        await next();
    }
}
