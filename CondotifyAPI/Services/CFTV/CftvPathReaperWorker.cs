using System.Text.RegularExpressions;

namespace CondotifyAPI.Services.CFTV;

/// <summary>
/// EnsurePathAsync registra caminhos em /v3/config/paths/add, que persiste na
/// configuracao do MediaMTX pelo tempo de vida do processo -- a unica remocao
/// explicita e CloseSession, que um cliente movel que trava ou vai para segundo
/// plano nunca chama. Sem isto, cada combinacao licenca/equipamento/canal/qualidade
/// ja aberta uma vez fica registrada para sempre (com a senha da camera em
/// texto claro na configuracao do gateway) ate o contentor reiniciar.
///
/// Este worker le periodicamente o estado de todos os caminhos e remove os que
/// (a) tem o formato de nome que o Condotify registra e (b) nunca tiveram uma
/// leitura de fato: nem estao prontos (ready) nem tem leitor algum. Segue o
/// mesmo formato de <see cref="CftvHealthMonitoringWorker"/>: intervalo
/// configuravel, uma passada imediata seguida de um <see cref="PeriodicTimer"/>.
/// </summary>
public sealed partial class CftvPathReaperWorker(
    IMediaGatewayClient gateway,
    IConfiguration configuration,
    ILogger<CftvPathReaperWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("CftvPathReaper:IntervalSeconds", 300), 60, 3600));

        await ReapAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReapAsync(stoppingToken);
    }

    private async Task ReapAsync(CancellationToken cancellationToken)
    {
        try
        {
            var paths = await gateway.ListPathsAsync(cancellationToken);
            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsOrphaned(path)) continue;

                await gateway.RemovePathAsync(path.Name, cancellationToken);

                // O nome do caminho nunca inclui a fonte (source); e seguro registrar.
                logger.LogInformation("Caminho orfao removido do gateway de midia: {Path}.", path.Name);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao limpar caminhos orfaos do gateway de midia.");
        }
    }

    /// <summary>
    /// Um caminho e considerado orfao quando nunca foi de fato assistido (nem
    /// ready nem com leitor algum) e o nome segue o padrao que o proprio
    /// Condotify registra -- nunca remove um caminho arbitrario de terceiros.
    /// </summary>
    internal static bool IsOrphaned(GatewayPathState path) =>
        !path.Ready && path.ReaderCount == 0 && MatchesCondotifyNaming(path.Name);

    /// <summary>l{licenseId:N}_d{deviceId:N}_c{channel}_{m|s} -- ver MediaAccessTokenService.PathFor.</summary>
    internal static bool MatchesCondotifyNaming(string name) => CondotifyPathNameRegex().IsMatch(name);

    [GeneratedRegex(@"^l[0-9a-f]{32}_d[0-9a-f]{32}_c\d+_[ms]$")]
    private static partial Regex CondotifyPathNameRegex();
}
