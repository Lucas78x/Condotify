using System.Security.Claims;
using System.Text.Json;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.CFTV;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/cftv")]
[RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
public sealed class CftvStreamingController : ControllerBase
{
    private const int TokenLifetimeSeconds = 120;
    private const int DefaultMaxViewersPerLicense = 24;
    private const int OfflineGraceMinutes = 10;

    private readonly DatabaseContext _context;
    private readonly ICftvStreamPathResolver _paths;
    private readonly IMediaAccessTokenService _tokens;
    private readonly IMediaGatewayClient _gateway;
    private readonly ICftvSnapshotService _snapshots;
    private readonly ICFTVService _cftvService;
    private readonly ILogger<CftvStreamingController> _logger;

    public CftvStreamingController(
        DatabaseContext context,
        ICftvStreamPathResolver paths,
        IMediaAccessTokenService tokens,
        IMediaGatewayClient gateway,
        ICftvSnapshotService snapshots,
        ICFTVService cftvService,
        ILogger<CftvStreamingController> logger)
    {
        _context = context;
        _paths = paths;
        _tokens = tokens;
        _gateway = gateway;
        _snapshots = snapshots;
        _cftvService = cftvService;
        _logger = logger;
    }

    [HttpPost("{deviceId:guid}/sessions")]
    public async Task<IActionResult> OpenSession(
        Guid licenseId,
        Guid deviceId,
        [FromBody] OpenCftvSessionIn input,
        CancellationToken cancellationToken)
    {
        var device = await _context.CFTVDevices
            .AsNoTracking()
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);

        if (device is null) return NotFound();

        if (IsCameraOffline(device.IsActive, device.LastSeenAt, DateTime.UtcNow))
            return StatusCode(StatusCodes.Status409Conflict,
                new { Result = "CameraOffline", Errors = "A camera esta offline no momento. Tente novamente mais tarde." });

        var channel = ResolveChannel(device.DeviceType, input.Channel);
        if (device.DeviceType != CFTVDeviceTypeEnum.Camera && channel > device.MaxChannels)
            return BadRequest(new { Result = "InvalidChannel", Errors = "O canal informado nao existe neste equipamento." });

        var quality = string.Equals(input.Quality, "secondary", StringComparison.OrdinalIgnoreCase)
            ? StreamQuality.Secondary
            : StreamQuality.Main;

        var licensePrefix = $"l{licenseId:N}_";
        if (await _gateway.ActiveViewerCountAsync(licensePrefix, cancellationToken) >= MaxViewersPerLicense)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { Result = "SessionLimitReached", Errors = "Limite de visualizacoes simultaneas atingido. Tente novamente em instantes." });

        // ConnectivityProbePaths sempre devolve pelo menos um caminho (generico por tipo de
        // equipamento), entao esta segunda opcao nunca fica vazia: PreferredPath so retorna
        // null para marca desconhecida.
        var path = _paths.PreferredPath(device.Mark, device.DeviceType, channel, quality)
            ?? _paths.ConnectivityProbePaths(device.Mark, device.DeviceType, channel).First();

        var rtspPort = int.TryParse(new string(device.RTSPPort.Where(char.IsDigit).ToArray()), out var parsed) ? parsed : 554;

        // A credencial e usada aqui e apenas aqui. Nao pode ser registrada,
        // devolvida, nem entrar em mensagem de erro.
        var source = _paths.BuildRtspUrl(device.IpAddress, rtspPort, device.Username, device.Password, path);

        var mediaPath = MediaAccessTokenService.PathFor(licenseId, deviceId, channel, quality);

        if (!await _gateway.EnsurePathAsync(mediaPath, source, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { Result = "GatewayUnavailable", Errors = "O servico de video esta indisponivel. Tente novamente em instantes." });

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser) ? parsedUser : Guid.Empty;
        var expiresAt = DateTime.UtcNow.AddSeconds(TokenLifetimeSeconds);
        var token = _tokens.Issue(new MediaAccessGrant(licenseId, deviceId, channel, userId, expiresAt, quality));

        var protocol = string.Equals(input.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ? "hls" : "webrtc";
        var baseUrl = protocol == "hls"
            ? (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_HLS_BASEURL") ?? "http://localhost:8888").TrimEnd('/')
            : (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_WEBRTC_BASEURL") ?? "http://localhost:8889").TrimEnd('/');
        var playbackUrl = protocol == "hls"
            ? $"{baseUrl}/{mediaPath}/index.m3u8?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/{mediaPath}/whep?token={Uri.EscapeDataString(token)}";

        _logger.LogInformation(
            "Sessao de video aberta. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, usuario {UserId}, origem {Ip}.",
            licenseId, deviceId, channel, userId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");

        return Ok(new CftvSessionOut(Guid.NewGuid(), playbackUrl, token, expiresAt, protocol));
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid licenseId, CancellationToken cancellationToken)
    {
        var devices = await _context.CFTVDevices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new CftvStatusOut(x.Id, x.Name, x.IsActive, x.LastSeenAt, x.HealthMessage, x.MaxChannels))
            .ToListAsync(cancellationToken);

        return Ok(devices);
    }

    [HttpPost("{deviceId:guid}/test")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> TestDeviceConfiguration(
        Guid licenseId,
        Guid deviceId,
        [FromBody] UpdateCftvDeviceIn input,
        CancellationToken cancellationToken)
    {
        var persisted = await _context.CFTVDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (persisted is null) return NotFound();

        var validation = await new UpdateCftvDeviceInValidator().ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new
            {
                Result = "ValidationError",
                Errors = validation.Errors.Select(x => x.ErrorMessage).Distinct().ToArray()
            });

        var diagnostic = await DiagnoseAsync(input, EffectivePassword(input, persisted.Password), cancellationToken);
        return diagnostic.RtspReady
            ? Ok(diagnostic)
            : BadRequest(new { Result = "ConnectionFailed", Errors = diagnostic.Message, Details = diagnostic });
    }

    [HttpPut("{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> UpdateDevice(
        Guid licenseId,
        Guid deviceId,
        [FromBody] UpdateCftvDeviceIn input,
        CancellationToken cancellationToken)
    {
        var device = await _context.CFTVDevices
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (device is null) return NotFound();

        var validation = await new UpdateCftvDeviceInValidator().ValidateAsync(input, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new
            {
                Result = "ValidationError",
                Errors = validation.Errors.Select(x => x.ErrorMessage).Distinct().ToArray()
            });

        var effectivePassword = EffectivePassword(input, device.Password);
        var diagnostic = await DiagnoseAsync(input, effectivePassword, cancellationToken);
        if (!diagnostic.RtspReady)
            return BadRequest(new { Result = "ConnectionFailed", Errors = diagnostic.Message, Details = diagnostic });

        var previousMaxChannels = Math.Max(device.MaxChannels, 1);
        var maxChannels = input.DeviceType == CFTVDeviceTypeEnum.Camera ? 1 : input.MaxChannels;

        device.Name = input.Name.Trim();
        device.IpAddress = input.IpAddress.Trim();
        device.Username = input.UserName.Trim();
        device.Password = effectivePassword;
        device.HTTPPort = input.HTTPPort.Trim();
        device.RTSPPort = input.RTSPPort.Trim();
        device.IpType = input.IpType;
        device.Proportion = input.Proportion;
        device.Mark = input.Mark;
        device.DeviceType = input.DeviceType;
        device.MaxChannels = maxChannels;
        device.ResidentVisible = input.ResidentVisible;
        device.IsActive = true;
        device.HealthMessage = string.Empty;
        device.LastSeenAt = DateTime.UtcNow;

        var wantedChannels = Enumerable.Range(1, maxChannels).ToHashSet();
        foreach (var removed in device.Channels.Where(x => !wantedChannels.Contains(x.ChannelNumber)).ToList())
        {
            device.Channels.Remove(removed);
            _context.Remove(removed);
        }

        foreach (var number in wantedChannels.Where(number => device.Channels.All(x => x.ChannelNumber != number)))
        {
            device.Channels.Add(new CFTVChannelDTO
            {
                Id = Guid.NewGuid(),
                ChannelNumber = number,
                Name = $"Canal {number}",
                IsEnabled = true,
                RtspPath = string.Empty,
                CFTVDeviceId = device.Id
            });
        }

        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            var channelsToClear = Math.Max(previousMaxChannels, maxChannels);
            for (var channel = 1; channel <= channelsToClear; channel++)
            {
                await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Main), cancellationToken);
                await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Secondary), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "A configuração da câmera {DeviceId} foi atualizada, mas o gateway não confirmou a limpeza dos caminhos anteriores.",
                deviceId);
        }

        return NoContent();
    }

    [HttpGet("{deviceId:guid}/snapshot")]
    public async Task<IActionResult> Snapshot(Guid licenseId, Guid deviceId, [FromQuery] int channel = 1, CancellationToken cancellationToken = default)
    {
        var device = await _context.CFTVDevices.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (device is null) return NotFound();

        var snapshot = await _snapshots.FetchAsync(device, channel, cancellationToken);
        return snapshot is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { Result = "SnapshotUnavailable", Errors = "A miniatura da camera esta indisponivel." })
            : File(snapshot.Content, snapshot.ContentType);
    }

    [HttpDelete("{deviceId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> DeleteDevice(
        Guid licenseId,
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        var device = await _context.CFTVDevices
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);
        if (device is null) return NotFound();

        var linkedDevices = await _context.Devices
            .Where(x => x.LicenseId == licenseId && x.LprCameraId == deviceId)
            .ToListAsync(cancellationToken);

        foreach (var linkedDevice in linkedDevices)
        {
            linkedDevice.LprCameraId = null;
            linkedDevice.LprCameraChannel = null;
            linkedDevice.LprDoorChannel = null;
            linkedDevice.LprMode = null;
            linkedDevice.LastUpdatedAt = DateTime.UtcNow;
        }

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser)
            ? parsedUser
            : (Guid?)null;
        _context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            EntityType = "CftvDevice",
            EntityId = device.Id,
            Action = "Deleted",
            Status = "Success",
            Summary = $"Câmera ou gravador {device.Name} removido.",
            DetailsJson = JsonSerializer.Serialize(new
            {
                device.DeviceType,
                device.MaxChannels,
                UnlinkedAccessDevices = linkedDevices.Select(x => new { x.Id, x.Name }).ToList()
            }),
            UserId = userId,
            UserName = User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.Email) ?? "Usuário",
            CreatedAt = DateTime.UtcNow
        });

        _context.CFTVDevices.Remove(device);
        await _context.SaveChangesAsync(cancellationToken);

        var maxChannel = device.DeviceType == CFTVDeviceTypeEnum.Camera ? 1 : Math.Max(device.MaxChannels, 1);
        try
        {
            for (var channel = 1; channel <= maxChannel; channel++)
            {
                await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Main), cancellationToken);
                await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Secondary), cancellationToken);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception,
                "A camera {DeviceId} foi removida, mas o gateway de video nao confirmou o encerramento de todos os caminhos.",
                deviceId);
        }

        _logger.LogInformation(
            "Camera removida. Licenca {LicenseId}, equipamento {DeviceId}, vinculos de acesso removidos {LinkedDeviceCount}.",
            licenseId, deviceId, linkedDevices.Count);
        return NoContent();
    }

    [HttpDelete("{deviceId:guid}/sessions/{channel:int}")]
    public async Task<IActionResult> CloseSession(
        Guid licenseId,
        Guid deviceId,
        int channel,
        CancellationToken cancellationToken)
    {
        if (!await _context.CFTVDevices.AsNoTracking().AnyAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken))
            return NotFound();

        // CloseSession nao sabe qual qualidade estava aberta (a rota nao carrega esse dado),
        // entao remove os dois caminhos possiveis; remover um caminho que nao existe e inocuo.
        await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Main), cancellationToken);
        await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel, StreamQuality.Secondary), cancellationToken);

        _logger.LogInformation(
            "Sessao de video encerrada. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}.",
            licenseId, deviceId, channel);

        return NoContent();
    }

    private static int MaxViewersPerLicense =>
        int.TryParse(Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_MAX_VIEWERS_PER_LICENSE"), out var configured) && configured > 0
            ? configured
            : DefaultMaxViewersPerLicense;

    private async Task<CftvConnectionDiagnosticOut> DiagnoseAsync(
        UpdateCftvDeviceIn input,
        string password,
        CancellationToken cancellationToken)
    {
        var maxChannels = input.DeviceType == CFTVDeviceTypeEnum.Camera ? 1 : input.MaxChannels;
        var channels = Enumerable.Range(1, maxChannels)
            .Select(number => new CFTVChannel
            {
                Id = Guid.NewGuid(),
                ChannelNumber = number,
                Name = $"Canal {number}",
                IsEnabled = true,
                RtspPath = string.Empty
            })
            .ToList();

        var probe = CFTVDevice.Create(
            input.Name.Trim(),
            input.UserName.Trim(),
            password,
            input.IpAddress.Trim(),
            input.HTTPPort.Trim(),
            input.RTSPPort.Trim(),
            input.IpType,
            input.Proportion,
            input.Mark,
            input.DeviceType,
            maxChannels,
            channels,
            input.ResidentVisible);

        var result = await _cftvService.TestAsync(probe, cancellationToken);
        var channelsReady = result.Channels.Count(x => x.RtspOk);
        var ready = channelsReady > 0;
        var message = ready
            ? channelsReady == 1
                ? "Conexão RTSP confirmada. O vídeo está pronto para ser salvo."
                : $"Conexão RTSP confirmada em {channelsReady} canais."
            : !result.TcpRtspOk
                ? "A porta RTSP não respondeu. Confira a porta e se o serviço RTSP está habilitado na câmera."
                : "A porta RTSP respondeu, mas o vídeo não foi autenticado. Confira marca, usuário, senha e canais.";

        return new CftvConnectionDiagnosticOut(result.PingOk, result.TcpRtspOk, ready, channelsReady, message);
    }

    private static string EffectivePassword(UpdateCftvDeviceIn input, string persistedPassword) =>
        string.IsNullOrWhiteSpace(input.Password) ? persistedPassword : input.Password;

    /// <summary>
    /// Camera nao tem canais de verdade: PreferredPath ignora o canal informado para
    /// DeviceType.Camera e sempre resolve a mesma origem RTSP. Sem este clamp, canal:1 e
    /// canal:999999 acabam sendo dois caminhos MediaMTX diferentes para a mesma camera,
    /// deixando um unico usuario autorizado esgotar sozinho o limite de visualizadores.
    /// </summary>
    internal static int ResolveChannel(CFTVDeviceTypeEnum deviceType, int requestedChannel) =>
        deviceType == CFTVDeviceTypeEnum.Camera ? 1 : Math.Max(requestedChannel, 1);

    /// <summary>
    /// Com sourceOnDemand:true o MediaMTX so contata a camera na primeira leitura, entao sem
    /// esta checagem o cliente recebe 200 e um player travado quando a camera esta offline,
    /// com senha errada ou com caminho incompativel para o modelo. IsActive e LastSeenAt vem
    /// do CftvHealthMonitoringWorker; uma folga de 10 minutos evita falso positivo entre duas
    /// passagens do worker.
    /// </summary>
    internal static bool IsCameraOffline(bool isActive, DateTime? lastSeenAt, DateTime utcNow) =>
        !isActive && (lastSeenAt is null || lastSeenAt < utcNow.AddMinutes(-OfflineGraceMinutes));
}
