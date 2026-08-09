using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class TenantIsolationIntegrationTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _accessibleLicenseId;
    private Guid _inaccessibleLicenseId;
    private Guid _accessibleDeliveryId;
    private Guid _inaccessibleDeliveryId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);

        _enterpriseId = Guid.NewGuid();
        _accessibleLicenseId = Guid.NewGuid();
        _inaccessibleLicenseId = Guid.NewGuid();
        _accessibleDeliveryId = Guid.NewGuid();
        _inaccessibleDeliveryId = Guid.NewGuid();

        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Isolamento {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _accessibleLicenseId, EnterpriseId = _enterpriseId, Name = "Acessivel", Code = $"ACC-{_accessibleLicenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Licenses.Add(new LicenseDTO { Id = _inaccessibleLicenseId, EnterpriseId = _enterpriseId, Name = "Inacessivel", Code = $"INA-{_inaccessibleLicenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();

        _context.Deliveries.Add(new DeliveryDTO { Id = _accessibleDeliveryId, LicenseId = _accessibleLicenseId, Name = "Encomenda acessivel", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        _context.Deliveries.Add(new DeliveryDTO { Id = _inaccessibleDeliveryId, LicenseId = _inaccessibleLicenseId, Name = "Encomenda inacessivel", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Deliveries.IgnoreQueryFilters().Where(x => x.Id == _accessibleDeliveryId || x.Id == _inaccessibleDeliveryId).ExecuteDelete();
        _context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _accessibleLicenseId || x.Id == _inaccessibleLicenseId).ExecuteDelete();
        _context.Enterprises.IgnoreQueryFilters().Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task UnfilteredQuery_OnlyReturnsDeliveriesFromAccessibleLicense()
    {
        _tenant.SetAccessibleScope([_accessibleLicenseId], _enterpriseId);

        // Simula um controller que "esqueceu" de filtrar por licenseId --
        // exatamente o cenario que este subsistema existe para proteger.
        var visible = await _context.Deliveries
            .Where(x => x.Id == _accessibleDeliveryId || x.Id == _inaccessibleDeliveryId)
            .ToListAsync();

        var visibleIds = visible.Select(x => x.Id).ToHashSet();
        Assert.Contains(_accessibleDeliveryId, visibleIds);
        Assert.DoesNotContain(_inaccessibleDeliveryId, visibleIds);
    }

    [Fact]
    public async Task ExplicitLicenseFilter_StillWorksTogetherWithGlobalFilter()
    {
        // O global filter e um SUPERCONJUNTO -- uma query que ja filtra
        // explicitamente por uma licenca especifica dentro do conjunto
        // acessivel nao deve se comportar diferente com o filtro global
        // ativo.
        _tenant.SetAccessibleScope([_accessibleLicenseId, _inaccessibleLicenseId], _enterpriseId);

        var result = await _context.Deliveries
            .Where(x => x.LicenseId == _accessibleLicenseId && x.Id == _accessibleDeliveryId)
            .ToListAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task MultiLicenseHashSetQuery_MatchesDashboardPattern_ReturnsBothWhenBothAreAccessible()
    {
        // OperationsController.GetDashboard filtra com um HashSet<Guid> de
        // licencas acessiveis por permissao especifica (ex.: ViewDeliveries),
        // nao uma unica licenca -- exatamente o padrao que a auditoria
        // original teria quebrado com um filtro de "uma licenca por
        // requisicao". Aqui o conjunto explicito da query (deliveryLicenseIds)
        // e IGUAL ao conjunto do accessor global -- confirma que o filtro
        // global (superconjunto ou igual) nao esconde nada que a query
        // explicita ja pretendia mostrar.
        _tenant.SetAccessibleScope([_accessibleLicenseId, _inaccessibleLicenseId], _enterpriseId);
        var deliveryLicenseIds = new HashSet<Guid> { _accessibleLicenseId, _inaccessibleLicenseId };

        var result = await _context.Deliveries
            .Where(x => deliveryLicenseIds.Contains(x.LicenseId) && (x.Id == _accessibleDeliveryId || x.Id == _inaccessibleDeliveryId))
            .ToListAsync();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NullAccessorScope_HidesEverything()
    {
        // _tenant nunca teve SetAccessibleScope chamado -- estado inicial.
        var visible = await _context.Deliveries
            .Where(x => x.Id == _accessibleDeliveryId)
            .ToListAsync();

        Assert.Empty(visible);
    }

    [Fact]
    public async Task UnrestrictedAccessor_SeesAllLicensesRegardlessOfAccessibleSet()
    {
        // Simula um worker: nunca chama SetAccessibleScope, so MarkUnrestricted.
        _tenant.MarkUnrestricted();

        var visible = await _context.Deliveries
            .Where(x => x.Id == _accessibleDeliveryId || x.Id == _inaccessibleDeliveryId)
            .ToListAsync();

        var visibleIds = visible.Select(x => x.Id).ToHashSet();
        Assert.Contains(_accessibleDeliveryId, visibleIds);
        Assert.Contains(_inaccessibleDeliveryId, visibleIds);
    }
}
