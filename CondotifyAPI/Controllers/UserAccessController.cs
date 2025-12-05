using CondotifyAPI.Commands.Enterprises;
using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Commands.Users;
using CondotifyAPI.Data.Enterprise;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Data.Users;
using CondotifyAPI.Services.AccessControl;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace DigitalWorldOnline.Management.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IAccessControlService _controlService;
    private readonly string _apiKey;

    public AccessController(ISender sender,
        IAccessControlService controlService)
    {
        _sender = sender;
        _controlService = controlService;
        _apiKey = Environment.GetEnvironmentVariable("DNA_ACCOUNT_API_KEY")!;

#if DEBUG
        _apiKey = "1";
#endif
    }


    [HttpPost("create-user")]
    [ProducesResponseType(typeof(CreateUserAccessOut), 201)]
    public async Task<IActionResult> Create(
       [FromHeader(Name = "X-API-Key")] string apiKey,
       [FromBody] CreateUserAccessIn user)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
            return Unauthorized();
        }

        var command = user.ToCommand();

        var validator = await new CreateUserAccessCommandValidator().ValidateAsync(command);

        if (validator.IsValid)
        {
            var result = await _sender.Send(command);

            if (result == UserAccessCreateResult.Created)
            {
                return Created("", new CreateUserAccessOut { Result = result });
            }
            else
            {
                string errorMessage = result switch
                {
                    UserAccessCreateResult.EmailInUse => "O email já está em uso.",
                    UserAccessCreateResult.RGInUse => "O RG já está em uso.",
                    UserAccessCreateResult.CPFInUse => "O CPF já está em uso.",
                    UserAccessCreateResult.PhoneInUse => "O telefone já está em uso.",
                    UserAccessCreateResult.InvalidData => "Os dados fornecidos são inválidos.",
                    _ => "Erro desconhecido."
                };

                return Conflict(new CreateUserAccessOut
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

    [HttpPost("create-user-by-enterprise")]
    [ProducesResponseType(typeof(CreateUserAccessOut), 201)]
    public async Task<IActionResult> CreateByEnterprise(
     [FromHeader(Name = "X-API-Key")] string apiKey,
     [FromBody] CreateUserAccessByEnterpriseIn user)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
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

        // Executar comando
        var result = await _sender.Send(command);

        // Retorno
        if (result == UserAccessCreateResult.Created)
        {
            return Created("", new CreateUserAccessOut { Result = result });
        }
        else
        {
            string errorMessage = result switch
            {
                UserAccessCreateResult.EmailInUse => "O email já está em uso.",
                UserAccessCreateResult.RGInUse => "O RG já está em uso.",
                UserAccessCreateResult.CPFInUse => "O CPF já está em uso.",
                UserAccessCreateResult.PhoneInUse => "O telefone já está em uso.",
                UserAccessCreateResult.InvalidData => "Os dados fornecidos são inválidos.",
                _ => "Erro desconhecido."
            };

            return Conflict(new CreateUserAccessOut
            {
                Result = result,
                Errors = errorMessage
            });
        }
    }

    [HttpPost("create-license-by-enterprise")]
    [ProducesResponseType(typeof(CreateUserAccessOut), 201)]
    public async Task<IActionResult> CreateLicenseByEnterprise(
       [FromHeader(Name = "X-API-Key")] string apiKey,
       [FromBody] CreateLicenseByEnterpriseIn user)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
            return Unauthorized();
        }

        var command = user.ToCommand();

        var validator = await new CreateLicenseByEnterpriseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }

        var result = await _sender.Send(command);

        if (result != null)
        {
            return Created("", new CreateLicenseOut { Result = LicenseCreateResult.Created, License = result });
        }
        else
        {
            return Conflict(new CreateLicenseOut
            {
                Result = LicenseCreateResult.LicenseKeyInUse,
                Errors = "Licença já existente"
            });
        }
    }

    [HttpPost("create-device-by-license")]
    [ProducesResponseType(typeof(CreateAccessControlDeviceByLicenseOut), 201)]
    public async Task<IActionResult> CreateDeviceByLicense(
      [FromHeader(Name = "X-API-Key")] string apiKey,
      [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
            return Unauthorized();
        }

        var command = device.ToCommand();

        var validator = await new CreateAccessControlDeviceByLicenseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }

        if (device.IsActive)
        {
            var successConection = await _controlService.TestConnectionAsync(device);
            if (!successConection)
            {
                return BadRequest(new
                {
                    Result = "InvalidRequest",
                    Errors = "Não foi possível conectar ao dispositivo, verifique os dados de acesso e tente novamente."
                });
            }
        }

        var result = await _sender.Send(command);

        if (result != null)
        {
            return Created("", new CreateAccessControlDeviceByLicenseOut { Result = CreateAccessControlDeviceResult.InvalidData, Device = result });
        }
        else
        {
            return Conflict(new CreateLicenseOut
            {
                Result = LicenseCreateResult.LicenseKeyInUse,
                Errors = "Licença já existente"
            });
        }
    }

    [HttpPost("test-connection")]
    [ProducesResponseType(typeof(CreateAccessControlDeviceByLicenseOut), 201)]
    public async Task<IActionResult> TestConnection(
   [FromHeader(Name = "X-API-Key")] string apiKey,
   [FromBody] CreateAccessControlDeviceByLicenseIn device)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
            return Unauthorized();
        }

        var successConection = await _controlService.TestConnectionAsync(device);
        if (!successConection)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = "Não foi possível conectar ao dispositivo, verifique os dados de acesso e tente novamente."
            });
        }

        return Created("", new CreateAccessControlDeviceByLicenseOut { Result = CreateAccessControlDeviceResult.InvalidData });
    }

    [HttpPost("create-enterprise")]
    [ProducesResponseType(typeof(CreateEnterpriseOut), 201)]
    public async Task<IActionResult> CreateEnterprise(
        [FromHeader(Name = "X-API-Key")] string apiKey,
        [FromBody] CreateEnterpriseIn enterprise)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
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
