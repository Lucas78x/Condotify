using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Deliveries;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CondotifyAPI.Tests;

public sealed class DeliveryPhotoTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;

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
        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Foto entrega {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca foto entrega", Code = $"FT-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Deliveries.Where(x => x.LicenseId == _licenseId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
        if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, recursive: true);
    }

    private const string OnePixelPngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"condotify-media-tests-{Guid.NewGuid():N}");

    private PrivateMediaStore BuildMediaStore()
    {
        Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", "test-media-secret-with-enough-entropy");
        Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", _mediaRoot);
        return new PrivateMediaStore(new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task CreateDeliveryCore_WithPhotoBase64_StoresPhotoUrlViaMediaStore()
    {
        var media = BuildMediaStore();
        var input = new CreateDeliveryIn { Name = "Encomenda com foto", PhotoBase64 = OnePixelPngDataUri };

        var delivery = await LicenseStructureController.CreateDeliveryCore(_context, media, _licenseId, input, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(delivery.PhotoUrl));
        Assert.NotEqual(OnePixelPngDataUri, delivery.PhotoUrl);
    }

    [Fact]
    public async Task CreateDeliveryCore_WithoutPhoto_LeavesPhotoUrlEmpty()
    {
        var media = BuildMediaStore();
        var input = new CreateDeliveryIn { Name = "Encomenda sem foto" };

        var delivery = await LicenseStructureController.CreateDeliveryCore(_context, media, _licenseId, input, CancellationToken.None);

        Assert.Equal(string.Empty, delivery.PhotoUrl);
    }
}
