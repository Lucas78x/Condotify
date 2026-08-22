using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Condotify.Services;

/// <summary>
/// Le os dados da sessao dos claims gravados no cookie pelo LoginController.
/// </summary>
public sealed class ClaimsSessionContextProvider : ISessionContextProvider
{
    /// <summary>
    /// Nome do claim que guarda o token. Este valor ja existe nos cookies
    /// emitidos em producao: alteracao invalida as sessoes ativas.
    /// </summary>
    public const string AccessTokenClaim = "condotify_access_token";

    /// <summary>
    /// Refresh token mantido apenas no ticket de autenticacao protegido pelo host web.
    /// Ele nunca e devolvido ao JavaScript nem enviado pelo cliente de API comum.
    /// </summary>
    public const string RefreshTokenClaim = "condotify_refresh_token";

    /// <summary>Instante Unix em que o access token deixa de ser valido.</summary>
    public const string AccessTokenExpiresAtClaim = "condotify_access_token_expires_at";

    /// <summary>
    /// Nome do claim que guarda a empresa. Emitido por LoginController a
    /// partir do payload do JWT.
    /// </summary>
    public const string EnterpriseIdClaim = "enterprise_id";

    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly IJSRuntime? _jsRuntime;

    public ClaimsSessionContextProvider(AuthenticationStateProvider authenticationStateProvider, IJSRuntime? jsRuntime = null)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _jsRuntime = jsRuntime;
    }

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        ReadClaimAsync(AccessTokenClaim);

    public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) =>
        ReadClaimAsync(EnterpriseIdClaim);

    public async ValueTask HandleUnauthorizedAsync(CancellationToken cancellationToken = default)
    {
        if (_jsRuntime is null) return;

        try
        {
            await _jsRuntime.InvokeVoidAsync("condotifySession.handleUnauthorized", cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or TaskCanceledException)
        {
            // O reload encerra o circuito enquanto a chamada JS ainda pode estar
            // em voo. Durante prerenderizacao tambem nao existe runtime interativo.
        }
    }

    private async ValueTask<string?> ReadClaimAsync(string claimType)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirst(claimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
