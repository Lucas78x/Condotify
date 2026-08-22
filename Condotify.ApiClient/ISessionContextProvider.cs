namespace Condotify.Services;

/// <summary>
/// Fornece os dados da sessao atual usados pelo <see cref="CondotifyApiClient"/>.
/// A web resolve a partir dos claims do cookie de sessao; o aplicativo MAUI
/// resolve a partir do SecureStorage.
/// </summary>
public interface ISessionContextProvider
{
    /// <summary>Token Bearer enviado a API. Null quando nao ha sessao.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Empresa da sessao, exigida ao criar uma licenca. Null quando ausente.</summary>
    ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifica o host de que a API recusou o token atual. A web tenta renovar o
    /// cookie e recria o circuito Blazor; hosts que gerenciam a sessao por conta
    /// propria podem manter a implementacao padrao.
    /// </summary>
    ValueTask HandleUnauthorizedAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
