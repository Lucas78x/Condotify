using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

// Prova que LicenseAuthorizationService.GetGrantAsync funciona mesmo quando chamado
// ANTES de qualquer populacao do ICurrentTenantAccessor -- exatamente o que acontece
// quando RequireLicensePermissionAttribute (um IAsyncAuthorizationFilter) roda, porque
// filtros de Authorization do MVC rodam antes de qualquer Action filter (o defeito
// original da Task 5, corrigido estruturalmente na Task 7 movendo a populacao do escopo
// para middleware). Ver Task 7 do plano de filtro de tenant.
public sealed class RequireLicensePermissionCrossFilterStageTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _nonAdminUserId;

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
        _nonAdminUserId = Guid.NewGuid();

        _context.Enterprises.Add(new EnterpriseDTO
        {
            Id = _enterpriseId,
            Name = $"Teste crossfilter {_enterpriseId:N}",
            CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}",
            Email = $"{_enterpriseId:N}@teste.condotify.local"
        });
        _context.Licenses.Add(new LicenseDTO
        {
            Id = _licenseId,
            EnterpriseId = _enterpriseId,
            Name = "Licenca crossfilter",
            Code = $"CF-{_licenseId:N}"[..20],
            ExpireDate = DateTime.UtcNow.AddYears(1),
            CreatedAt = DateTime.UtcNow
        });
        _context.Users.Add(new UserAccessDTO
        {
            Id = _nonAdminUserId,
            EnterpriseId = _enterpriseId,
            AccessType = AccessTypeEnum.Viewer,
            Name = "Usuario crossfilter",
            Email = $"{_nonAdminUserId:N}@teste.condotify.local"
        });
        await _context.SaveChangesAsync();

        _context.LicenseUserAccesses.Add(new LicenseUserAccessDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = _licenseId,
            UserId = _nonAdminUserId,
            IsActive = true,
            // O brief da Task 7 citava "Staff", que nao existe em LicenseAccessRoleEnum
            // (Administrator/Manager/Concierge/Operator/Viewer). Concierge e um papel
            // nao-admin, que e o que este teste precisa.
            Role = LicenseAccessRoleEnum.Concierge,
            Permissions = LicensePermissionEnum.ViewDeliveries,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.LicenseUserAccesses.IgnoreQueryFilters().Where(x => x.UserId == _nonAdminUserId).ExecuteDeleteAsync();
        await _context.Users.Where(x => x.Id == _nonAdminUserId).ExecuteDeleteAsync();
        await _context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _licenseId).ExecuteDeleteAsync();
        await _context.Enterprises.IgnoreQueryFilters().Where(x => x.Id == _enterpriseId).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }

    private ClaimsPrincipal NonAdminPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, _nonAdminUserId.ToString()),
        new Claim("enterprise_id", _enterpriseId.ToString()),
        new Claim("principal_type", "user")
    ], "TestAuth"));

    [Fact]
    public async Task GetGrantAsync_ForNonAdminUser_WorksBeforeAccessorIsEverPopulated()
    {
        // _tenant.SetAccessibleScope nunca foi chamado -- simula LicensePermissionFilter
        // rodando como authorization filter, antes de qualquer middleware/action filter.
        Assert.Null(_tenant.AccessibleLicenseIds);

        var authService = new LicenseAuthorizationService(_context);

        var grant = await authService.GetGrantAsync(NonAdminPrincipal(), _licenseId);

        Assert.NotNull(grant);
        Assert.Equal(_licenseId, grant!.LicenseId);
        Assert.True(grant.Has(LicensePermissionEnum.ViewDeliveries));
    }
}
