using CondotifyAPI.Data.Structure;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Route("api/access/licenses/{licenseId:guid}")]
[Authorize]
public class LicenseStructureController : ControllerBase
{
    private readonly DatabaseContext _context;

    public LicenseStructureController(DatabaseContext context)
    {
        _context = context;
    }

    [HttpGet("structure")]
    public async Task<IActionResult> GetStructure(Guid licenseId)
    {
        var exists = await _context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId);
        if (!exists) return NotFound();

        var blocks = await _context.Blocks
            .AsNoTracking()
            .Include(x => x.Units)
            .ThenInclude(x => x.Residents)
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(new LicenseStructureOut
        {
            LicenseId = licenseId,
            Blocks = blocks.Select(ToBlockOut).ToList()
        });
    }

    [HttpPost("blocks")]
    public async Task<IActionResult> CreateBlock(Guid licenseId, [FromBody] CreateBlockIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome do bloco e obrigatorio." });

        var exists = await _context.Licenses.AnyAsync(x => x.Id == licenseId);
        if (!exists) return NotFound();

        var duplicate = await _context.Blocks.AnyAsync(x => x.LicenseId == licenseId && x.Name == input.Name);
        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe um bloco com este nome." });

        var block = new BlockDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Name = input.Name.Trim(),
            CreatedAt = DateTime.UtcNow,
            LastUpdatedAt = DateTime.UtcNow,
            Units = new()
        };

        _context.Blocks.Add(block);
        await _context.SaveChangesAsync();

        return Created("", ToBlockOut(block));
    }

    [HttpPost("units")]
    public async Task<IActionResult> CreateUnit(Guid licenseId, [FromBody] CreateUnitIn input)
    {
        if (input.BlockId == Guid.Empty || string.IsNullOrWhiteSpace(input.Number))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Bloco e numero da unidade sao obrigatorios." });

        var block = await _context.Blocks.FirstOrDefaultAsync(x => x.Id == input.BlockId && x.LicenseId == licenseId);
        if (block == null) return NotFound();

        var duplicate = await _context.Units.AnyAsync(x => x.BlockId == input.BlockId && x.Number == input.Number);
        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe uma unidade com este numero neste bloco." });

        var unit = new UnitDTO
        {
            Id = Guid.NewGuid(),
            BlockId = input.BlockId,
            Number = input.Number.Trim(),
            Floor = input.Floor?.Trim() ?? string.Empty,
            Residents = new()
        };

        _context.Units.Add(unit);
        await _context.SaveChangesAsync();

        return Created("", new UnitOut
        {
            Id = unit.Id,
            BlockId = unit.BlockId,
            BlockName = block.Name,
            Number = unit.Number,
            Floor = unit.Floor,
            TotalResidents = 0
        });
    }

    [HttpPost("residents")]
    public async Task<IActionResult> CreateResident(Guid licenseId, [FromBody] CreateResidentIn input)
    {
        if (input.UnitId == Guid.Empty || string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Unidade e nome do morador sao obrigatorios." });

        var unit = await _context.Units
            .Include(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId);

        if (unit == null) return NotFound();

        var duplicate = await _context.Residents.AnyAsync(x =>
            x.UnitId == input.UnitId &&
            !string.IsNullOrWhiteSpace(input.CPF) &&
            x.CPF == input.CPF);

        if (duplicate)
            return Conflict(new { Result = "Duplicate", Errors = "Ja existe um morador com este CPF nesta unidade." });

        var resident = new ResidentAccessDTO
        {
            Id = Guid.NewGuid(),
            UnitId = input.UnitId,
            Name = input.Name.Trim(),
            Email = input.Email?.Trim() ?? string.Empty,
            Password = input.Password ?? string.Empty,
            PhoneNumber = input.PhoneNumber?.Trim() ?? string.Empty,
            CPF = input.CPF?.Trim() ?? string.Empty,
            RG = input.RG?.Trim() ?? string.Empty,
            BirthDate = input.BirthDate?.Trim() ?? string.Empty,
            ApartmentNumber = string.IsNullOrWhiteSpace(input.ApartmentNumber) ? unit.Number : input.ApartmentNumber.Trim(),
            ImgUrl = string.Empty,
            AccessType = input.AccessType,
            FirstAccess = true,
            Temporary = input.Temporary,
            Expire = input.Expire ?? DateTime.UtcNow.AddYears(10),
            LastAccess = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            AccessCredentials = new List<ResidentAccessCredentialDTO>()
        };

        _context.Residents.Add(resident);
        await _context.SaveChangesAsync();

        return Created("", ToResidentOut(resident, unit.Number));
    }

    [HttpGet("devices")]
    public async Task<IActionResult> GetAccessDevices(Guid licenseId)
    {
        var devices = await _context.Devices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new AccessDeviceOut
            {
                Id = x.Id,
                Name = x.Name,
                IPAddress = x.IPAddress,
                Port = x.Port,
                Model = x.Model,
                SerialNumber = x.SerialNumber,
                MACAddress = x.MACAddress,
                Type = x.Type.ToString(),
                IsActive = x.IsActive
            })
            .ToListAsync();

        return Ok(devices);
    }

    [HttpGet("cftv")]
    public async Task<IActionResult> GetCftvDevices(Guid licenseId)
    {
        var devices = await _context.CFTVDevices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new CftvDeviceOut
            {
                Id = x.Id,
                Name = x.Name,
                IpAddress = x.IpAddress,
                HTTPPort = x.HTTPPort,
                RTSPPort = x.RTSPPort,
                DeviceType = x.DeviceType.ToString(),
                MaxChannels = x.MaxChannels
            })
            .ToListAsync();

        return Ok(devices);
    }

    private static BlockOut ToBlockOut(BlockDTO block)
    {
        var units = block.Units?.OrderBy(x => x.Number).Select(unit => new UnitOut
        {
            Id = unit.Id,
            BlockId = block.Id,
            BlockName = block.Name,
            Number = unit.Number,
            Floor = unit.Floor,
            TotalResidents = unit.Residents?.Count ?? 0,
            Residents = unit.Residents?.Select(resident => ToResidentOut(resident, unit.Number)).ToList() ?? new()
        }).ToList() ?? new();

        return new BlockOut
        {
            Id = block.Id,
            Name = block.Name,
            TotalUnits = units.Count,
            TotalResidents = units.Sum(x => x.TotalResidents),
            Units = units
        };
    }

    private static ResidentOut ToResidentOut(ResidentAccessDTO resident, string unitNumber)
    {
        return new ResidentOut
        {
            Id = resident.Id,
            UnitId = resident.UnitId,
            UnitNumber = unitNumber,
            Name = resident.Name,
            Email = resident.Email,
            PhoneNumber = resident.PhoneNumber,
            CPF = resident.CPF,
            RG = resident.RG,
            ApartmentNumber = resident.ApartmentNumber,
            AccessType = resident.AccessType.ToString(),
            Temporary = resident.Temporary,
            Expire = resident.Expire
        };
    }
}
