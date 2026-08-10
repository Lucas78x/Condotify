using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class AnnouncementsControllerTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _otherLicenseId;

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
        _otherLicenseId = Guid.NewGuid();
        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Comunicados {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca comunicados", Code = $"AN-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Licenses.Add(new LicenseDTO { Id = _otherLicenseId, EnterpriseId = _enterpriseId, Name = "Outra licenca", Code = $"AN2-{_otherLicenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Announcements.IgnoreQueryFilters().Where(x => x.LicenseId == _licenseId || x.LicenseId == _otherLicenseId).ExecuteDelete();
        _context.Licenses.IgnoreQueryFilters().Where(x => x.Id == _licenseId || x.Id == _otherLicenseId).ExecuteDelete();
        _context.Enterprises.IgnoreQueryFilters().Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task CreateAnnouncementCore_PersistsWithCorrectFields()
    {
        var input = new CreateAnnouncementIn { Title = "Manutencao da piscina", Body = "A piscina ficara fechada na sexta-feira.", IsUrgent = true };

        var announcement = AnnouncementsController.CreateAnnouncementCore(_licenseId, input, "Sindico Teste");
        _context.Announcements.Add(announcement);
        await _context.SaveChangesAsync();

        var saved = await _context.Announcements.FirstOrDefaultAsync(x => x.Id == announcement.Id);
        Assert.NotNull(saved);
        Assert.Equal("Manutencao da piscina", saved!.Title);
        Assert.True(saved.IsUrgent);
        Assert.Equal("Sindico Teste", saved.CreatedBy);
    }

    [Fact]
    public async Task ListAnnouncementsCore_DoesNotLeakAcrossLicenses()
    {
        _context.Announcements.Add(AnnouncementsController.CreateAnnouncementCore(_licenseId, new CreateAnnouncementIn { Title = "Da licenca certa", Body = "Corpo", IsUrgent = false }, "Autor"));
        _context.Announcements.Add(AnnouncementsController.CreateAnnouncementCore(_otherLicenseId, new CreateAnnouncementIn { Title = "De outra licenca", Body = "Corpo", IsUrgent = false }, "Autor"));
        await _context.SaveChangesAsync();

        var results = await AnnouncementsController.ListAnnouncementsCore(_context, _licenseId);

        Assert.Single(results);
        Assert.Equal("Da licenca certa", results[0].Title);
    }
}
