using System.Net.Sockets;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.CFTV;

/// <summary>
/// Verifica periodicamente se cada camera responde, para que o aplicativo
/// possa mostrar online/offline sem esperar o timeout de um stream.
/// Segue o mesmo formato de <see cref="AccessControl.DeviceHealthMonitoringWorker"/>:
/// intervalo configuravel via <see cref="IConfiguration"/>, uma checagem
/// imediata seguida de um <see cref="PeriodicTimer"/>, e gravacao apenas
/// quando algo realmente mudou.
/// </summary>
public sealed class CftvHealthMonitoringWorker(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<CftvHealthMonitoringWorker> logger) : BackgroundService
{
    internal const string OfflineMessage = "Sem resposta na porta RTSP.";
    internal const int DefaultRtspPort = 554;
    private const int ProbeTimeoutMs = 1500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("CftvMonitoring:IntervalSeconds", 120), 15, 3600));

        await MonitorAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await MonitorAsync(stoppingToken);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();

            // A verificacao de saude nao usa credenciais. Projetar somente os
            // campos necessarios evita que uma senha antiga, criptografada com
            // outra chave, interrompa a verificacao de todas as demais cameras.
            var devices = await context.CFTVDevices
                .AsNoTracking()
                .Select(device => new
                {
                    device.Id,
                    device.IpAddress,
                    device.RTSPPort,
                    device.IsActive,
                    device.HealthMessage
                })
                .ToListAsync(cancellationToken);
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var port = ParsePort(device.RTSPPort, DefaultRtspPort);
                var reachable = await TcpReachableAsync(device.IpAddress, port, ProbeTimeoutMs, cancellationToken);

                var healthMessage = DescribeHealth(reachable);
                var now = DateTime.UtcNow;
                if (device.IsActive != reachable ||
                    !string.Equals(device.HealthMessage, healthMessage, StringComparison.Ordinal) ||
                    reachable)
                {
                    await context.CFTVDevices
                        .Where(candidate => candidate.Id == device.Id)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(candidate => candidate.IsActive, reachable)
                            .SetProperty(candidate => candidate.HealthMessage, healthMessage)
                            .SetProperty(candidate => candidate.LastSeenAt, candidate => reachable ? now : candidate.LastSeenAt),
                            cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha ao verificar a saude das cameras.");
        }
    }

    /// <summary>
    /// Extrai a porta numerica de um campo de porta livre (ex.: "554" ou "554/tcp").
    /// Nunca lanca; cai no valor padrao informado quando nao ha digitos validos.
    /// </summary>
    internal static int ParsePort(string? rawPort, int fallback)
    {
        var digits = new string((rawPort ?? string.Empty).Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) && parsed is > 0 and <= 65535 ? parsed : fallback;
    }

    /// <summary>Mensagem de saude correspondente ao resultado da checagem TCP. Nunca inclui IP, porta ou credencial.</summary>
    internal static string DescribeHealth(bool reachable) => reachable ? string.Empty : OfflineMessage;

    private static async Task<bool> TcpReachableAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
