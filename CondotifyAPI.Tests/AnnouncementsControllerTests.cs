using System.Security.Claims;
using CondotifyAPI.Controllers;
using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Jwt;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CondotifyAPI.Tests;

public sealed class AnnouncementsControllerTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _otherLicenseId;
    private Guid _blockId;
    private Guid _unitId;
    private Guid _residentId;

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

        _blockId = Guid.NewGuid();
        _unitId = Guid.NewGuid();
        _residentId = Guid.NewGuid();
        _context.Blocks.Add(new BlockDTO { Id = _blockId, LicenseId = _licenseId, Name = "Bloco Comunicados" });
        _context.Units.Add(new UnitDTO { Id = _unitId, BlockId = _blockId, Number = "101" });
        await _context.SaveChangesAsync();

        _context.Residents.Add(new ResidentAccessDTO
        {
            Id = _residentId, UnitId = _unitId, Name = "Morador Comunicados", Email = $"{_residentId:N}@teste.condotify.local",
            Password = string.Empty, PhoneNumber = string.Empty, CommercialPhone = string.Empty, CPF = string.Empty, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = "101", ImgUrl = string.Empty, Description = string.Empty,
            AccessType = ResidentAccessTypeEnum.Responsible, FirstAccess = false, NotifyAccess = false, IsActive = true,
            Temporary = false, LastAccess = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, AccessCredentials = []
        });
        await _context.SaveChangesAsync();

        _context.ResidentUnitLinks.Add(new ResidentUnitLinkDTO
        {
            Id = Guid.NewGuid(), ResidentId = _residentId, UnitId = _unitId,
            Relationship = ResidentUnitRelationshipEnum.OwnerResponsible, Description = string.Empty,
            IsPrimary = true, IsActive = true, StartsAt = DateTime.UtcNow.AddDays(-30), EndsAt = null,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.PushNotifications.IgnoreQueryFilters().Where(x => x.SubjectId == _residentId).ExecuteDelete();
        _context.Announcements.IgnoreQueryFilters().Where(x => x.LicenseId == _licenseId || x.LicenseId == _otherLicenseId).ExecuteDelete();
        _context.ResidentUnitLinks.IgnoreQueryFilters().Where(x => x.ResidentId == _residentId).ExecuteDelete();
        _context.Residents.IgnoreQueryFilters().Where(x => x.Id == _residentId).ExecuteDelete();
        _context.Units.IgnoreQueryFilters().Where(x => x.Id == _unitId).ExecuteDelete();
        _context.Blocks.IgnoreQueryFilters().Where(x => x.Id == _blockId).ExecuteDelete();
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

    // Regressao: a rota "/comunicados" precisa estar na allowlist de MobileDeepLinks.
    // Sem ela, PushNotificationService.Validate lanca ArgumentException, o
    // PlatformPushNotifier engole a excecao com um simples log de warning e NENHUM
    // push/notificacao in-app e gravado - silenciosamente, sem falhar o request.
    [Fact]
    public async Task Create_EnqueuesAnnouncementPushForResidentWithCurrentLink()
    {
        var notifier = new PlatformPushNotifier(
            _context,
            new PushNotificationService(_context),
            NullLogger<PlatformPushNotifier>.Instance);
        var controller = new AnnouncementsController(_context, notifier)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("name", "Sindico Push")], "Test"))
                }
            }
        };
        var input = new CreateAnnouncementIn { Title = "Assembleia geral", Body = "Sera na quinta-feira as 19h.", IsUrgent = false };

        var response = await controller.Create(_licenseId, input, CancellationToken.None);

        Assert.IsType<CreatedResult>(response);
        var announcement = await _context.Announcements.AsNoTracking().SingleAsync(x => x.LicenseId == _licenseId);
        var notification = await _context.PushNotifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SubjectId == _residentId && x.DeduplicationKey == $"announcement-published:{announcement.Id:N}");

        Assert.NotNull(notification);
        Assert.Equal(MobileNotificationCategory.Announcement, notification!.Category);
        Assert.Equal(PrincipalTypes.Resident, notification.SubjectType);
        Assert.Equal("/comunicados", notification.Route);
        Assert.Equal("https://app.condotify.com.br/app/comunicados", notification.DeepLink);
        Assert.Equal("Novo comunicado: Assembleia geral", notification.Title);
    }

    [Fact]
    public void MobileDeepLinks_AllowsComunicadosRoute()
    {
        Assert.True(Condotify.Models.MobileDeepLinks.TryNormalize("/comunicados", out var route));
        Assert.Equal("/comunicados", route);
    }
}
