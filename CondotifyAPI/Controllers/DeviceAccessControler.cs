using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.AccessControl;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize]
[Route("api/access/devices")]
public class DeviceAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IAccessControlService _controlService;
    private readonly ILicenseAuthorizationService _authorization;
    private readonly string _apiKey;

    public DeviceAccessController(ISender sender, IAccessControlService controlService, ILicenseAuthorizationService authorization)
    {
        _sender = sender;
        _controlService = controlService;
        _authorization = authorization;
        _apiKey = Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY")!;
#if DEBUG
        _apiKey = "1";
#endif
    }

    [HttpPost("by-license")]
    public async Task<IActionResult> CreateByLicense(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (apiKey != _apiKey)
            return Unauthorized();

        if (!Guid.TryParse(device.LicenseId, out var licenseId))
            return BadRequest(new { Result = "InvalidRequest", Errors = "A licenca informada e invalida." });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var command = device.ToCommand();
        var validator = await new CreateAccessControlDeviceByLicenseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });

        if (device.IsActive && !await _controlService.TestConnectionAsync(device))
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = "Não foi possível conectar ao dispositivo."
            });

        var result = await _sender.Send(command);

        return Created("", new CreateAccessControlDeviceByLicenseOut
        {
            Result = CreateAccessControlDeviceResult.Created,
            Device = result
        });
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (apiKey != _apiKey)
            return Unauthorized();

        if (!Guid.TryParse(device.LicenseId, out var licenseId))
            return BadRequest(new { Result = "InvalidRequest", Errors = "A licenca informada e invalida." });

        if (!await _authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManageDevices, HttpContext.RequestAborted))
            return Forbid();

        var ok = await _controlService.TestConnectionAsync(device);

        return ok
            ? Ok(new { Result = "Success" })
            : BadRequest(new { Result = "Fail" });
    }
}
