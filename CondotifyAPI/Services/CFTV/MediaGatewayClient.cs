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
    private readonly bool _normalizeAudio;
    private readonly string _internalSecret;

    public MediaGatewayClient(HttpClient http, ILogger<MediaGatewayClient> logger)
        : this(
            http,
            logger,
            string.Equals(Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_TRANSCODE_AUDIO"), "true", StringComparison.OrdinalIgnoreCase),
            Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET") ?? string.Empty)
    {
    }

    internal MediaGatewayClient(HttpClient http, ILogger<MediaGatewayClient> logger, bool normalizeAudio, string internalSecret)
    {
        _http = http;
        _logger = logger;
        _normalizeAudio = normalizeAudio;
        _internalSecret = internalSecret;
    }

    public async Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default)
    {
        try
        {
            var configuration = PathConfiguration(path, rtspSource);
            using var response = await _http.PostAsJsonAsync(
                $"/v3/config/paths/add/{path}",
                configuration,
                cancellationToken);

            if (response.IsSuccessStatusCode) return true;

            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);

                // Abrir WebRTC e, em seguida, tentar o fallback HLS registra o mesmo caminho
                // duas vezes. Remover e recriar o caminho existente encerra a sessao ativa no
                // MediaMTX. A edicao da camera ja invalida explicitamente os caminhos, portanto
                // reutilizar o registro aqui e seguro e preserva os espectadores conectados.
                if (detail.Contains("path already exists", StringComparison.OrdinalIgnoreCase))
                    return true;

                _logger.LogWarning(
                    "O gateway de midia recusou a configuracao do caminho {Path}. Status {Status}.",
                    path, (int)response.StatusCode);
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

    private object PathConfiguration(string path, string rtspSource)
    {
        if (!_normalizeAudio || string.IsNullOrWhiteSpace(_internalSecret))
            return new { source = rtspSource, sourceOnDemand = true };

        var publishToken = Uri.EscapeDataString(_internalSecret);
        var command = string.Join(' ',
            "ffmpeg -nostdin -hide_banner -loglevel warning",
            "-fflags +genpts+discardcorrupt -use_wallclock_as_timestamps 1",
            "-rtsp_transport tcp",
            $"-i '{rtspSource}'",
            "-map 0:v:0 -map 0:a:0?",
            "-c:v copy",
            "-af aresample=async=1000:first_pts=0,asetpts=N/SR/TB",
            "-c:a libopus -ar 48000 -ac 1 -b:a 64k -application lowdelay",
            "-avoid_negative_ts make_zero",
            "-f rtsp -rtsp_transport tcp",
            $"'rtsp://127.0.0.1:8554/{path}?internal={publishToken}'");

        return new
        {
            source = "publisher",
            runOnDemand = command,
            runOnDemandRestart = true,
            runOnDemandStartTimeout = "20s",
            runOnDemandCloseAfter = "10s"
        };
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
