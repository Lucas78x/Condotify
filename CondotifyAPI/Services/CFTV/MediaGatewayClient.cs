using System.Net.Http.Json;
using System.Text.Json;

namespace CondotifyAPI.Services.CFTV;

public interface IMediaGatewayClient
{
    Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default);
    Task RemovePathAsync(string path, CancellationToken cancellationToken = default);
    Task<int> ActivePathCountAsync(CancellationToken cancellationToken = default);
}

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

            // O gateway devolve 400 quando o caminho ja existe. Para o nosso
            // fluxo isso e sucesso: o caminho esta pronto para leitura.
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) return true;

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

    public async Task<int> ActivePathCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("/v3/paths/list", cancellationToken);
            if (!response.IsSuccessStatusCode) return 0;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("itemCount", out var count) ? count.GetInt32() : 0;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao consultar os caminhos ativos do gateway de midia.");
            return 0;
        }
    }
}
