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
}
