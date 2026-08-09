using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class LicenseAuthorizationServiceTenantFilterTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);

        _enterpriseId = Guid.NewGuid();
        _licenseId = Guid.NewGuid();
        _userId = Guid.NewGuid();

        _context.Enterprises.Add(new EnterpriseDTO
        {
            Id = _enterpriseId,
            Name = $"Teste circularidade {_enterpriseId:N}",
            CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}",
            Email = $"{_enterpriseId:N}@teste.condotify.local"
        });
        _context.Licenses.Add(new LicenseDTO
        {
            Id = _licenseId,
            EnterpriseId = _enterpriseId,
            Name = $"Licenca circularidade {_licenseId:N}",
            Code = $"CIRC-{_licenseId:N}"[..20],
            ExpireDate = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow
        });
        _context.Users.Add(new UserAccessDTO
        {
            Id = _userId,
            EnterpriseId = _enterpriseId,
            AccessType = AccessTypeEnum.Admin,
            Name = "Usuario Teste Circularidade",
            Email = $"{_userId:N}@teste.condotify.local"
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Licenses.RemoveRange(_context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _licenseId));
        _context.Users.RemoveRange(_context.Users.Where(x => x.Id == _userId));
        _context.Enterprises.RemoveRange(_context.Enterprises.Where(x => x.Id == _enterpriseId));
        await _context.SaveChangesAsync();
        await _context.DisposeAsync();
    }

    private ClaimsPrincipal AdminPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, _userId.ToString()),
        new Claim("enterprise_id", _enterpriseId.ToString()),
        new Claim("principal_type", "user")
    ], "TestAuth"));

    [Fact]
    public async Task GetLicensePermissionsAsync_ReturnsLicense_EvenWhenAccessorScopeIsEmpty()
    {
        // Simula o estado ANTES do TenantScopeActionFilter rodar: nada foi
        // computado ainda, e e exatamente este metodo que precisa computar.
        _tenant.SetAccessibleScope([], null);

        var authService = new LicenseAuthorizationService(_context);
        var permissions = await authService.GetLicensePermissionsAsync(AdminPrincipal());

        Assert.True(permissions.ContainsKey(_licenseId), "GetLicensePermissionsAsync nao encontrou a licenca -- o filtro global provavelmente esta escondendo a propria consulta que calcula o conjunto acessivel (circularidade nao quebrada).");
    }

    [Fact]
    public async Task GetGrantAsync_ReturnsGrant_EvenWhenAccessorScopeIsEmpty()
    {
        _tenant.SetAccessibleScope([], null);

        var authService = new LicenseAuthorizationService(_context);
        var grant = await authService.GetGrantAsync(AdminPrincipal(), _licenseId);

        Assert.NotNull(grant);
        Assert.Equal(_licenseId, grant!.LicenseId);
    }
}
