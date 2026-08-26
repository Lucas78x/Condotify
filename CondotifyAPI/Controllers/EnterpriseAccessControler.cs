using CondotifyAPI.Commands.Enterprises;
using CondotifyAPI.Data.Enterprise;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CondotifyAPI.Services.Security;

// Rota de bootstrap: cria a empresa antes de qualquer conta existir para ela,
// entao nao ha JWT possivel para autenticar a chamada. Protegida por rede
// (porta interna, ver InternalRouteGuard) em vez de credencial de usuario.
[ApiController]
[AllowAnonymous]
[Route("api/internal/enterprises")]
public class EnterpriseAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string? _apiKey;
    private readonly ILogger<EnterpriseAccessController> _logger;

    public EnterpriseAccessController(ISender sender, ILogger<EnterpriseAccessController> logger)
    {
        _sender = sender;
        _logger = logger;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateEnterpriseIn enterprise)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
        {
            _logger.LogWarning("Tentativa de criar empresa rejeitada: API key invalida. RemoteIp={RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized();
        }

        var command = enterprise.ToCommand();

        var validator = await new CreateEnterpriseCommandValidator().ValidateAsync(command);

        if (validator.IsValid)
        {
            var result = await _sender.Send(command);

            if (result == EnterpriseCreateResult.Created)
            {
                _logger.LogInformation("Empresa criada via bootstrap interno. CNPJ={CNPJ} RemoteIp={RemoteIp}", enterprise.CNPJ, HttpContext.Connection.RemoteIpAddress);
                return Created("", new CreateEnterpriseOut { Result = result });
            }
            else
            {
                string errorMessage = result switch
                {
                    EnterpriseCreateResult.CNPJInUse => "O CNPJ já está em uso.",
                    EnterpriseCreateResult.EmailInUse => "O email já está em uso.",
                    EnterpriseCreateResult.InvalidData => "Os dados fornecidos são inválidos.",
                    _ => "Erro desconhecido."
                };

                return Conflict(new CreateEnterpriseOut
                {
                    Result = result,
                    Errors = errorMessage
                });
            }
        }
        else
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }
    }
}
