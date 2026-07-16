using CondotifyAPI.Commands.Licenses;
using CondotifyAPI.Data.Licenses;
using CondotifyAPI.Query;
using DigitalWorldOnline.Management.Api.Data;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/access/licenses")]
public class LicenseAccessController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILicenseAuthorizationService _authorization;
    private readonly DatabaseContext _context;

    public LicenseAccessController(ISender sender, ILicenseAuthorizationService authorization, DatabaseContext context)
    {
        _sender = sender;
        _authorization = authorization;
        _context = context;
    }

    [HttpGet]
    [Authorize] 
    public async Task<IActionResult> GetLicenses()
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("UserId não encontrado no token");

            var query = new GetLicenseSummariesByUserQuery(userIdClaim);

            var licenses = await _sender.Send(query);
            var allowed = await _authorization.GetAccessibleLicenseIdsAsync(User);
            return Ok(licenses.Where(x => allowed.Contains(x.Id)).ToList()); 
        }
        catch (FluentValidation.ValidationException ex)
        {
            return BadRequest(new { Result = "InvalidRequest", Errors = ex.Errors.Select(e => e.ErrorMessage) });
        }
        catch (Exception)
        {
            throw;
        }
    }

    [HttpGet("by-user/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetLicensesByUserId(Guid id)
    {
        try
        {
            var query = new GetLicenseSummariesByUserQuery(id.ToString());

            var licenses = await _sender.Send(query);
            var allowed = await _authorization.GetAccessibleLicenseIdsAsync(User);
            return Ok(licenses.Where(x => allowed.Contains(x.Id)).ToList());
        }
        catch (FluentValidation.ValidationException ex)
        {
            return BadRequest(new { Result = "InvalidRequest", Errors = ex.Errors.Select(e => e.ErrorMessage) });
        }
        catch (Exception)
        {
            throw;
        }
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetLicenseById(Guid id)
    {
        try
        {
            if (await _authorization.GetGrantAsync(User, id) is null)
                return Forbid();
            var query = new GetLicenseByIdQuery(id);

            var license = await _sender.Send(query);
            if (license == null)
                return NotFound();

            return Ok(license);
        }
        catch (FluentValidation.ValidationException ex)
        {
            return BadRequest(new { Result = "InvalidRequest", Errors = ex.Errors.Select(e => e.ErrorMessage) });
        }
        catch (Exception)
        {
            throw;
        }
    }

    [HttpPost("by-enterprise")]
    [Authorize] 
    public async Task<IActionResult> CreateByEnterprise([FromBody] CreateLicenseByEnterpriseIn license)
    {
        if (!Guid.TryParse(license.EnterpriseId, out var enterpriseId) ||
            !Guid.TryParse(User.FindFirstValue("enterprise_id"), out var currentEnterpriseId) ||
            enterpriseId != currentEnterpriseId ||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Forbid();

        var canCreate = await _context.Users.AsNoTracking().AnyAsync(x =>
            x.Id == userId && x.EnterpriseId == enterpriseId &&
            (x.AccessType == AccessTypeEnum.Admin || x.AccessType == AccessTypeEnum.Developer));
        if (!canCreate) return Forbid();

        var command = license.ToCommand();
        var validator = await new CreateLicenseByEnterpriseCommandValidator().ValidateAsync(command);

        if (!validator.IsValid)
            return BadRequest(new
            {
                Result = "InvalidRequest",
                Errors = string.Join(";", validator.Errors.Select(x => x.ErrorMessage))
            });

        var result = await _sender.Send(command);

        if (result != null)
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
