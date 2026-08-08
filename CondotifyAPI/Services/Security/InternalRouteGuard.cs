namespace CondotifyAPI.Services.Security;

/// <summary>
/// As rotas api/internal/* existem para comunicacao servidor-a-servidor: o
/// callback de autorizacao do MediaMTX (que nao envia cabecalhos
/// customizados) e o bootstrap de empresas/usuarios de staff (que nao pode
/// exigir JWT porque ainda nao existe conta para autenticar). A protecao e
/// de rede: a API escuta em duas portas e apenas a publica sai do contentor.
/// </summary>
public static class InternalRouteGuard
{
    private const string InternalPrefix = "/api/internal/";

    public static bool IsAllowed(string path, int localPort, int internalPort)
    {
        if (localPort == internalPort) return true;

        var normalized = path.EndsWith('/') ? path : path + "/";
        return !normalized.StartsWith(InternalPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
