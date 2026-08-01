using System.Security.Claims;
using CondotifyAPI.Data.Equipments;
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
    private const int MaxConcurrentPaths = 24;

    private readonly DatabaseContext _context;
    private readonly ICftvStreamPathResolver _paths;
    private readonly IMediaAccessTokenService _tokens;
    private readonly IMediaGatewayClient _gateway;
    private readonly ILogger<CftvStreamingController> _logger;

    public CftvStreamingController(
        DatabaseContext context,
        ICftvStreamPathResolver paths,
        IMediaAccessTokenService tokens,
        IMediaGatewayClient gateway,
        ILogger<CftvStreamingController> logger)
    {
        _context = context;
        _paths = paths;
        _tokens = tokens;
        _gateway = gateway;
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

        var channel = input.Channel < 1 ? 1 : input.Channel;
        if (device.DeviceType != CFTVDeviceTypeEnum.Camera && channel > device.MaxChannels)
            return BadRequest(new { Result = "InvalidChannel", Errors = "O canal informado nao existe neste equipamento." });

        if (await _gateway.ActivePathCountAsync(cancellationToken) >= MaxConcurrentPaths)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { Result = "SessionLimitReached", Errors = "Limite de visualizacoes simultaneas atingido. Tente novamente em instantes." });

        var quality = string.Equals(input.Quality, "secondary", StringComparison.OrdinalIgnoreCase)
            ? StreamQuality.Secondary
            : StreamQuality.Main;

        var path = _paths.PreferredPath(device.Mark, device.DeviceType, channel, quality)
            ?? _paths.ConnectivityProbePaths(device.Mark, device.DeviceType, channel).FirstOrDefault();

        if (path is null)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { Result = "UnsupportedDevice", Errors = "Este modelo ainda nao possui caminho de video conhecido." });

        var rtspPort = int.TryParse(new string(device.RTSPPort.Where(char.IsDigit).ToArray()), out var parsed) ? parsed : 554;

        // A credencial e usada aqui e apenas aqui. Nao pode ser registrada,
        // devolvida, nem entrar em mensagem de erro.
        var source = _paths.BuildRtspUrl(device.IpAddress, rtspPort, device.Username, device.Password, path);

        var mediaPath = MediaAccessTokenService.PathFor(licenseId, deviceId, channel);

        if (!await _gateway.EnsurePathAsync(mediaPath, source, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { Result = "GatewayUnavailable", Errors = "O servico de video esta indisponivel. Tente novamente em instantes." });

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser) ? parsedUser : Guid.Empty;
        var expiresAt = DateTime.UtcNow.AddSeconds(TokenLifetimeSeconds);
        var token = _tokens.Issue(new MediaAccessGrant(licenseId, deviceId, channel, userId, expiresAt));

        var protocol = string.Equals(input.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ? "hls" : "webrtc";
        var baseUrl = (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_PLAYBACK_BASEURL") ?? "http://localhost:8889").TrimEnd('/');
        var playbackUrl = protocol == "hls"
            ? $"{baseUrl}/{mediaPath}/index.m3u8?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/{mediaPath}/whep?token={Uri.EscapeDataString(token)}";

        _logger.LogInformation(
            "Sessao de video aberta. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, usuario {UserId}, origem {Ip}.",
            licenseId, deviceId, channel, userId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");

        return Ok(new CftvSessionOut(Guid.NewGuid(), playbackUrl, token, expiresAt, protocol));
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

        await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel), cancellationToken);

        _logger.LogInformation(
            "Sessao de video encerrada. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}.",
            licenseId, deviceId, channel);

        return NoContent();
    }
}
