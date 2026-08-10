using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Hubs;

// Hub dedicado a atualizacoes ao vivo da Portaria (agenda de visitas, eventos de
// acesso, encomendas). Cada conexao entra em um grupo por licenca ("license-{id}")
// somente depois de confirmar que o principal autenticado tem permissao de ver
// eventos daquela licenca -- sem essa checagem, um porteiro autenticado poderia se
// inscrever no grupo de outra licenca e vazar eventos de outro condominio. Ver
// docs/superpowers/plans/2026-08-09-portaria-ux-reform.md, Task 6.
[Authorize]
public sealed class ConciergeHub(ILicenseAuthorizationService licenseAuth) : Hub
{
    public async Task JoinLicenseGroup(Guid licenseId)
    {
        var allowed = await licenseAuth.HasPermissionAsync(Context.User!, licenseId, LicensePermissionEnum.ViewEvents, Context.ConnectionAborted);
        if (!allowed) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(licenseId));
    }

    public static string GroupName(Guid licenseId) => $"license-{licenseId}";
}
