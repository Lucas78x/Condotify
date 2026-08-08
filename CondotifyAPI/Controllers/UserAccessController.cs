using CondotifyAPI.Commands.Users;
using CondotifyAPI.Data.Users;
using CondotifyAPI.Services.Security;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWorldOnline.Management.Api.Controllers;

// Rota de bootstrap: cria contas de staff, inclusive a primeira de uma nova
// empresa, entao nao ha JWT possivel para autenticar a chamada. Protegida por
// rede (porta interna, ver InternalRouteGuard) em vez de credencial de usuario.
[ApiController]
[Route("api/internal/users")]
public sealed class UserAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string? _apiKey;
    private readonly ILogger<UserAccessController> _logger;

    public UserAccessController(ISender sender, ILogger<UserAccessController> logger)
    {
        _sender = sender;
        _logger = logger;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-API-Key")] string? apiKey,
        [FromBody] CreateUserAccessIn user)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
        {
            _logger.LogWarning("Tentativa de criar usuario rejeitada: API key invalida. RemoteIp={RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

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
        {
            _logger.LogInformation("Usuario staff criado via bootstrap interno. Email={Email} EnterpriseId={EnterpriseId} Type={Type} RemoteIp={RemoteIp}", user.Email, user.EnterpriseId, user.Type, HttpContext.Connection.RemoteIpAddress);
            return Created("", new CreateUserAccessOut { Result = result });
        }

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
        {
            _logger.LogWarning("Tentativa de criar usuario (by-enterprise) rejeitada: API key invalida. RemoteIp={RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

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
        {
            _logger.LogInformation("Usuario staff criado via bootstrap interno (by-enterprise). Email={Email} EnterpriseId={EnterpriseId} Type={Type} RemoteIp={RemoteIp}", user.Email, user.EnterpriseId, user.Type, HttpContext.Connection.RemoteIpAddress);
            return Created("", new CreateUserAccessOut { Result = result });
        }

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
