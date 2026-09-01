using System.Security.Claims;
using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Operations;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace CondotifyAPI.Tests;

public sealed class GlobalSearchVehicleTests : IAsyncLifetime
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

        _context.Enterprises.Add(new EnterpriseDTO
        {
            Id = _enterpriseId,
            Name = $"Pesquisa global {_enterpriseId:N}",
            CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}",
            Email = $"{_enterpriseId:N}@teste.condotify.local"
        });
        _context.Licenses.Add(new LicenseDTO
        {
            Id = _licenseId,
            EnterpriseId = _enterpriseId,
            Name = "Licenca pesquisa global",
            Code = $"GLB-{_licenseId:N}"[..20],
            ExpireDate = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow
        });
        _context.Blocks.Add(new BlockDTO { Id = _blockId, LicenseId = _licenseId, Name = "Bloco Busca" });
        _context.Units.Add(new UnitDTO { Id = _unitId, BlockId = _blockId, Number = "203" });
        await _context.SaveChangesAsync();

        _context.Residents.Add(new ResidentAccessDTO
        {
            Id = _residentId,
            UnitId = _unitId,
            Name = "Pessoa Veiculo",
            Email = $"{_residentId:N}@teste.condotify.local",
            Password = string.Empty,
            PhoneNumber = string.Empty,
            CommercialPhone = string.Empty,
            CPF = string.Empty,
            RG = string.Empty,
            BirthDate = string.Empty,
            ApartmentNumber = "203",
            ImgUrl = string.Empty,
            Description = string.Empty,
            AccessType = ResidentAccessTypeEnum.Responsible,
            FirstAccess = false,
            NotifyAccess = false,
            IsActive = true,
            Temporary = false,
            LastAccess = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            AccessCredentials = []
        });
        _context.Vehicles.Add(new VehicleDTO
        {
            Id = _vehicleId,
            UnitId = _unitId,
            ResidentId = _residentId,
            Plate = "QRS4T56",
            Brand = "Honda",
            Model = "City",
            Color = "Prata",
            Type = "Carro",
            TagIdentifier = string.Empty,
            IsActive = true
        });
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
    public async Task SearchResidents_WithVehiclePermission_ReturnsMatchingPlate()
    {
        var controller = CreateController(canViewVehicles: true);

        var response = await controller.SearchResidents(
            query: null,
            document: null,
            phone: null,
            credential: null,
            unit: null,
            licenseId: null,
            vehiclePlate: "qrs4",
            take: 50);

        var result = Assert.IsType<OkObjectResult>(response);
        var residents = Assert.IsAssignableFrom<IEnumerable<GlobalResidentSearchOut>>(result.Value).ToList();
        var resident = Assert.Single(residents);
        var vehicle = Assert.Single(resident.Vehicles);
        Assert.Equal("QRS4T56", vehicle.Plate);
        Assert.Equal("Honda", vehicle.Brand);
        Assert.Equal("City", vehicle.Model);
    }

    [Fact]
    public async Task SearchResidents_WithoutVehiclePermission_DoesNotReturnPlateMatch()
    {
        var controller = CreateController(canViewVehicles: false);

        var response = await controller.SearchResidents(
            query: null,
            document: null,
            phone: null,
            credential: null,
            unit: null,
            licenseId: null,
            vehiclePlate: "qrs4",
            take: 50);

        var result = Assert.IsType<OkObjectResult>(response);
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<GlobalResidentSearchOut>>(result.Value));
    }

    [Fact]
    public async Task SearchResidents_WithoutPlate_DoesNotExposeVehicles()
    {
        var controller = CreateController(canViewVehicles: false);

        var response = await controller.SearchResidents(
            query: "Pessoa Veiculo",
            document: null,
            phone: null,
            credential: null,
            unit: null,
            licenseId: null,
            vehiclePlate: null,
            take: 50);

        var result = Assert.IsType<OkObjectResult>(response);
        var resident = Assert.Single(Assert.IsAssignableFrom<IEnumerable<GlobalResidentSearchOut>>(result.Value));
        Assert.Empty(resident.Vehicles);
    }

    private OperationsController CreateController(bool canViewVehicles)
    {
        var authorization = new Mock<ILicenseAuthorizationService>();
        authorization
            .Setup(x => x.GetLicenseIdsWithPermissionAsync(
                It.IsAny<ClaimsPrincipal>(),
                LicensePermissionEnum.ViewPeople,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<Guid> { _licenseId });
        authorization
            .Setup(x => x.GetLicenseIdsWithPermissionAsync(
                It.IsAny<ClaimsPrincipal>(),
                LicensePermissionEnum.ViewVehicles,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(canViewVehicles ? new HashSet<Guid> { _licenseId } : []);

        var controller = new OperationsController(_context, authorization.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("enterprise_id", _enterpriseId.ToString())],
                        "test"))
                }
            }
        };
        return controller;
    }
}
