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
using CondotifyAPI.ViewModels;

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
            var visible = licenses.Where(x => allowed.Contains(x.Id)).ToList();
            await EnrichLicenseListAsync(visible);
            return Ok(visible);
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
            var visible = licenses.Where(x => allowed.Contains(x.Id)).ToList();
            await EnrichLicenseListAsync(visible);
            return Ok(visible);
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

            await EnrichLicenseSummaryAsync(license, id);

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

    [HttpGet("by-url-key/{urlKey}")]
    [Authorize]
    public async Task<IActionResult> GetLicenseByUrlKey(string urlKey)
    {
        var normalizedKey = urlKey.Trim().ToLowerInvariant();
        var licenseId = await _context.Licenses
            .AsNoTracking()
            .Where(x => x.UrlKey == normalizedKey)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync();

        if (licenseId is null || await _authorization.GetGrantAsync(User, licenseId.Value) is null)
            return NotFound();

        var license = await _sender.Send(new GetLicenseByIdQuery(licenseId.Value));
        if (license is null) return NotFound();

        await EnrichLicenseSummaryAsync(license, licenseId.Value);
        return Ok(license);
    }

    private async Task EnrichLicenseListAsync(List<LicenseSummaryDto> licenses)
    {
        if (licenses.Count == 0) return;

        var ids = licenses.Select(x => x.Id).ToArray();
        var residentCounts = await _context.Blocks
            .AsNoTracking()
            .Where(block => ids.Contains(block.LicenseId))
            .Select(block => new
            {
                block.LicenseId,
                ResidentIds = block.Units
                    .SelectMany(unit => unit.Residents)
                    .Select(resident => resident.Id)
            })
            .ToListAsync();

        var byLicense = residentCounts
            .GroupBy(x => x.LicenseId)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(x => x.ResidentIds).Distinct().Count());

        foreach (var license in licenses)
            license.Moradores = byLicense.GetValueOrDefault(license.Id);
    }

    private async Task EnrichLicenseSummaryAsync(LicenseSummaryViewModel license, Guid licenseId)
    {
        var blocks = await _context.Blocks
            .AsNoTracking()
            .Where(block => block.LicenseId == licenseId)
            .OrderBy(block => block.Name)
            .Select(block => new BlockSummaryViewModel
            {
                Id = block.Id,
                Name = block.Name,
                TotalUnits = block.Units.Count,
                TotalResidents = block.Units
                    .SelectMany(unit => unit.Residents)
                    .Select(resident => resident.Id)
                    .Distinct()
                    .Count()
            })
            .ToListAsync();

        license.Blocks = blocks;
        license.TotalBlocks = blocks.Count;
        license.TotalUnits = blocks.Sum(x => x.TotalUnits);
        license.TotalResidents = await _context.Blocks
            .AsNoTracking()
            .Where(block => block.LicenseId == licenseId)
            .SelectMany(block => block.Units)
            .SelectMany(unit => unit.Residents)
            .Select(resident => resident.Id)
            .Distinct()
            .CountAsync();
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

    [HttpPut("{id:guid}/modules")]
    [Authorize]
    public async Task<IActionResult> UpdateModules(Guid id, [FromBody] CondotifyAPI.Data.Licenses.UpdateLicenseModulesIn input)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId))
            return Forbid();

        var license = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id);
        if (license is null) return NotFound();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (!CanManageModules(user, enterpriseId, license.EnterpriseId))
            return Forbid();

        license.EnabledModules = (CondotifyAPI.Domain.Enums.License.LicenseModuleEnum)input.EnabledModules;
        await _context.SaveChangesAsync();

        return Ok(new { EnabledModules = (long)license.EnabledModules });
    }

    // Extraido para ser testavel sem banco: CreateByEnterprise faz uma checagem
    // parecida (mesmo unico outro lugar que restringe uma acao a Developer/
    // Admin da propria enterprise), mas so cria uma licenca nova -- sempre
    // dentro da propria enterprise por construcao, entao nao precisa comparar
    // contra a enterprise de uma licenca alheia como aqui.
    internal static bool CanManageModules(CondotifyAPI.Domain.DTO.Users.UserAccessDTO? user, Guid callerEnterpriseId, Guid licenseEnterpriseId) =>
        user is not null &&
        user.EnterpriseId == callerEnterpriseId &&
        callerEnterpriseId == licenseEnterpriseId &&
        user.AccessType is AccessTypeEnum.Admin or AccessTypeEnum.Developer;
}
