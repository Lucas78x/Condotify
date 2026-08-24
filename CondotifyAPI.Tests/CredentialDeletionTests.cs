using System.Security.Claims;
using AutoMapper;
using CondotifyAPI.Controllers;
using CondotifyAPI.Data.AccessControl;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.Invitation;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CondotifyAPI.Tests;

public sealed class CredentialDeletionTests : IAsyncLifetime
{
    private readonly Guid _enterpriseId = Guid.NewGuid();
    private readonly Guid _licenseId = Guid.NewGuid();
    private readonly Guid _blockId = Guid.NewGuid();
    private readonly Guid _unitId = Guid.NewGuid();
    private readonly Guid _hostId = Guid.NewGuid();
    private readonly Guid _guestId = Guid.NewGuid();
    private readonly Guid _credentialId = Guid.NewGuid();
    private readonly Guid _ordinaryCredentialId = Guid.NewGuid();
    private readonly Guid _visitId = Guid.NewGuid();
    private DatabaseContext _context = null!;
    private CredentialManagementController _controller = null!;

    public async Task InitializeAsync()
    {
        var tenant = new CurrentTenantAccessor();
        tenant.MarkUnrestricted();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, tenant);

        var now = DateTime.UtcNow;
        _context.Enterprises.Add(new EnterpriseDTO
        {
            Id = _enterpriseId,
            Name = $"Exclusao {_enterpriseId:N}",
            CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}",
            Email = $"{_enterpriseId:N}@teste.condotify.local"
        });
        _context.Licenses.Add(new LicenseDTO
        {
            Id = _licenseId,
            EnterpriseId = _enterpriseId,
            Name = "Licenca exclusao",
            Code = $"DEL-{_licenseId:N}"[..20],
            ExpireDate = now.AddYears(1),
            CreatedAt = now
        });
        _context.Blocks.Add(new BlockDTO
        {
            Id = _blockId,
            LicenseId = _licenseId,
            Name = "Bloco exclusao",
            CreatedAt = now,
            LastUpdatedAt = now
        });
        _context.Units.Add(new UnitDTO { Id = _unitId, BlockId = _blockId, Number = "101" });
        _context.Residents.AddRange(
            NewResident(_hostId, "Morador anfitriao", ResidentAccessTypeEnum.Responsible, now),
            NewResident(_guestId, "Visitante expirado", ResidentAccessTypeEnum.Guest, now));
        _context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
        {
            Id = _credentialId,
            ResidentId = _guestId,
            CredentialType = AccessCredentialTypeEnum.QrCode,
            Identifier = $"VIS-{_credentialId:N}",
            IsActive = false,
            IsTemporary = true,
            ValidFrom = now.AddDays(-2),
            ValidTo = now.AddDays(-1),
            CreatedAt = now.AddDays(-2)
        });
        _context.AccessVisits.Add(new AccessVisitDTO
        {
            Id = _visitId,
            LicenseId = _licenseId,
            HostResidentId = _hostId,
            GuestResidentId = _guestId,
            CredentialId = _credentialId,
            VisitorName = "Visitante expirado",
            Status = AccessVisitStatusEnum.Expired,
            ValidFrom = now.AddDays(-2),
            ValidTo = now.AddDays(-1),
            CreatedAt = now.AddDays(-2),
            UpdatedAt = now
        });
        await _context.SaveChangesAsync();

        _controller = new CredentialManagementController(
            _context,
            Mock.Of<IAccessControlService>(),
            Mock.Of<IAccessRouteResolver>(),
            Mock.Of<IMapper>(),
            NullLogger<CredentialManagementController>.Instance,
            Mock.Of<IPrivateMediaStore>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim("enterprise_id", _enterpriseId.ToString())], "test"))
                }
            }
        };
    }

    [Fact]
    public async Task DeleteCredential_WithVisitHistory_ArchivesCredentialAndKeepsVisit()
    {
        var result = await _controller.DeleteCredential(_licenseId, _credentialId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var output = Assert.IsType<CredentialOperationOut>(ok.Value);
        Assert.True(output.Success);
        Assert.Contains("historico da visita", output.Message, StringComparison.OrdinalIgnoreCase);

        _context.ChangeTracker.Clear();
        var credential = await _context.ResidentAccessCredentials
            .SingleAsync(x => x.Id == _credentialId);
        Assert.False(credential.IsActive);
        Assert.NotNull(credential.ArchivedAt);
        Assert.True(await _context.AccessVisits.AnyAsync(x => x.Id == _visitId && x.CredentialId == _credentialId));

        var listResult = Assert.IsType<OkObjectResult>(await _controller.GetCredentials(_licenseId));
        Assert.Empty(Assert.IsType<List<CredentialOut>>(listResult.Value));
    }

    [Fact]
    public async Task DeleteCredential_WithoutVisitHistory_RemovesCredentialPermanently()
    {
        var now = DateTime.UtcNow;
        _context.ResidentAccessCredentials.Add(new ResidentAccessCredentialDTO
        {
            Id = _ordinaryCredentialId,
            ResidentId = _hostId,
            CredentialType = AccessCredentialTypeEnum.Card,
            Identifier = $"CARD-{_ordinaryCredentialId:N}",
            IsActive = false,
            ValidFrom = now.AddDays(-2),
            ValidTo = now.AddDays(-1),
            CreatedAt = now.AddDays(-2)
        });
        await _context.SaveChangesAsync();

        var result = await _controller.DeleteCredential(_licenseId, _ordinaryCredentialId);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True(Assert.IsType<CredentialOperationOut>(ok.Value).Success);
        Assert.False(await _context.ResidentAccessCredentials.AnyAsync(x => x.Id == _ordinaryCredentialId));
    }

    public async Task DisposeAsync()
    {
        await _context.AccessVisits.IgnoreQueryFilters().Where(x => x.Id == _visitId).ExecuteDeleteAsync();
        await _context.ResidentAccessCredentials.IgnoreQueryFilters()
            .Where(x => x.Id == _credentialId || x.Id == _ordinaryCredentialId)
            .ExecuteDeleteAsync();
        await _context.Residents.IgnoreQueryFilters().Where(x => x.Id == _hostId || x.Id == _guestId).ExecuteDeleteAsync();
        await _context.Units.IgnoreQueryFilters().Where(x => x.Id == _unitId).ExecuteDeleteAsync();
        await _context.Blocks.IgnoreQueryFilters().Where(x => x.Id == _blockId).ExecuteDeleteAsync();
        await _context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _licenseId).ExecuteDeleteAsync();
        await _context.Enterprises.IgnoreQueryFilters().Where(x => x.Id == _enterpriseId).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }

    private ResidentAccessDTO NewResident(Guid id, string name, ResidentAccessTypeEnum type, DateTime now) => new()
    {
        Id = id,
        UnitId = _unitId,
        Name = name,
        AccessType = type,
        IsActive = true,
        Temporary = type == ResidentAccessTypeEnum.Guest,
        Expire = now.AddYears(1),
        LastAccess = now,
        CreatedAt = now
    };
}
