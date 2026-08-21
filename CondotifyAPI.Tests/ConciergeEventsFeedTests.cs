using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class ConciergeEventsFeedTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _deviceId;
    private Guid? _photoCredentialId;
    private Guid? _photoResidentId;
    private Guid? _photoUnitId;
    private Guid? _photoBlockId;

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
        _deviceId = Guid.NewGuid();
        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Eventos {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca eventos", Code = $"EV-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Devices.Add(new AccessControlDeviceDTO { Id = _deviceId, LicenseId = _licenseId, Name = "Portaria principal", Model = "Teste", IsActive = true });
        await _context.SaveChangesAsync();

        _context.AccessEventRecords.Add(new AccessEventRecordDTO { Id = Guid.NewGuid(), LicenseId = _licenseId, DeviceId = _deviceId, ExternalEventId = "1", Event = "Entrada", Authorized = true, OccurredAt = DateTime.UtcNow, PersonName = "Joao Autorizado", CreatedAt = DateTime.UtcNow });
        _context.AccessEventRecords.Add(new AccessEventRecordDTO { Id = Guid.NewGuid(), LicenseId = _licenseId, DeviceId = _deviceId, ExternalEventId = "2", Event = "Negado", Authorized = false, OccurredAt = DateTime.UtcNow, PersonName = "Maria Negada", CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.AccessEventRecords.Where(x => x.LicenseId == _licenseId).ExecuteDelete();
        if (_photoCredentialId.HasValue)
            _context.ResidentAccessCredentials.Where(x => x.Id == _photoCredentialId).ExecuteDelete();
        if (_photoResidentId.HasValue)
            _context.Residents.Where(x => x.Id == _photoResidentId).ExecuteDelete();
        if (_photoUnitId.HasValue)
            _context.Units.Where(x => x.Id == _photoUnitId).ExecuteDelete();
        if (_photoBlockId.HasValue)
            _context.Blocks.Where(x => x.Id == _photoBlockId).ExecuteDelete();
        _context.Devices.Where(x => x.Id == _deviceId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetEventsFeedCore_WithoutFilter_ReturnsAllCombinedAcrossDevices()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, null, null, 50);

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task GetEventsFeedCore_WithSearch_FiltersByPersonName()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, "joao", null, 50);

        Assert.Single(events);
        Assert.Equal("Joao Autorizado", events[0].PersonName);
    }

    [Fact]
    public async Task GetEventsFeedCore_WithResultFilter_FiltersByAuthorized()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, null, false, 50);

        Assert.Single(events);
        Assert.Equal("Maria Negada", events[0].PersonName);
    }

    [Fact]
    public async Task GetEventsFeedCore_WithLinkedCredential_ReturnsResidentPhoto()
    {
        _photoBlockId = Guid.NewGuid();
        _photoUnitId = Guid.NewGuid();
        _photoResidentId = Guid.NewGuid();
        _photoCredentialId = Guid.NewGuid();
        var photoUrl = $"/private-media/{_licenseId:D}/{Guid.NewGuid():D}";
        var now = DateTime.UtcNow;

        _context.Blocks.Add(new BlockDTO
        {
            Id = _photoBlockId.Value,
            LicenseId = _licenseId,
            Name = "Bloco foto",
            CreatedAt = now,
            LastUpdatedAt = now
        });
        _context.Units.Add(new UnitDTO
        {
            Id = _photoUnitId.Value,
            BlockId = _photoBlockId.Value,
            Number = "101"
        });
        _context.Residents.Add(new ResidentAccessDTO
        {
            Id = _photoResidentId.Value,
            UnitId = _photoUnitId.Value,
            Name = "Pessoa Com Foto",
            ImgUrl = photoUrl,
            AccessType = ResidentAccessTypeEnum.Responsible,
            Expire = now.AddYears(1),
            LastAccess = now,
            CreatedAt = now
        });
        _context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
        {
            Id = _photoCredentialId.Value,
            ResidentId = _photoResidentId.Value,
            CredentialType = AccessCredentialTypeEnum.Face,
            Identifier = $"FACE-{_photoResidentId:N}",
            IsActive = true,
            ValidFrom = now.AddDays(-1),
            ValidTo = now.AddYears(1),
            CreatedAt = now
        });
        _context.AccessEventRecords.Add(new AccessEventRecordDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = _licenseId,
            DeviceId = _deviceId,
            CredentialId = _photoCredentialId.Value,
            ExternalEventId = "photo-event",
            Event = "Entrada",
            Authorized = true,
            OccurredAt = now,
            PersonName = "Pessoa Com Foto",
            CreatedAt = now
        });
        await _context.SaveChangesAsync();

        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, "Pessoa Com Foto", null, 50);

        var accessEvent = Assert.Single(events);
        Assert.Equal(photoUrl, accessEvent.PhotoUrl);
    }
}
