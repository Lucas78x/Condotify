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
[Authorize(Policy = "Resident")]
[Route("api/resident/cameras")]
public sealed class ResidentCftvController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    ICftvStreamPathResolver paths,
    IMediaAccessTokenService tokens,
    IMediaGatewayClient gateway,
    ICftvSnapshotService snapshots,
    ILogger<ResidentCftvController> logger) : ControllerBase
{
    private const int TokenLifetimeSeconds = 120;
    private const int DefaultMaxViewersPerLicense = 24;

    [HttpGet]
    public async Task<IActionResult> GetCameras(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var cameras = await context.CFTVDevices.AsNoTracking()
            .Where(x => x.LicenseId == grant.LicenseId && x.ResidentVisible &&
                        x.Channels.Any(channel => channel.IsEnabled && channel.ResidentVisible))
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                DeviceType = x.DeviceType.ToString(),
                x.MaxChannels,
                Online = x.IsActive,
                x.LastSeenAt,
                x.HealthMessage,
                Channels = x.Channels
                    .Where(channel => channel.IsEnabled && channel.ResidentVisible)
                    .OrderBy(channel => channel.ChannelNumber)
                    .Select(channel => new
                    {
                        channel.ChannelNumber,
                        channel.Name
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return Ok(cameras);
    }

    [HttpPost("{deviceId:guid}/sessions")]
    public async Task<IActionResult> OpenSession(
        Guid deviceId,
        [FromBody] OpenCftvSessionIn input,
        CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var device = await context.CFTVDevices.AsNoTracking()
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId &&
                                      x.LicenseId == grant.LicenseId &&
                                      x.ResidentVisible,
                cancellationToken);
        if (device is null) return NotFound();

        if (CftvStreamingController.IsCameraOffline(device.IsActive, device.LastSeenAt, DateTime.UtcNow))
            return Conflict(new { Result = "CameraOffline", Errors = "A camera esta offline no momento. Tente novamente mais tarde." });

        var channel = CftvStreamingController.ResolveChannel(device.DeviceType, input.Channel);
        if (device.DeviceType != CFTVDeviceTypeEnum.Camera && channel > device.MaxChannels)
            return BadRequest(new { Result = "InvalidChannel", Errors = "O canal informado nao existe neste equipamento." });
        if (!IsResidentChannelVisible(device.Channels, channel)) return NotFound();

        var quality = string.Equals(input.Quality, "secondary", StringComparison.OrdinalIgnoreCase)
            ? StreamQuality.Secondary
            : StreamQuality.Main;

        if (await gateway.ActiveViewerCountAsync($"l{grant.LicenseId:N}_", cancellationToken) >= MaxViewersPerLicense)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { Result = "SessionLimitReached", Errors = "Limite de visualizacoes simultaneas atingido. Tente novamente em instantes." });

        var sourcePath = paths.PreferredPath(device.Mark, device.DeviceType, channel, quality)
            ?? paths.ConnectivityProbePaths(device.Mark, device.DeviceType, channel).First();
        var rtspPort = int.TryParse(new string(device.RTSPPort.Where(char.IsDigit).ToArray()), out var parsedPort) ? parsedPort : 554;
        var source = paths.BuildRtspUrl(device.IpAddress, rtspPort, device.Username, device.Password, sourcePath);
        var mediaPath = MediaAccessTokenService.PathFor(grant.LicenseId, deviceId, channel, quality);

        if (!await gateway.EnsurePathAsync(mediaPath, source, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { Result = "GatewayUnavailable", Errors = "O servico de video esta indisponivel. Tente novamente em instantes." });

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser) ? parsedUser : grant.ResidentId;
        var expiresAt = DateTime.UtcNow.AddSeconds(TokenLifetimeSeconds);
        var token = tokens.Issue(new MediaAccessGrant(grant.LicenseId, deviceId, channel, userId, expiresAt, quality));
        var protocol = string.Equals(input.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ? "hls" : "webrtc";
        var baseUrl = protocol == "hls"
            ? (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_HLS_BASEURL") ?? "http://localhost:8888").TrimEnd('/')
            : (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_WEBRTC_BASEURL") ?? "http://localhost:8889").TrimEnd('/');
        var playbackUrl = protocol == "hls"
            ? $"{baseUrl}/{mediaPath}/index.m3u8?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/{mediaPath}/whep?token={Uri.EscapeDataString(token)}";

        logger.LogInformation(
            "Sessao de video do morador aberta. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, morador {ResidentId}.",
            grant.LicenseId, deviceId, channel, grant.ResidentId);

        return Ok(new CftvSessionOut(Guid.NewGuid(), playbackUrl, token, expiresAt, protocol));
    }

    [HttpGet("{deviceId:guid}/snapshot")]
    public async Task<IActionResult> Snapshot(Guid deviceId, [FromQuery] int channel = 1, CancellationToken cancellationToken = default)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var device = await context.CFTVDevices.AsNoTracking()
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == grant.LicenseId && x.ResidentVisible, cancellationToken);
        if (device is null) return NotFound();

        channel = CftvStreamingController.ResolveChannel(device.DeviceType, channel);
        if (device.DeviceType != CFTVDeviceTypeEnum.Camera && channel > device.MaxChannels)
            return NotFound();
        if (!IsResidentChannelVisible(device.Channels, channel)) return NotFound();

        var snapshot = await snapshots.FetchAsync(device, channel, cancellationToken);
        return snapshot is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, new { Result = "SnapshotUnavailable", Errors = "A miniatura da camera esta indisponivel." })
            : File(snapshot.Content, snapshot.ContentType);
    }

    [HttpDelete("{deviceId:guid}/sessions/{channel:int}")]
    public async Task<IActionResult> CloseSession(Guid deviceId, int channel, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var device = await context.CFTVDevices.AsNoTracking()
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == grant.LicenseId && x.ResidentVisible, cancellationToken);
        if (device is null) return NotFound();

        channel = CftvStreamingController.ResolveChannel(device.DeviceType, channel);
        if (!IsResidentChannelVisible(device.Channels, channel)) return NotFound();
        await gateway.RemovePathAsync(MediaAccessTokenService.PathFor(grant.LicenseId, deviceId, channel, StreamQuality.Main), cancellationToken);
        await gateway.RemovePathAsync(MediaAccessTokenService.PathFor(grant.LicenseId, deviceId, channel, StreamQuality.Secondary), cancellationToken);
        return NoContent();
    }

    private static int MaxViewersPerLicense =>
        int.TryParse(Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_MAX_VIEWERS_PER_LICENSE"), out var configured) && configured > 0
            ? configured
            : DefaultMaxViewersPerLicense;

    internal static bool IsResidentChannelVisible(
        IEnumerable<CondotifyAPI.Domain.DTO.Equipments.CFTVChannelDTO> channels,
        int channel) =>
        channels.Any(item => item.ChannelNumber == channel && item.IsEnabled && item.ResidentVisible);
}
