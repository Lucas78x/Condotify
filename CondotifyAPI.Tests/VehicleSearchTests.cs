using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class VehicleSearchTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _blockId;
    private Guid _unitId;
    private Guid _residentId;
    private Guid _vehicleId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);
        _tenant.MarkUnrestricted();

        _enterpriseId = Guid.NewGuid();
        _licenseId = Guid.NewGuid();
        _blockId = Guid.NewGuid();
        _unitId = Guid.NewGuid();
        _residentId = Guid.NewGuid();
        _vehicleId = Guid.NewGuid();

        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Placa {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca placa", Code = $"PLT-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Blocks.Add(new BlockDTO { Id = _blockId, LicenseId = _licenseId, Name = "Bloco A" });
        _context.Units.Add(new UnitDTO { Id = _unitId, BlockId = _blockId, Number = "101" });
        await _context.SaveChangesAsync();

        _context.Residents.Add(new ResidentAccessDTO
        {
            Id = _residentId, UnitId = _unitId, Name = "Morador Placa", Email = $"{_residentId:N}@teste.condotify.local",
            Password = string.Empty, PhoneNumber = string.Empty, CommercialPhone = string.Empty, CPF = string.Empty, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = "101", ImgUrl = string.Empty, Description = string.Empty,
            AccessType = ResidentAccessTypeEnum.Responsible, FirstAccess = false, NotifyAccess = false, IsActive = true,
            Temporary = false, LastAccess = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, AccessCredentials = []
        });
        _context.Vehicles.Add(new VehicleDTO { Id = _vehicleId, UnitId = _unitId, ResidentId = _residentId, Plate = "ABC1D23", Brand = "Fiat", Model = "Argo", Color = "Prata", Type = "Carro", TagIdentifier = string.Empty, IsActive = true });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Vehicles.Where(x => x.Id == _vehicleId).ExecuteDelete();
        _context.Residents.Where(x => x.Id == _residentId).ExecuteDelete();
        _context.Units.Where(x => x.Id == _unitId).ExecuteDelete();
        _context.Blocks.Where(x => x.Id == _blockId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task SearchVehiclesByPlate_ReturnsMatch_WithOwnerAndUnit()
    {
        var results = await PeopleManagementController.SearchVehiclesByPlateCore(_context, _licenseId, "abc1");

        Assert.Single(results);
        Assert.Equal("ABC1D23", results[0].Plate);
        Assert.Equal("Morador Placa", results[0].ResidentName);
        Assert.Equal("Bloco A / 101", results[0].UnitLabel);
    }

    [Fact]
    public async Task SearchVehiclesByPlate_DoesNotLeakVehiclesFromOtherLicenses()
    {
        var otherLicenseId = Guid.NewGuid();
        var results = await PeopleManagementController.SearchVehiclesByPlateCore(_context, otherLicenseId, "abc1");

        Assert.Empty(results);
    }
}
