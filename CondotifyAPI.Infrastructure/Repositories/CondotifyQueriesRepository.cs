using AutoMapper;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Enterprises;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure.Repositories;

public class CondotifyQueriesRepository : ICondotifyQueriesRepository
{
    private readonly DatabaseContext _context;
    private readonly IMapper _mapper;

    public CondotifyQueriesRepository(DatabaseContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    // =========================
    // USERS
    // =========================
    public async Task<UserAccess?> GetUserByIdAsync(Guid userId)
    {
        var dto = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId);

        return dto == null ? null : _mapper.Map<UserAccess>(dto);
    }

    public async Task<UserAccess?> GetUserByEmailAsync(string email)
    {
        var dto = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email == email);

        return dto == null ? null : _mapper.Map<UserAccess>(dto);
    }

    // =========================
    // ENTERPRISE
    // =========================
    public async Task<Enterprise?> GetEnterpriseByIdAsync(Guid enterpriseId)
    {
        var dto = await _context.Enterprises
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == enterpriseId);

        return dto == null ? null : _mapper.Map<Enterprise>(dto);
    }

    public async Task<List<Enterprise>> GetEnterprisesAsync()
    {
        var list = await _context.Enterprises
            .AsNoTracking()
            .ToListAsync();

        return _mapper.Map<List<Enterprise>>(list);
    }

    // =========================
    // LICENSE
    // =========================
    public async Task<License?> GetLicenseByIdAsync(Guid licenseId)
    {
        var dto = await _context.Licenses
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == licenseId);

        return dto == null ? null : _mapper.Map<License>(dto);
    }

    public async Task<List<License>> GetLicensesByEnterpriseAsync(Guid enterpriseId)
    {
        var list = await _context.Licenses
            .AsNoTracking()
            .Where(x => x.EnterpriseId == enterpriseId)
            .ToListAsync();

        return _mapper.Map<List<License>>(list);
    }

    // =========================
    // ACCESS CONTROL DEVICES
    // =========================
    public async Task<AccessControlDevice?> GetAccessControlDeviceByIdAsync(Guid deviceId)
    {
        var dto = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId);

        return dto == null ? null : _mapper.Map<AccessControlDevice>(dto);
    }

    public async Task<List<AccessControlDevice>> GetAccessControlDevicesByLicenseAsync(Guid licenseId)
    {
        var list = await _context.Devices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .ToListAsync();

        return _mapper.Map<List<AccessControlDevice>>(list);
    }

    // =========================
    // CFTV DEVICES
    // =========================
    public async Task<CFTVDevice?> GetCFTVDeviceByIdAsync(Guid deviceId)
    {
        var dto = await _context.CFTVDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId);

        return dto == null ? null : _mapper.Map<CFTVDevice>(dto);
    }

    public async Task<List<CFTVDevice>> GetCFTVDevicesByLicenseAsync(Guid licenseId)
    {
        var list = await _context.CFTVDevices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .ToListAsync();

        return _mapper.Map<List<CFTVDevice>>(list);
    }

    public async Task<AccessControlDevice?> GetDeviceByDeviceIdAsync(Guid deviceId)
    {
        var entity = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == deviceId);

        if (entity == null)
            return null;

        return _mapper.Map<AccessControlDevice>(entity);
    }
}
