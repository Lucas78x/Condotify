using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.CFTV;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/access/cftv")]
public class CftvAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ICFTVService _cftvService;
    private readonly ILicenseAuthorizationService _authorization;
    private readonly string? _apiKey;

    public CftvAccessController(ISender sender, ICFTVService cftvService, ILicenseAuthorizationService authorization)
    {
        _sender = sender;
        _cftvService = cftvService;
        _authorization = authorization;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost("by-license")]
    public async Task<IActionResult> CreateByLicense(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateCftvDeviceByLicenseIn cftv)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey)) return Unauthorized();

        if (!Guid.TryParse(cftv.LicenseId, out var licenseId))
            return BadRequest(new { Result = "InvalidRequest", Errors = "A licenca informada e invalida." });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var command = cftv.ToCommand();
        var validator = await new CreateCftvDeviceByLicenseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new { Result = "InvalidRequest", Errors = validator.Errors.Select(e => e.ErrorMessage) });

        var result = await _sender.Send(command);
        if (result is null)
        {
            return Conflict(new CreateCftvDeviceByLicenseOut
            {
                Result = CreateAccessControlDeviceResult.PersistenceFailed,
                Errors = "Não foi possível persistir a câmera ou o gravador."
            });
        }

        return Created("", new CreateCftvDeviceByLicenseOut
        {
            Result = CreateAccessControlDeviceResult.Created,
            Device = new CftvDeviceResponse { Id = result.Id, Name = result.Name }
        });
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestCftvConnection(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] TestCftvConnectionIn cftv)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        if (!Guid.TryParse(cftv.LicenseId, out var licenseId))
            return BadRequest(new { Result = "ValidationError", Errors = new[] { "Licenca invalida." } });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var validator = new TestCftvConnectionInValidator();
        var validation = validator.Validate(cftv);

        if (!validation.IsValid)
        {
            return BadRequest(new
            {
                Result = "ValidationError",
                Errors = validation.Errors.Select(e => e.ErrorMessage)
            });
        }

        var device = CFTVDevice.Create(
                        string.Empty,
                        cftv.UserName,
                        cftv.Password,
                        cftv.IpAddress,
                        string.IsNullOrWhiteSpace(cftv.HTTPPort) ? "80" : cftv.HTTPPort,
                        string.IsNullOrWhiteSpace(cftv.RTSPPort) ? "554" : cftv.RTSPPort,
                        cftv.IpType,
                        ScreenProportionEnum.Widescreen,
                        cftv.Mark,
                        cftv.DeviceType,
                        32,
                        []
                    );


        if (device.DeviceType != CFTVDeviceTypeEnum.Camera)
        {
            device.AddChannels(cftv.Channels);
        }

        var res = await _cftvService.TestAsync(device);

        var anyChannelOk = res.Channels.Any(c => c.RtspOk);

        if (!anyChannelOk)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = device.DeviceType == CFTVDeviceTypeEnum.Camera
                    ? "Não foi possível conectar à câmera via RTSP."
                    : "Não foi possível conectar a nenhum canal do gravador via RTSP.",

                Details = new TestCftvConnectionOut
                {
                    PingOk = res.PingOk,
                    TcpRtspOk = res.TcpRtspOk,
                    Channels = res.Channels
                        .Select(c => new ChannelTestResultOut
                        {
                            ChannelNumber = c.ChannelNumber,
                            RtspOk = c.RtspOk,
                            RtspUrlWorking = c.RtspUrlWorking,
                            Error = c.Error,
                            Attempts = c.Attempts.Take(5).ToList()
                        })
                        .ToList()
                }
            });
        }

        return Ok(new TestCftvConnectionOut
        {
            PingOk = res.PingOk,
            TcpRtspOk = res.TcpRtspOk,
            Channels = res.Channels
                .Where(c => c.RtspOk)
                .ToList()
        });
    }
}
