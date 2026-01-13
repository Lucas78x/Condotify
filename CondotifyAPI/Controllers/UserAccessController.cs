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

    [ApiController]
    [Route("api/access/users")]
    public class UserAccessController : ControllerBase
    {
        private readonly ISender _sender;
        private readonly string _apiKey;

        public UserAccessController(ISender sender)
        {
            _sender = sender;
            _apiKey = Environment.GetEnvironmentVariable("CT_UserAccess_API_KEY")!;
#if DEBUG
            _apiKey = "1";
#endif
        }

        [HttpPost]
        public async Task<IActionResult> Create(
            [FromHeader(Name = "X-API-Key")] string apiKey,
            [FromBody] CreateUserAccessIn user)
        {
            if (apiKey != _apiKey)
                return Unauthorized();

            var command = user.ToCommand();
            var validator = await new CreateUserAccessCommandValidator().ValidateAsync(command);

            if (!validator.IsValid)
                return BadRequest(new
                {
                    Result = "InvalidRequest",
                    Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
                });

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
            [FromHeader(Name = "X-API-Key")] string apiKey,
            [FromBody] CreateUserAccessByEnterpriseIn user)
        {
            if (apiKey != _apiKey)
                return Unauthorized();

            var command = user.ToCommand();
            var validator = await new CreateUserAccessByEnterpriseCommandValidator().ValidateAsync(command);

            if (!validator.IsValid)
                return BadRequest(new
                {
                    Result = "InvalidRequest",
                    Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
                });

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

}
