using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Queries;
using CondotifyAPI.Services.AccessControl;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/access/devices")]
public class DeviceAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IAccessControlService _controlService;
    private readonly string _apiKey;

    public DeviceAccessController(ISender sender, IAccessControlService controlService)
    {
        _sender = sender;
        _controlService = controlService;
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

        var ok = await _controlService.TestConnectionAsync(device);

        return ok
            ? Ok(new { Result = "Success" })
            : BadRequest(new { Result = "Fail" });
    }

    [HttpPost("OpenDoor")]
    public async Task<IActionResult> OpenDoor(
    [FromHeader(Name = "X-API-Key")] string apiKey,
    [FromBody] CftvDeviceOpenDoor command)
    {
        if (apiKey != _apiKey)
            return Unauthorized();

        var device = await _sender.Send(new GetAccessDeviceByDeviceIdQuery(command.DeviceId));

        if (device == null)
            return BadRequest(new { Result = $"Device not Found by Id {command.DeviceId}" });

        var isOnline = await _controlService.TestConnectionAsync(device);

        var result = await _controlService.OpenDoorAsync(device);

        return result
            ? Ok(new { Result = "Success" })
            : BadRequest(new { Result = "Fail" });
    }
}
