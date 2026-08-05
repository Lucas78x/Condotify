using System.Security.Claims;
using CondotifyAPI.Data.Vehicles;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}")]
public sealed class VehicleController(DatabaseContext context) : ControllerBase
{
    [HttpGet("units/{unitId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ViewVehicles)]
    public async Task<IActionResult> ListByUnit(Guid licenseId, Guid unitId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (!await UnitBelongsAsync(licenseId, unitId)) return NotFound();

        var vehicles = await context.Vehicles
            .AsNoTracking()
            .Where(v => v.UnitId == unitId)
            .OrderBy(v => v.Plate)
            .ToListAsync();

        return Ok(vehicles.Select(ToOut));
    }

    [HttpPost("units/{unitId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Create(Guid licenseId, Guid unitId, [FromBody] VehicleCreateIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (!await UnitBelongsAsync(licenseId, unitId)) return NotFound();

        var plate = (input.Plate ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(plate))
            return BadRequest(new { Result = "InvalidPlate", Errors = "Informe a placa do veiculo." });

        var alreadyExists = await context.Vehicles.AsNoTracking()
            .AnyAsync(v => v.UnitId == unitId && v.Plate == plate);
        if (alreadyExists)
            return Conflict(new { Result = "DuplicatePlate", Errors = "Esta unidade ja possui um veiculo com essa placa." });

        var now = DateTime.UtcNow;
        var vehicle = new VehicleDTO
        {
            Id = Guid.NewGuid(),
            UnitId = unitId,
            ResidentId = input.ResidentId,
            Plate = plate,
            Brand = input.Brand ?? string.Empty,
            Model = input.Model ?? string.Empty,
            Color = input.Color ?? string.Empty,
            Type = string.IsNullOrWhiteSpace(input.Type) ? "Carro" : input.Type,
            TagIdentifier = input.TagIdentifier ?? string.Empty,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return Created($"api/access/licenses/{licenseId}/vehicles/{vehicle.Id}", ToOut(vehicle));
    }

    [HttpPatch("vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Update(Guid licenseId, Guid vehicleId, [FromBody] VehicleUpdateIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        vehicle.Brand = input.Brand ?? string.Empty;
        vehicle.Model = input.Model ?? string.Empty;
        vehicle.Color = input.Color ?? string.Empty;
        vehicle.Type = string.IsNullOrWhiteSpace(input.Type) ? "Carro" : input.Type;
        vehicle.TagIdentifier = input.TagIdentifier ?? string.Empty;
        vehicle.ResidentId = input.ResidentId;
        vehicle.IsActive = input.IsActive;
        vehicle.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return Ok(ToOut(vehicle));
    }

    [HttpDelete("vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Deactivate(Guid licenseId, Guid vehicleId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        vehicle.IsActive = false;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return NoContent();
    }

    private static VehicleOut ToOut(VehicleDTO v) => new()
    {
        Id = v.Id,
        UnitId = v.UnitId,
        ResidentId = v.ResidentId,
        Plate = v.Plate,
        Brand = v.Brand,
        Model = v.Model,
        Color = v.Color,
        Type = v.Type,
        TagIdentifier = v.TagIdentifier,
        IsActive = v.IsActive,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt
    };

    private async Task<bool> UnitBelongsAsync(Guid licenseId, Guid unitId) =>
        await context.Units.AsNoTracking().AnyAsync(u => u.Id == unitId && u.Block.LicenseId == licenseId);

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
