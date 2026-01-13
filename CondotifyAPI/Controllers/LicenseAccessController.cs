using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Data.Enterprise;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/access/licenses")]
public class LicenseAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string _apiKey;

    public LicenseAccessController(ISender sender)
    {
        _sender = sender;
        _apiKey = Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY")!;
#if DEBUG
        _apiKey = "1";
#endif
    }

    [HttpPost("by-enterprise")]
    public async Task<IActionResult> CreateByEnterprise(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateLicenseByEnterpriseIn license)
    {
        if (apiKey != _apiKey)
            return Unauthorized();

        var command = license.ToCommand();
        var validator = await new CreateLicenseByEnterpriseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });

        var result = await _sender.Send(command);

        if (result is not null)
            return Created("", new CreateLicenseOut
            {
                Result = LicenseCreateResult.Created,
                License = result
            });

        return Conflict(new CreateLicenseOut
        {
            Result = LicenseCreateResult.LicenseKeyInUse,
            Errors = "Licença já existente"
        });
    }
}
