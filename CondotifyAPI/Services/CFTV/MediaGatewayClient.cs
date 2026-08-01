using System.Net.Http.Json;
using System.Text.Json;

namespace CondotifyAPI.Services.CFTV;

public interface IMediaGatewayClient
{
    Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default);
    Task RemovePathAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Numero de caminhos com pelo menos um leitor ativo cujo nome comeca por
    /// <paramref name="licensePrefix"/> (ex.: "l{licenseId:N}_"). Ao contrario de um
    /// contador global, isto torna o limite de sessoes simultaneas por licenca, nao
    /// compartilhado entre todas as licencas da instalacao.
    /// </summary>
    Task<int> ActiveViewerCountAsync(string licensePrefix, CancellationToken cancellationToken = default);

    /// <summary>Estado de todos os caminhos conhecidos pelo gateway, para o CftvPathReaperWorker.</summary>
    Task<IReadOnlyList<GatewayPathState>> ListPathsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Estado minimo de um caminho do MediaMTX. Nunca inclui a fonte (source): ela carrega a credencial da camera.</summary>
public sealed record GatewayPathState(string Name, bool Ready, int ReaderCount);

/// <summary>
/// Cliente da Control API do MediaMTX. Esta API NAO tem autenticacao quando
/// authMethod e http, entao o endereco configurado precisa ser alcancavel
/// apenas pela rede interna. Nunca registre o rtspSource: ele contem a
/// credencial da camera.
/// </summary>
public sealed class MediaGatewayClient : IMediaGatewayClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MediaGatewayClient> _logger;

    public MediaGatewayClient(HttpClient http, ILogger<MediaGatewayClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"/v3/config/paths/add/{path}",
                new { source = rtspSource, sourceOnDemand = true },
                cancellationToken);

            if (response.IsSuccessStatusCode) return true;

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                // O gateway devolve 400 tanto quando o caminho ja existe quanto quando a
                // configuracao e invalida. Aceitar isso como sucesso sem mais nada faria uma
                // fonte rotacionada (ex.: senha trocada) nunca se propagar: o caminho antigo
                // continuaria registrado para sempre. Em vez disso apagamos e tentamos de
                // novo; se ainda existia, o registro novo assume a fonte atual, e se a
                // configuracao era mesmo invalida, a segunda tentativa falha e devolvemos false.
                using var deleteResponse = await _http.DeleteAsync($"/v3/config/paths/delete/{path}", cancellationToken);

                using var retryResponse = await _http.PostAsJsonAsync(
                    $"/v3/config/paths/add/{path}",
                    new { source = rtspSource, sourceOnDemand = true },
                    cancellationToken);

                if (retryResponse.IsSuccessStatusCode) return true;

                _logger.LogWarning(
                    "O gateway de midia recusou o registro do caminho {Path} mesmo apos nova tentativa. Status {Status}.",
                    path, (int)retryResponse.StatusCode);
                return false;
            }

            _logger.LogWarning(
                "O gateway de midia recusou o registro do caminho {Path}. Status {Status}.",
                path, (int)response.StatusCode);
            return false;
        }
        catch (Exception exception)
        {
            // A mensagem nunca inclui rtspSource.
            _logger.LogError(exception, "Falha ao comunicar com o gateway de midia ao registrar {Path}.", path);
            return false;
        }
    }

    public async Task RemovePathAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            // Verificado em 2026-08-01 contra a imagem 1.9.3: o endpoint so
            // aceita o metodo DELETE. Um POST devolve 404 (rota inexistente),
            // entao usar POST aqui deixaria o caminho nunca removido.
            using var response = await _http.DeleteAsync($"/v3/config/paths/delete/{path}", cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogDebug("O caminho {Path} nao pode ser removido. Status {Status}.", path, (int)response.StatusCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao remover o caminho {Path} do gateway de midia.", path);
        }
    }

    public async Task<int> ActiveViewerCountAsync(string licensePrefix, CancellationToken cancellationToken = default)
    {
        try
        {
            var count = 0;
            foreach (var item in await FetchPathItemsAsync(cancellationToken))
            {
                if (!item.Name.StartsWith(licensePrefix, StringComparison.Ordinal)) continue;
                if (item.ReaderCount > 0) count++;
            }

            return count;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao consultar os visualizadores ativos do gateway de midia.");
            return 0;
        }
    }

    public async Task<IReadOnlyList<GatewayPathState>> ListPathsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await FetchPathItemsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao listar os caminhos do gateway de midia.");
            return [];
        }
    }

    /// <summary>
    /// Le /v3/paths/list uma unica vez. Este endpoint devolve o estado de TODOS os
    /// caminhos conhecidos -- inclusive os registrados via config/paths/add que
    /// ainda nao tiveram nenhuma leitura -- com "ready" e "readers" refletindo o
    /// estado de execucao. Nunca le o campo "source": ele carrega a credencial.
    /// </summary>
    private async Task<IReadOnlyList<GatewayPathState>> FetchPathItemsAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync("/v3/paths/list", cancellationToken);
        if (!response.IsSuccessStatusCode) return [];

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<GatewayPathState>();
        foreach (var item in items.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            if (name.Length == 0) continue;

            var ready = item.TryGetProperty("ready", out var readyProp) && readyProp.ValueKind == JsonValueKind.True;
            var readerCount = item.TryGetProperty("readers", out var readersProp) && readersProp.ValueKind == JsonValueKind.Array
                ? readersProp.GetArrayLength()
                : 0;

            result.Add(new GatewayPathState(name, ready, readerCount));
        }

        return result;
    }
}
