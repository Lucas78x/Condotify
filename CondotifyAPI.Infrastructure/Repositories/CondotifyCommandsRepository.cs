using AutoMapper;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Models.Enterprises;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Domain.Models.License;
using CondotifyAPI.Domain.Models.Units;
using CondotifyAPI.Domain.Models.Users;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure.Repositories;

public class CondotifyCommandsRepository : ICondotifyCommandsRepository
{
    private readonly DatabaseContext _context;
    private readonly IMapper _mapper;

    public CondotifyCommandsRepository(DatabaseContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    public async Task<UserAccessCreateResult> AddUserAccessAsync(UserAccess user)
    {
        var existentAccount = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Email == user.Email ||
                (!string.IsNullOrWhiteSpace(user.Email)));

        if (existentAccount != null)
        {
            if (existentAccount.Email == user.Email)
                return UserAccessCreateResult.EmailInUse;

            if (existentAccount.CPF == user.CPF)
                return UserAccessCreateResult.CPFInUse;

            if (existentAccount.RG == user.RG)
                return UserAccessCreateResult.RGInUse;

            if (existentAccount.PhoneNumber == user.PhoneNumber)
                return UserAccessCreateResult.PhoneInUse;
        }

        var dto = _mapper.Map<UserAccessDTO>(user);

        _context.Add(dto);
        await _context.SaveChangesAsync();

        return UserAccessCreateResult.Created;
    }

    public async Task<EnterpriseCreateResult> AddEnterpriseAsync(Enterprise enterprise)
    {
        var existentEnterprise = await _context.Enterprises
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.CNPJ == enterprise.CNPJ ||
                x.Email == enterprise.Email);

        if (existentEnterprise != null)
        {
            if (existentEnterprise.CNPJ == enterprise.CNPJ)
                return EnterpriseCreateResult.CNPJInUse;

            if (existentEnterprise.Email == enterprise.Email)
                return EnterpriseCreateResult.EmailInUse;
        }

        var dto = _mapper.Map<EnterpriseDTO>(enterprise);

        _context.Add(dto);
        await _context.SaveChangesAsync();

        return EnterpriseCreateResult.Created;
    }

    public async Task<License?> AddLicenseAsync(Guid enterpriseId, License license)
    {
        var existentLicense = await _context.Licenses
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Name == license.Name &&
                x.EnterpriseId == enterpriseId);

        if (existentLicense != null)
            return null;

        var dto = _mapper.Map<LicenseDTO>(license);
        dto.EnterpriseId = enterpriseId;

        _context.Add(dto);
        await _context.SaveChangesAsync();

        license.MaskCNPJ();

        return license;
    }

    public async Task<AccessControlDevice?> AddAccessControlDeviceAsync(Guid licenseId, AccessControlDevice device)
    {
        var existentDevice = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                (x.SerialNumber == device.SerialNumber ||
                 x.MACAddress == device.MACAddress) &&
                x.LicenseId == licenseId);

        if (existentDevice != null)
            return null;

        var dto = _mapper.Map<AccessControlDeviceDTO>(device);
        dto.LicenseId = licenseId;

        _context.Add(dto);
        await _context.SaveChangesAsync();

        return device;
    }

    public async Task<bool> RemoveAccessControlDeviceAsync(AccessControlDevice device)
    {
        var existentDevice = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == device.Id);

        if (existentDevice != null)
        {
            _context.Devices.Remove(existentDevice);
            await _context.SaveChangesAsync();
        }

        return true;
    }

    public async Task<CFTVDevice?> AddCFTVDeviceAsync(Guid licenseId, CFTVDevice device)
    {
        var existentDevice = await _context.CFTVDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.IpAddress == device.IpAddress &&
                x.HTTPPort == device.HTTPPort &&
                x.RTSPPort == device.RTSPPort &&
                x.LicenseId == licenseId);

        if (existentDevice != null)
            return null;

        var dto = _mapper.Map<CFTVDeviceDTO>(device);
        dto.LicenseId = licenseId;

        _context.Add(dto);
        await _context.SaveChangesAsync();

        return device;
    }

    public async Task<CFTVDevice?> UpdateCFTVDeviceAsync(Guid licenseId, CFTVDevice device)
    {
        var existentDevice = await _context.CFTVDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Id == device.Id &&
                x.LicenseId == licenseId);

        if (existentDevice == null)
            return null;

        var conflict = await _context.CFTVDevices
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id != device.Id &&
                x.LicenseId == licenseId &&
                x.IpAddress == device.IpAddress &&
                x.HTTPPort == device.HTTPPort &&
                x.RTSPPort == device.RTSPPort);

        if (conflict)
            return null;

        var dto = _mapper.Map<CFTVDeviceDTO>(device);
        dto.LicenseId = licenseId;

        _context.CFTVDevices.Update(dto);
        await _context.SaveChangesAsync();

        return device;
    }

    public async Task<bool> RemoveAccessCFTVDeviceAsync(CFTVDevice device)
    {
        var existentDevice = await _context.CFTVDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == device.Id);

        if (existentDevice != null)
        {
            _context.CFTVDevices.Remove(existentDevice);
            await _context.SaveChangesAsync();
        }

        return true;
    }
}
