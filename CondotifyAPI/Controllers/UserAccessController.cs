using CondotifyAPI.Commands.Users;
using CondotifyAPI.Data.Users;
using CondotifyAPI.Services.Security;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWorldOnline.Management.Api.Controllers;

[ApiController]
[Route("api/access/users")]
public sealed class UserAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string? _apiKey;

    public UserAccessController(ISender sender)
    {
        _sender = sender;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromBody] CreateUserAccessIn user)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        var command = user.ToCommand();
        var validator = await new CreateUserAccessCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }

        var result = await _sender.Send(command);

        if (result == UserAccessCreateResult.Created)
            return Created("", new CreateUserAccessOut { Result = result });

        return Conflict(new CreateUserAccessOut
        {
            Result = result,
            Errors = MapUserErrors(result)
        });
    }

    [HttpPost("by-enterprise")]
    public async Task<IActionResult> CreateByEnterprise(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromBody] CreateUserAccessByEnterpriseIn user)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
            return Unauthorized();

        var command = user.ToCommand();
        var validator = await new CreateUserAccessByEnterpriseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }

        var result = await _sender.Send(command);

        if (result == UserAccessCreateResult.Created)
            return Created("", new CreateUserAccessOut { Result = result });

        return Conflict(new CreateUserAccessOut
        {
            Result = result,
            Errors = MapUserErrors(result)
        });
    }

    private static string MapUserErrors(UserAccessCreateResult result) =>
        result switch
        {
            UserAccessCreateResult.EmailInUse => "O email já está em uso.",
            UserAccessCreateResult.RGInUse => "O RG já está em uso.",
            UserAccessCreateResult.CPFInUse => "O CPF já está em uso.",
            UserAccessCreateResult.PhoneInUse => "O telefone já está em uso.",
            _ => "Erro desconhecido."
        };
}
