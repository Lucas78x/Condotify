using CondotifyAPI.Commands.Enterprises;
using CondotifyAPI.Data.Enterprise;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using CondotifyAPI.Services.Security;

[ApiController]
[Route("api/access/enterprises")]
public class EnterpriseAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly string? _apiKey;

    public EnterpriseAccessController(ISender sender)
    {
        _sender = sender;
        _apiKey = ApiKeySecurity.GetConfiguredKey();
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateEnterpriseIn enterprise)
    {
        if (!ApiKeySecurity.IsValid(_apiKey, apiKey))
        {
            return Unauthorized();
        }

        var command = enterprise.ToCommand();

        var validator = await new CreateEnterpriseCommandValidator().ValidateAsync(command);

        if (validator.IsValid)
        {
            var result = await _sender.Send(command);

            if (result == EnterpriseCreateResult.Created)
            {
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
