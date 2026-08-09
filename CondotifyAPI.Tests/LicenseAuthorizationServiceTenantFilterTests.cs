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
    private Guid _nonAdminUserId;
    private Guid _licenseUserAccessId;

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
        _nonAdminUserId = Guid.NewGuid();
        _licenseUserAccessId = Guid.NewGuid();

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
        _context.Users.Add(new UserAccessDTO
        {
            Id = _nonAdminUserId,
            EnterpriseId = _enterpriseId,
            AccessType = AccessTypeEnum.Viewer,
            Name = "Usuario Nao-Admin Teste Circularidade",
            Email = $"{_nonAdminUserId:N}@teste.condotify.local"
        });
        await _context.SaveChangesAsync();

        // Concede acesso explicito a licenca via LicenseUserAccessDTO -- a
        // UNICA entidade tocada por LicenseAuthorizationService que de fato
        // implementa ILicenseScoped e portanto e sujeita ao filtro global.
        _context.LicenseUserAccesses.Add(new LicenseUserAccessDTO
        {
            Id = _licenseUserAccessId,
            LicenseId = _licenseId,
            UserId = _nonAdminUserId,
            Role = LicenseAccessRoleEnum.Viewer,
            Permissions = LicensePermissionEnum.ViewDashboard,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.LicenseUserAccesses.RemoveRange(_context.LicenseUserAccesses.IgnoreQueryFilters().Where(x => x.Id == _licenseUserAccessId));
        await _context.SaveChangesAsync();
        _context.Licenses.RemoveRange(_context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _licenseId));
        _context.Users.RemoveRange(_context.Users.Where(x => x.Id == _userId || x.Id == _nonAdminUserId));
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

    private ClaimsPrincipal NonAdminPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, _nonAdminUserId.ToString()),
        new Claim("enterprise_id", _enterpriseId.ToString()),
        new Claim("principal_type", "user")
    ], "TestAuth"));

    [Fact]
    public async Task GetLicensePermissionsAsync_ReturnsLicense_EvenWhenAccessorScopeIsEmpty()
    {
        // Simula o estado ANTES do TenantScopePopulationMiddleware rodar: nada
        // foi computado ainda, e e exatamente este metodo que precisa computar.
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

    [Fact]
    public async Task GetLicensePermissionsAsync_NonAdmin_ReturnsLicense_EvenWhenAccessorScopeIsEmpty()
    {
        // Ao contrario dos dois testes acima (principal Admin), este usuario
        // nao-admin forca GetLicensePermissionsAsync a cair no branch que
        // consulta _context.LicenseUserAccesses -- a UNICA consulta neste
        // service que efetivamente sofre o filtro global (LicenseUserAccessDTO
        // implementa ILicenseScoped; LicenseDTO e UserAccessDTO nao). Sem o
        // .IgnoreQueryFilters() naquela linha especifica, este teste falha.
        _tenant.SetAccessibleScope([], null);

        var authService = new LicenseAuthorizationService(_context);
        var permissions = await authService.GetLicensePermissionsAsync(NonAdminPrincipal());

        Assert.True(permissions.ContainsKey(_licenseId), "GetLicensePermissionsAsync (branch LicenseUserAccesses) nao encontrou a licenca -- o filtro global esta escondendo a propria consulta que calcula o conjunto acessivel (circularidade nao quebrada).");
    }
}
