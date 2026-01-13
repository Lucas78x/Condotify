using CondotifyAPI.Commands.Enterprises;
using CondotifyAPI.Commands.Equipments;
using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Commands.Users;
using CondotifyAPI.Data.Enterprise;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Data.Users;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.CFTV;
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
    private readonly ICFTVService _cftvService;
    private readonly string _apiKey;

    public AccessController(ISender sender,
        IAccessControlService controlService,
        ICFTVService cftvService)
    {
        _sender = sender;
        _controlService = controlService;
        _cftvService = cftvService;
        _apiKey = Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY")!;

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

    [HttpPost("create-cftvdevice-by-license")]
    [ProducesResponseType(typeof(CreateAccessControlDeviceByLicenseOut), 201)]
    public async Task<IActionResult> CreateCftvDeviceByLicense(
    [FromHeader(Name = "X-API-Key")] string apiKey,
    [FromBody] CreateCftvDeviceByLicenseIn cftv)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
        {
            return Unauthorized();
        }

        var command = cftv.ToCommand();

        var validator = await new CreateCftvDeviceByLicenseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });
        }

        var device = CFTVDevice.Create(
                    cftv.Name,
                    cftv.UserName,
                    cftv.Password,
                    cftv.IpAddress,
                    cftv.HTTPPort,
                    cftv.RTSPPort,
                    cftv.IpType,
                    cftv.Proportion,
                    cftv.Mark,
                    cftv.DeviceType,
                    cftv.MaxChannels,
                    cftv.Channels
                );



        var res = await _cftvService.TestAsync(device);
        var anyChannelOk = res.Channels.Any(c => c.RtspOk);

        if (!anyChannelOk)
        {
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = "Não foi possível conectar ao dispositivo, verifique os dados de acesso e tente novamente."
            });
        }


        var result = await _sender.Send(command);

        if (result != null)
        {
            return Created("", new CreateCftvDeviceByLicenseOut
            {
                Result = CreateAccessControlDeviceResult.Created,
                Device = new CftvDeviceResponse
                {
                    Id = result.Id,
                    Name = result.Name,
                    IpAddress = result.IpAddress,
                    HTTPPort = result.HTTPPort,
                    RTSPPort = result.RTSPPort,
                    DeviceType = result.DeviceType,
                    IsActive = true,
                    CreatedAt = DateTime.Now,
                }
            });
        }
        else
        {
            return Conflict(new CreateCftvDeviceByLicenseOut
            {
                Result = CreateAccessControlDeviceResult.InvalidData,
                Errors = "Não foi possível conectar ao dispositivo, verifique os dados de acesso e tente novamente."
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

    [HttpPost("test-cftv-connection")]
    [ProducesResponseType(typeof(TestCftvConnectionOut), 200)]
    public async Task<IActionResult> TestCftvConnection(
       [FromHeader(Name = "X-API-Key")] string apiKey,
       [FromBody] TestCftvConnectionIn cftv)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey != _apiKey)
            return Unauthorized();

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
                        cftv.HTTPPort,
                        cftv.RTSPPort,
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
