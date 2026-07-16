using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CondotifyAPI.Services.Authorization;
using System.Security.Claims;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/operations")]
[Authorize]
public sealed class OperationsController(DatabaseContext context) : ControllerBase
{
    [HttpGet("residents/search")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> SearchResidents(
        [FromQuery] string? query,
        [FromQuery] string? document,
        [FromQuery] string? phone,
        [FromQuery] string? credential,
        [FromQuery] string? unit,
        [FromQuery] Guid? licenseId,
        [FromQuery] int take = 50)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId)) return Unauthorized();

        var residents = context.Residents
            .AsNoTracking()
            .Where(x => x.Unit.Block.License.EnterpriseId == enterpriseId);

        if (licenseId.HasValue)
            residents = residents.Where(x => x.Unit.Block.LicenseId == licenseId.Value);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = Pattern(query);
            residents = residents.Where(x => EF.Functions.ILike(x.Name, pattern) || EF.Functions.ILike(x.Email, pattern));
        }
        if (!string.IsNullOrWhiteSpace(document))
        {
            var pattern = Pattern(document);
            residents = residents.Where(x => EF.Functions.ILike(x.CPF, pattern) || EF.Functions.ILike(x.RG, pattern));
        }
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var pattern = Pattern(phone);
            residents = residents.Where(x => EF.Functions.ILike(x.PhoneNumber, pattern));
        }
        if (!string.IsNullOrWhiteSpace(unit))
        {
            var pattern = Pattern(unit);
            residents = residents.Where(x => EF.Functions.ILike(x.Unit.Number, pattern) || EF.Functions.ILike(x.Unit.Block.Name, pattern));
        }
        if (!string.IsNullOrWhiteSpace(credential))
        {
            var pattern = Pattern(credential);
            residents = residents.Where(x => x.AccessCredentials.Any(c => EF.Functions.ILike(c.Identifier, pattern)));
        }

        var result = await residents
            .OrderBy(x => x.Name)
            .Take(Math.Clamp(take, 1, 100))
            .Select(x => new GlobalResidentSearchOut
            {
                Id = x.Id,
                LicenseId = x.Unit.Block.LicenseId,
                LicenseName = x.Unit.Block.License.Name,
                Name = x.Name,
                BlockName = x.Unit.Block.Name,
                UnitNumber = x.Unit.Number,
                CPF = x.CPF,
                RG = x.RG,
                PhoneNumber = x.PhoneNumber,
                Email = x.Email,
                AccessType = x.AccessType.ToString(),
                Temporary = x.Temporary,
                Expire = x.Expire,
                Credentials = x.AccessCredentials
                    .OrderByDescending(c => c.IsActive)
                    .ThenBy(c => c.CredentialType)
                    .Take(5)
                    .Select(c => new GlobalCredentialSearchOut
                    {
                        Type = c.CredentialType.ToString(),
                        Identifier = c.CredentialType == AccessCredentialTypeEnum.Password ? "********" : c.Identifier,
                        IsActive = c.IsActive
                    })
                    .ToList()
            })
            .ToListAsync();

        return Ok(result);
    }

    private static string Pattern(string value) => $"%{value.Trim()}%";
}
