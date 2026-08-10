# Comunicados (real) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir o MVP de "Comunicados" — um board de avisos simples (título, corpo, urgência) publicado pela equipe no portal e lido pelos moradores no app mobile, com push notification na publicação.

**Architecture:** .NET 8 — `CondotifyAPI` (controller novo `AnnouncementsController` + endpoint `api/resident/announcements`), `CondotifyAPI.Infrastructure` (entidade `AnnouncementDTO`, `ILicenseScoped`, migration), `Condotify` (Blazor Server, aba nova em `LicenseWorkspace.razor`), `Condotify.Mobile` (página nova `Comunicados.razor`), `Condotify.Contracts`/`Condotify.ApiClient` (tipos e métodos compartilhados).

**Tech Stack:** ASP.NET Core Web API + EF Core (Npgsql), Blazor Server + MudBlazor 9.7.0, MAUI Blazor Hybrid, xUnit para testes de backend.

## Global Constraints

- Fora de escopo (não implementar): segmentação por bloco/unidade/perfil, confirmação de leitura, anexos/imagens/links estruturados, diferenciação de push por urgência, paginação, tela dedicada na navegação principal do mobile (entra no menu "Mais").
- Exclusão é definitiva (hard delete), **não** via `IRecycleBinService` — esse serviço é um conjunto fechado de métodos por tipo de entidade (`CaptureBlock`/`CaptureUnit`/`CaptureResident`/`CaptureVehicle`/`CaptureDevice`), sem suporte genérico; integrar Comunicados a ele fica para quando a lixeira for generalizada (item futuro da lista de auditoria/lixeira).
- Todo comunicado é visível para todos os moradores ativos da licença — sem checagem de permissão granular do lado do morador (mesma postura de Documentos/Encomendas).
- Publicar/editar/apagar exige a permissão `ManageAnnouncements` (equipe) — sem uma permissão de "visualizar" separada.
- Toda publicação dispara push via `IPlatformPushNotifier.NotifyLicenseUsersAsync`, categoria `MobileNotificationCategory.Announcement`, para todo morador com vínculo ativo na licença (mesmo fan-out que `ResourceDocumentsController.cs` já faz para Documentos) — best-effort, falha no push não desfaz a criação.
- Seguir os padrões já estabelecidos: `RequireLicensePermission` + a checagem de acesso já usada pelos outros controllers, `ILicenseScoped` + filtro global de tenant (reflection loop em `DatabaseContext.OnModelCreating`), bit aditivo em `LicenseModuleEnum`/`LicensePermissionEnum` (mesmo padrão do subsistema de feature flags), `MudAutocomplete`/diálogos MudBlazor consistentes com o resto do portal, `MobilePullToRefreshState` para a lista mobile.
- Todo teste de integração de backend usa a mesma convenção já em uso: `Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres` via `CONDOTIFY_DB_CONNECTION`, container `condotify-postgres`.
- Sem testes de UI automatizados (sem bUnit neste projeto) — verificação de frontend é manual.

---

## Task 1: Modelo, permissões e migration

**Files:**
- Create: `CondotifyAPI.Domain/DTO/Announcements/AnnouncementDTO.cs`
- Create: `CondotifyAPI.Infrastructure/ContextConfiguration/Announcements/AnnouncementConfiguration.cs`
- Create: `CondotifyAPI.Infrastructure/DatabaseContext/Announcements/DatabaseContext.Announcement.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`
- Modify: `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`
- Modify: `Condotify.Contracts/LicenseManagementViewModels.cs`
- Modify: `CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs`
- Modify: `Condotify.Contracts/LicenseModuleEnum.cs`
- Modify: `CondotifyAPI.Domain/Enums/Mobile/MobileNotificationEnums.cs`
- Modify: `Condotify.Contracts/MobileNotificationViewModels.cs`
- Modify: `CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs`
- Create: migration em `CondotifyAPI.Infrastructure/Migrations/` (gerada pelo comando do Step 6)

**Interfaces:**
- Produces: `AnnouncementDTO` (`Id`, `LicenseId`, `Title`, `Body`, `IsUrgent`, `CreatedBy`, `CreatedAt`, `UpdatedAt`), `LicensePermissionEnum.ManageAnnouncements`, `LicenseModuleEnum.Announcements`, `MobileNotificationCategory.Announcement` — todos consumidos pelas Tasks 2-4.

- [ ] **Step 1: Criar a entidade `AnnouncementDTO`**

```csharp
// CondotifyAPI.Domain/DTO/Announcements/AnnouncementDTO.cs
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Announcements;

public sealed class AnnouncementDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

(Segue exatamente o formato de `CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs:5-18` — `LicenseId` + navegação `License` + `ILicenseScoped`.)

- [ ] **Step 2: Configuração EF + registro do `DbSet`**

```csharp
// CondotifyAPI.Infrastructure/ContextConfiguration/Announcements/AnnouncementConfiguration.cs
using CondotifyAPI.Domain.DTO.Announcements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Announcements;

public sealed class AnnouncementConfiguration : IEntityTypeConfiguration<AnnouncementDTO>
{
    public void Configure(EntityTypeBuilder<AnnouncementDTO> builder)
    {
        builder.ToTable("Announcements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.CreatedBy).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.CreatedAt });
    }
}
```

```csharp
// CondotifyAPI.Infrastructure/DatabaseContext/Announcements/DatabaseContext.Announcement.cs
using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Infrastructure.ContextConfiguration.Announcements;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AnnouncementDTO> Announcements { get; set; }

    internal static void AnnouncementEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AnnouncementConfiguration());
    }
}
```

(Segue exatamente o formato de `CondotifyAPI.Infrastructure/DatabaseContext/Documents/DatabaseContext.ResourceDocument.cs` e `.../ContextConfiguration/Documents/ResourceDocumentConfiguration.cs`.)

Em `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`, dentro de `OnModelCreating` (linha 57 hoje), adicionar logo após `ResourceDocumentEntityConfiguration(modelBuilder);`:

```csharp
        ResourceDocumentEntityConfiguration(modelBuilder);
        AnnouncementEntityConfiguration(modelBuilder);
```

O loop de reflection já existente (linhas 71-76 do mesmo arquivo hoje) aplica o filtro global de tenant automaticamente a `AnnouncementDTO`, já que ela implementa `ILicenseScoped` — nenhuma mudança adicional necessária ali.

- [ ] **Step 3: Nova permissão `ManageAnnouncements`**

Em `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`, o próximo bit livre em `LicensePermissionEnum` é `1L << 35` (o enum vai de `1L << 0` até `ManageDocuments = 1L << 34`, com `All = (1L << 35) - 1` hoje). Trocar:

```csharp
    ManageDocuments = 1L << 34,
    All = (1L << 35) - 1
}
```

por:

```csharp
    ManageDocuments = 1L << 34,
    ManageAnnouncements = 1L << 35,
    All = (1L << 36) - 1
}
```

Em `Condotify.Contracts/LicenseManagementViewModels.cs`, o enum espelho `LicensePermission` (linha 434 hoje) tem uma lacuna pré-existente (não usa os bits 29/30 que o servidor usa para `ViewVehicles`/`ManageVehicles`, mas ambos os lados concordam que `1L << 35` está livre, já que `All = (1L << 35) - 1` é idêntico nos dois arquivos hoje). Trocar, do mesmo jeito:

```csharp
        ManageDocuments = 1L << 34,
        All = (1L << 35) - 1
    }
```

por:

```csharp
        ManageDocuments = 1L << 34,
        ManageAnnouncements = 1L << 35,
        All = (1L << 36) - 1
    }
```

Não mexer na lacuna pré-existente dos bits 29/30 — fora de escopo desta task.

- [ ] **Step 4: Novo módulo `Announcements`**

Em `CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs` (o enum vai de `Cameras = 1L << 0` até `Documents = 1L << 9`, com `All = (1L << 10) - 1`):

```csharp
    Documents = 1L << 9,
    All = (1L << 10) - 1
}
```

vira:

```csharp
    Documents = 1L << 9,
    Announcements = 1L << 10,
    All = (1L << 11) - 1
}
```

Fazer a mesma troca, idêntica, em `Condotify.Contracts/LicenseModuleEnum.cs` (os dois arquivos são espelhos exatos hoje, byte a byte).

- [ ] **Step 5: Nova categoria de notificação `Announcement`**

Em `CondotifyAPI.Domain/Enums/Mobile/MobileNotificationEnums.cs`, o enum `MobileNotificationCategory` (linhas 11-21 hoje) vai de `Access = 1` até `Financial = 8`:

```csharp
public enum MobileNotificationCategory
{
    Access = 1,
    Visitor = 2,
    Delivery = 3,
    Booking = 4,
    Security = 5,
    Operational = 6,
    System = 7,
    Financial = 8
}
```

vira:

```csharp
public enum MobileNotificationCategory
{
    Access = 1,
    Visitor = 2,
    Delivery = 3,
    Booking = 4,
    Security = 5,
    Operational = 6,
    System = 7,
    Financial = 8,
    Announcement = 9
}
```

Fazer a mesma troca, idêntica, em `Condotify.Contracts/MobileNotificationViewModels.cs` (linhas 11-21 hoje — é uma cópia independente, não referenciada via `ProjectReference`, então os dois arquivos precisam ser editados separadamente e ficar com os mesmos valores inteiros).

`MobileNotificationsController.cs:18` já usa `Enum.GetValues<MobileNotificationCategory>()` para a lista de categorias de preferências de push — a nova categoria `Announcement` é automaticamente incluída ali, nenhuma mudança adicional necessária nesse controller. `MobileNotificationsController.IsEssential` (linha 226-227 hoje) só retorna `true` para `Security`/`System` — `Announcement` fica não-essencial por padrão (morador pode desativar), comportamento correto, nenhuma mudança necessária.

- [ ] **Step 6: Gerar e revisar a migration**

Run: `dotnet ef migrations add AddAnnouncements --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`

Ler a migration gerada antes de aplicar — o banco de desenvolvimento local tem dados reais (mesma cautela já seguida em todo o histórico deste projeto). Confirmar que ela cria a tabela `Announcements` com as colunas de `AnnouncementConfiguration` (Step 2) e a FK para `Licenses` com `ON DELETE CASCADE`, e nenhuma outra mudança inesperada no diff do modelo (o EF pode detectar mudanças não relacionadas se o modelo já estivesse dessincronizado — se isso acontecer, parar e investigar antes de continuar, não aplicar uma migration com mudanças não explicadas por esta task).

A migration é aplicada automaticamente no próximo start da API (`db.Database.Migrate()` já roda no startup, `CondotifyAPI/Program.cs`) — não é necessário rodar `dotnet ef database update` manualmente neste fluxo de trabalho, mas pode ser usado para testar localmente antes do commit se preferir.

- [ ] **Step 7: Atualizar a contagem esperada de entidades `ILicenseScoped`**

Em `CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs:56`:

```csharp
        Assert.Equal(29, licenseScopedTypes.Count);
```

vira:

```csharp
        Assert.Equal(30, licenseScopedTypes.Count);
```

- [ ] **Step 8: Build + suíte completa**

Run: `dotnet build CondotifyAPI.Infrastructure/CondotifyAPI.Infrastructure.csproj -o /tmp/comunicados-task1-check && rm -rf /tmp/comunicados-task1-check`
Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/comunicados-task1-check2 && rm -rf /tmp/comunicados-task1-check2`
Run: `dotnet build Condotify/Condotify.csproj -o /tmp/comunicados-task1-check3 && rm -rf /tmp/comunicados-task1-check3` (garante que os dois enums espelho no `Condotify.Contracts` compilam certo do lado do portal também)
Run: `dotnet test CondotifyAPI.Tests`
Expected: builds limpos, toda a suíte passa incluindo o novo `Assert.Equal(30, ...)`.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI.Domain/DTO/Announcements/AnnouncementDTO.cs \
        CondotifyAPI.Infrastructure/ContextConfiguration/Announcements/AnnouncementConfiguration.cs \
        CondotifyAPI.Infrastructure/DatabaseContext/Announcements/DatabaseContext.Announcement.cs \
        CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs \
        CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs \
        Condotify.Contracts/LicenseManagementViewModels.cs \
        CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs \
        Condotify.Contracts/LicenseModuleEnum.cs \
        CondotifyAPI.Domain/Enums/Mobile/MobileNotificationEnums.cs \
        Condotify.Contracts/MobileNotificationViewModels.cs \
        CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs \
        CondotifyAPI.Infrastructure/Migrations/
git commit -m "feat(announcements): modelo, permissao, modulo e categoria de notificacao para Comunicados"
```

---

## Task 2: Backend — `AnnouncementsController` + endpoint do morador

**Files:**
- Create: `CondotifyAPI/Data/Announcements/AnnouncementDtos.cs`
- Create: `CondotifyAPI/Controllers/AnnouncementsController.cs`
- Create: `CondotifyAPI/Controllers/ResidentAnnouncementsController.cs`
- Create: `Condotify.Contracts/AnnouncementViewModels.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Test: `CondotifyAPI.Tests/AnnouncementsControllerTests.cs`

**Interfaces:**
- Consumes: `AnnouncementDTO`, `LicensePermissionEnum.ManageAnnouncements`, `MobileNotificationCategory.Announcement` (Task 1), `IPlatformPushNotifier.NotifyLicenseUsersAsync` (já existe), `IResidentAuthorizationService.GetGrantAsync` (já existe).
- Produces: `GET/POST/PUT/DELETE api/access/licenses/{licenseId}/announcements`, `GET api/resident/announcements`, `CondotifyApiClient.GetAnnouncementsAsync/CreateAnnouncementAsync/UpdateAnnouncementAsync/DeleteAnnouncementAsync/GetResidentAnnouncementsAsync`.

- [ ] **Step 1: DTOs de request/response do lado da equipe**

```csharp
// CondotifyAPI/Data/Announcements/AnnouncementDtos.cs
namespace CondotifyAPI.Data.Announcements;

public sealed class AnnouncementOut
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class CreateAnnouncementIn
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
}

public sealed class UpdateAnnouncementIn
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
}
```

- [ ] **Step 2: Escrever o teste (falha primeiro)**

```csharp
// CondotifyAPI.Tests/AnnouncementsControllerTests.cs
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
```

Antes de escrever isto, ler `CondotifyAPI.Domain/DTO/Enterprise/EnterpriseDTO.cs` e `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs` para confirmar nomes exatos de propriedades obrigatórias (o padrão acima já reflete o que outras tasks deste tipo de teste, neste mesmo repositório, já confirmaram funcionar — mas confirme antes de rodar).

- [ ] **Step 3: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~AnnouncementsControllerTests"`
Expected: FAIL — `AnnouncementsController` ainda não existe.

- [ ] **Step 4: Implementar `AnnouncementsController`**

```csharp
// CondotifyAPI/Controllers/AnnouncementsController.cs
using System.Security.Claims;
using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Domain.DTO.Announcements;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/announcements")]
public sealed class AnnouncementsController(DatabaseContext context, IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> List(Guid licenseId, CancellationToken cancellationToken)
    {
        var results = await ListAnnouncementsCore(context, licenseId, cancellationToken);
        return Ok(results);
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Create(Guid licenseId, [FromBody] CreateAnnouncementIn input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160)
            return BadRequest(new { Errors = "Informe um titulo valido (ate 160 caracteres)." });
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 4000)
            return BadRequest(new { Errors = "Informe o texto do comunicado (ate 4000 caracteres)." });

        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var announcement = CreateAnnouncementCore(licenseId, input, actorName);
        context.Announcements.Add(announcement);
        await context.SaveChangesAsync(cancellationToken);

        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var residentId in ResourceDocumentsController.ResolveLicenseNotificationTargets(links, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                MobileNotificationCategory.Announcement,
                announcement.IsUrgent ? $"Comunicado urgente: {announcement.Title}" : $"Novo comunicado: {announcement.Title}",
                Truncate(announcement.Body, 140),
                "/comunicados",
                $"announcement-published:{announcement.Id:N}",
                cancellationToken);
        }

        return Created(string.Empty, ToOut(announcement));
    }

    [HttpPut("{announcementId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Update(Guid licenseId, Guid announcementId, [FromBody] UpdateAnnouncementIn input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Length > 160)
            return BadRequest(new { Errors = "Informe um titulo valido (ate 160 caracteres)." });
        if (string.IsNullOrWhiteSpace(input.Body) || input.Body.Length > 4000)
            return BadRequest(new { Errors = "Informe o texto do comunicado (ate 4000 caracteres)." });

        var announcement = await context.Announcements.FirstOrDefaultAsync(x => x.Id == announcementId && x.LicenseId == licenseId, cancellationToken);
        if (announcement is null) return NotFound();

        announcement.Title = input.Title.Trim();
        announcement.Body = input.Body.Trim();
        announcement.IsUrgent = input.IsUrgent;
        announcement.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);

        return Ok(ToOut(announcement));
    }

    [HttpDelete("{announcementId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageAnnouncements)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid announcementId, CancellationToken cancellationToken)
    {
        var announcement = await context.Announcements.FirstOrDefaultAsync(x => x.Id == announcementId && x.LicenseId == licenseId, cancellationToken);
        if (announcement is null) return NotFound();

        context.Announcements.Remove(announcement);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Deleted" });
    }

    internal static AnnouncementDTO CreateAnnouncementCore(Guid licenseId, CreateAnnouncementIn input, string actorName)
    {
        var now = DateTime.UtcNow;
        return new AnnouncementDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Title = input.Title.Trim(),
            Body = input.Body.Trim(),
            IsUrgent = input.IsUrgent,
            CreatedBy = actorName,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal static async Task<List<AnnouncementOut>> ListAnnouncementsCore(DatabaseContext context, Guid licenseId, CancellationToken cancellationToken = default) =>
        await context.Announcements.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToOut(x))
            .ToListAsync(cancellationToken);

    private static AnnouncementOut ToOut(AnnouncementDTO x) => new()
    {
        Id = x.Id, Title = x.Title, Body = x.Body, IsUrgent = x.IsUrgent,
        CreatedBy = x.CreatedBy, CreatedAt = x.CreatedAt, UpdatedAt = x.UpdatedAt
    };

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength] + "...";
}
```

`AnnouncementOut` (Step 1) precisa de um `using CondotifyAPI.Domain.DTO.Announcements;` implícito para `ToOut`/`CreateAnnouncementCore` referenciarem `AnnouncementDTO` — o `using` já está listado no topo do arquivo acima, conferir ao compilar.

- [ ] **Step 5: Endpoint do morador**

```csharp
// CondotifyAPI/Controllers/ResidentAnnouncementsController.cs
using CondotifyAPI.Data.Announcements;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/announcements")]
public sealed class ResidentAnnouncementsController(DatabaseContext context, IResidentAuthorizationService authorization) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var results = await AnnouncementsController.ListAnnouncementsCore(context, grant.LicenseId, cancellationToken);
        return Ok(results);
    }
}
```

(Segue exatamente o formato de `ResidentResourceDocumentsController.cs` — `[Authorize(Policy = "Resident")]`, `IResidentAuthorizationService.GetGrantAsync(User, ct)` resolve o `LicenseId` do morador autenticado.)

- [ ] **Step 6: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~AnnouncementsControllerTests"`
Expected: PASS (2/2).

- [ ] **Step 7: Contratos de cliente**

```csharp
// Condotify.Contracts/AnnouncementViewModels.cs
namespace Condotify.Models;

public sealed class AnnouncementViewModel
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AnnouncementFormViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsUrgent { get; set; }
}
```

Um único `AnnouncementViewModel` serve tanto a listagem da equipe (portal) quanto a leitura do morador (mobile) — ao contrário do padrão de Documentos (que tem `ResourceDocumentViewModel`/`ResidentResourceDocumentViewModel` separados), aqui os campos precisados pelos dois lados são idênticos (não há campo sensível como `UploadedByName` para esconder do morador), então um único tipo é suficiente — simplificação deliberada, não uma cópia incompleta do padrão de Documentos.

Em `Condotify.ApiClient/CondotifyApiClient.cs`, adicionar perto dos métodos de `GetDocumentsAsync`/`UploadDocumentAsync`:

```csharp
    public Task<ApiResult<List<AnnouncementViewModel>>> GetAnnouncementsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AnnouncementViewModel>>($"api/access/licenses/{licenseId}/announcements", cancellationToken);

    public Task<ApiResult<AnnouncementViewModel>> CreateAnnouncementAsync(Guid licenseId, AnnouncementFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<AnnouncementViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/announcements", new { model.Title, model.Body, model.IsUrgent }, cancellationToken);

    public Task<ApiResult<AnnouncementViewModel>> UpdateAnnouncementAsync(Guid licenseId, Guid announcementId, AnnouncementFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<AnnouncementViewModel>(HttpMethod.Put, $"api/access/licenses/{licenseId}/announcements/{announcementId}", new { model.Title, model.Body, model.IsUrgent }, cancellationToken);

    public Task<ApiResult<bool>> DeleteAnnouncementAsync(Guid licenseId, Guid announcementId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/announcements/{announcementId}", cancellationToken);

    public Task<ApiResult<List<AnnouncementViewModel>>> GetResidentAnnouncementsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<AnnouncementViewModel>>("api/resident/announcements", cancellationToken);
```

`SendForAsync<T>(HttpMethod method, string path, object payload, CancellationToken cancellationToken)` já existe em `Condotify.ApiClient/CondotifyApiClient.cs:1536` com exatamente esta assinatura (confirmado) — os métodos acima já usam a ordem de parâmetros correta.

- [ ] **Step 8: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/comunicados-task2-check && rm -rf /tmp/comunicados-task2-check`
Run: `dotnet build Condotify.ApiClient/Condotify.ApiClient.csproj -o /tmp/comunicados-task2-check2 && rm -rf /tmp/comunicados-task2-check2`
Run: `dotnet test CondotifyAPI.Tests`
Expected: builds limpos, toda a suíte passa.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI/Data/Announcements/AnnouncementDtos.cs \
        CondotifyAPI/Controllers/AnnouncementsController.cs \
        CondotifyAPI/Controllers/ResidentAnnouncementsController.cs \
        Condotify.Contracts/AnnouncementViewModels.cs \
        Condotify.ApiClient/CondotifyApiClient.cs \
        CondotifyAPI.Tests/AnnouncementsControllerTests.cs
git commit -m "feat(announcements): CRUD para a equipe + endpoint de leitura para o morador, com push na publicacao"
```

---

## Task 3: Portal — aba "Comunicados" em `LicenseWorkspace.razor`

**Files:**
- Create: `Condotify/Components/LicenseModules/AnnouncementsModule.razor`
- Create: `Condotify/Components/Dialogs/AnnouncementFormDialog.razor`
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor`

**Interfaces:**
- Consumes: `CondotifyApiClient.GetAnnouncementsAsync/CreateAnnouncementAsync/UpdateAnnouncementAsync/DeleteAnnouncementAsync` (Task 2), `AnnouncementViewModel`/`AnnouncementFormViewModel` (Task 2), `LicensePermission.ManageAnnouncements`, `Condotify.Models.LicenseModuleEnum.Announcements` (Task 1).

- [ ] **Step 1: Diálogo de criar/editar comunicado**

```razor
@* Condotify/Components/Dialogs/AnnouncementFormDialog.razor *@
@inject CondotifyApiClient Api

<MudDialog Class="entity-dialog">
    <DialogContent>
        <div class="dialog-description">
            <MudIcon Icon="@Icons.Material.Outlined.Campaign" />
            <div><strong>@(AnnouncementId.HasValue ? "Editar comunicado" : "Novo comunicado")</strong><span>Fica visível para todos os moradores desta licença assim que publicado.</span></div>
        </div>
        @if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }
        <EditForm Model="_form" OnValidSubmit="SaveAsync">
            <DataAnnotationsValidator />
            <div class="form-grid">
                <MudTextField T="string" @bind-Value="_form.Title" Label="Título" Required Variant="Variant.Outlined" MaxLength="160" Class="full" />
                <MudTextField T="string" @bind-Value="_form.Body" Label="Texto do comunicado" Required Variant="Variant.Outlined" Lines="5" MaxLength="4000" Class="full" />
                <MudSwitch T="bool" @bind-Value="_form.IsUrgent" Color="Color.Error" Label="Marcar como urgente" Class="full" />
            </div>
            <div class="form-actions dialog-form-actions">
                <MudButton Variant="Variant.Text" Disabled="_saving" OnClick="Cancel">Cancelar</MudButton>
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Campaign" Disabled="_saving">@(_saving ? "Publicando..." : (AnnouncementId.HasValue ? "Salvar alterações" : "Publicar comunicado"))</MudButton>
            </div>
        </EditForm>
    </DialogContent>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter] public Guid? AnnouncementId { get; set; }
    [Parameter] public AnnouncementFormViewModel? Existing { get; set; }

    private readonly AnnouncementFormViewModel _form = new();
    private bool _saving;
    private string? _error;

    protected override void OnInitialized()
    {
        if (Existing is null) return;
        _form.Title = Existing.Title;
        _form.Body = Existing.Body;
        _form.IsUrgent = Existing.IsUrgent;
    }

    private void Cancel() => Dialog.Cancel();

    private async Task SaveAsync()
    {
        _saving = true; _error = null;
        var result = AnnouncementId.HasValue
            ? await Api.UpdateAnnouncementAsync(LicenseId, AnnouncementId.Value, _form)
            : await Api.CreateAnnouncementAsync(LicenseId, _form);
        _saving = false;
        if (!result.Success) { _error = result.Error; return; }
        Dialog.Close(DialogResult.Ok(true));
    }
}
```

- [ ] **Step 2: Módulo da aba**

```razor
@* Condotify/Components/LicenseModules/AnnouncementsModule.razor *@
@inject CondotifyApiClient Api
@inject IDialogService DialogService
@inject ISnackbar Snackbar

@if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }

@if (_loading)
{
    <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div>
}
else
{
    <section class="content-panel">
        <div class="panel-heading">
            <div><MudText Typo="Typo.h5">Comunicados</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">Visíveis a todos os moradores desta licença</MudText></div>
            <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Campaign" OnClick="OpenCreateAsync" Disabled="!CanManage">Publicar comunicado</MudButton>
        </div>

        @if (_announcements.Count == 0)
        {
            <div class="empty-state compact-empty">
                <MudIcon Icon="@Icons.Material.Outlined.Campaign" Size="Size.Large" Color="Color.Primary" />
                <MudText Typo="Typo.subtitle1">Nenhum comunicado publicado</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">Avisos publicados aqui ficam visíveis a todos os moradores no app.</MudText>
            </div>
        }
        else
        {
            <div class="document-list">
                @foreach (var announcement in _announcements)
                {
                    <div class="document-row">
                        <span class="category-badge @(announcement.IsUrgent ? "category-amber" : "category-primary")"><MudIcon Icon="@Icons.Material.Outlined.Campaign" /></span>
                        <div class="document-row-body">
                            <div class="document-row-top">
                                <span class="document-row-title">@announcement.Title</span>
                                @if (announcement.IsUrgent) { <span class="category-chip category-amber">Urgente</span> }
                            </div>
                            <div class="document-row-desc">@announcement.Body</div>
                            <div class="document-row-meta">Publicado em @announcement.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") · @announcement.CreatedBy</div>
                        </div>
                        <div class="document-row-actions">
                            <MudTooltip Text="Editar"><MudIconButton Icon="@Icons.Material.Outlined.Edit" Size="Size.Small" Disabled="!CanManage" OnClick="() => OpenEditAsync(announcement)" /></MudTooltip>
                            <MudTooltip Text="Excluir"><MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error" Disabled="!CanManage" OnClick="() => DeleteAsync(announcement.Id)" /></MudTooltip>
                        </div>
                    </div>
                }
            </div>
        }
    </section>
}

@code {
    [Parameter] public Guid LicenseId { get; set; }
    [Parameter] public bool CanManage { get; set; }

    private List<AnnouncementViewModel> _announcements = [];
    private bool _loading;
    private string? _error;

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        var result = await Api.GetAnnouncementsAsync(LicenseId);
        _loading = false;
        if (result.Success) _announcements = result.Value ?? [];
        else _error = result.Error;
    }

    private async Task OpenCreateAsync()
    {
        var parameters = new DialogParameters { [nameof(AnnouncementFormDialog.LicenseId)] = LicenseId };
        var dialog = await DialogService.ShowAsync<AnnouncementFormDialog>("", parameters, new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is { Canceled: false }) { Snackbar.Add("Comunicado publicado com sucesso.", Severity.Success); await LoadAsync(); }
    }

    private async Task OpenEditAsync(AnnouncementViewModel announcement)
    {
        var existing = new AnnouncementFormViewModel { Title = announcement.Title, Body = announcement.Body, IsUrgent = announcement.IsUrgent };
        var parameters = new DialogParameters
        {
            [nameof(AnnouncementFormDialog.LicenseId)] = LicenseId,
            [nameof(AnnouncementFormDialog.AnnouncementId)] = announcement.Id,
            [nameof(AnnouncementFormDialog.Existing)] = existing
        };
        var dialog = await DialogService.ShowAsync<AnnouncementFormDialog>("", parameters, new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is { Canceled: false }) { Snackbar.Add("Comunicado atualizado.", Severity.Success); await LoadAsync(); }
    }

    private async Task DeleteAsync(Guid announcementId)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync("Excluir comunicado", "Excluir este comunicado? Ele deixa de aparecer para os moradores imediatamente.", yesText: "Excluir", cancelText: "Cancelar");
        if (confirmed != true) return;
        var result = await Api.DeleteAnnouncementAsync(LicenseId, announcementId);
        if (!result.Success) { Snackbar.Add(result.Error ?? "Não foi possível excluir o comunicado.", Severity.Error); return; }
        Snackbar.Add("Comunicado excluído.", Severity.Success);
        await LoadAsync();
    }
}
```

- [ ] **Step 3: Ligar a aba em `LicenseWorkspace.razor`**

No grupo `"Operação"` do array `TabGroups` (linhas 118-123 hoje), adicionar a nova entrada após `agendamento`:

```csharp
        ("Operação", [
            new("ocorrencias", "Ocorrências", Icons.Material.Outlined.AssignmentLate, LicensePermission.ViewIncidents, Condotify.Models.LicenseModuleEnum.Incidents),
            new("automacoes", "Automações", Icons.Material.Outlined.AutoAwesome, LicensePermission.ViewAutomations, Condotify.Models.LicenseModuleEnum.Automations),
            new("emergencia", "Emergência", Icons.Material.Outlined.HealthAndSafety, LicensePermission.ViewEmergency, Condotify.Models.LicenseModuleEnum.Emergency),
            new("agendamento", "Agendamento", Icons.Material.Outlined.Deck, LicensePermission.ViewBookings, Condotify.Models.LicenseModuleEnum.Bookings),
            new("comunicados", "Comunicados", Icons.Material.Outlined.Campaign, LicensePermission.ManageAnnouncements, Condotify.Models.LicenseModuleEnum.Announcements),
        ]),
```

No `@switch (CurrentSection)` (linhas 57-98 hoje), adicionar um `case` novo — por exemplo logo após o `case "agendamento":` (linhas 71-73):

```csharp
            case "agendamento":
                <AgendamentoModule LicenseId="LicenseId" CanManage="@Has(LicensePermission.ManageBookings)" />
                break;
            case "comunicados":
                <AnnouncementsModule LicenseId="LicenseId" CanManage="@Has(LicensePermission.ManageAnnouncements)" />
                break;
```

Em `DefaultSection` (linhas 142-155 hoje), adicionar uma linha antes do fallback final `"administracao"`:

```csharp
        : Has(LicensePermission.ManageDocuments) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Documents) ? "documentos"
        : Has(LicensePermission.ManageAnnouncements) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Announcements) ? "comunicados"
        : Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) || Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts) ? "administracao"
```

Em `SectionAllowed` (linhas 192-206 hoje), adicionar um case:

```csharp
        "documentos" => Has(LicensePermission.ManageDocuments),
        "comunicados" => Has(LicensePermission.ManageAnnouncements),
        "administracao" => Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) || Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts),
```

Em `ModuleFor` (linhas 208-220 hoje), adicionar um case:

```csharp
        "documentos" => Condotify.Models.LicenseModuleEnum.Documents,
        "comunicados" => Condotify.Models.LicenseModuleEnum.Announcements,
        _ => null
```

- [ ] **Step 4: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/comunicados-task3-check && rm -rf /tmp/comunicados-task3-check`
Expected: build limpo.

- [ ] **Step 5: Verificação manual**

Sem suíte de testes de UI: rodar o portal localmente, abrir `/licencas/{id}/comunicados`, publicar/editar/excluir um comunicado, confirmar que a aba só aparece para um usuário com `ManageAnnouncements`.

- [ ] **Step 6: Commit**

```bash
git add Condotify/Components/LicenseModules/AnnouncementsModule.razor \
        Condotify/Components/Dialogs/AnnouncementFormDialog.razor \
        Condotify/Components/Pages/LicenseWorkspace.razor
git commit -m "feat(announcements): aba Comunicados no portal (publicar, editar, excluir)"
```

---

## Task 4: Mobile — tela `Comunicados.razor` para o morador

**Files:**
- Create: `Condotify.Mobile/Components/Pages/Comunicados.razor`
- Modify: `Condotify.Mobile/Components/Pages/More.razor`
- Modify: `Condotify.Mobile/Components/Pages/Notifications.razor`

**Interfaces:**
- Consumes: `CondotifyApiClient.GetResidentAnnouncementsAsync` (Task 2), `AnnouncementViewModel` (Task 2), `MobilePullToRefreshState` (já existe, mesmo padrão de `Deliveries.razor`), `Condotify.Models.LicenseModuleEnum.Announcements`/`MobileNotificationCategory.Announcement` (Task 1).

- [ ] **Step 1: Tela de Comunicados (morador)**

```razor
@* Condotify.Mobile/Components/Pages/Comunicados.razor *@
@page "/comunicados"
@implements IDisposable
@inject CondotifyApiClient Api
@inject MobilePullToRefreshState PullToRefresh

<PageTitle>Comunicados | Condotify</PageTitle>
<PageHeader Eyebrow="CONDOMÍNIO" Title="Comunicados" Subtitle="Avisos publicados pela administração.">
    <Actions><MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" aria-label="Atualizar" /></Actions>
</PageHeader>

<PageState Loading="_loading" Error="@_error" Empty="@(_announcements.Count == 0)" EmptyTitle="Nenhum comunicado" EmptyText="Quando a administração publicar um aviso, ele aparece aqui." Retry="LoadAsync">
    <section class="list-group">
        @foreach (var announcement in _announcements)
        {
            <div class="list-row @(announcement.IsUrgent ? "list-row-urgent" : null)">
                <span class="settings-icon @(announcement.IsUrgent ? "error" : "info")"><MudIcon Icon="@Icons.Material.Outlined.Campaign" /></span>
                <div class="list-main">
                    <div class="list-title">
                        @announcement.Title
                        @if (announcement.IsUrgent) { <MudChip T="string" Size="Size.Small" Color="Color.Error" Class="ml-2">Urgente</MudChip> }
                    </div>
                    <div class="list-meta">@announcement.Body</div>
                    <div class="list-meta">@announcement.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") · @announcement.CreatedBy</div>
                </div>
            </div>
        }
    </section>
</PageState>

@code {
    private List<AnnouncementViewModel> _announcements = [];
    private bool _loading;
    private string _error = string.Empty;

    protected override Task OnInitializedAsync()
    {
        PullToRefresh.Register(LoadAsync);
        return LoadAsync();
    }

    public void Dispose() => PullToRefresh.Unregister(LoadAsync);

    private async Task LoadAsync()
    {
        _loading = true; _error = string.Empty;
        var result = await Api.GetResidentAnnouncementsAsync();
        _loading = false;
        if (result.Success) _announcements = result.Value ?? [];
        else _error = result.Error ?? "Não foi possível carregar os comunicados.";
    }
}
```

(Segue o padrão de `Condotify.Mobile/Components/Pages/Documentos.razor` para a lista + `PageState`, e o padrão de `Deliveries.razor` para o registro do pull-to-refresh via `MobilePullToRefreshState` — `Register(LoadAsync)` em `OnInitializedAsync`, `Unregister(LoadAsync)` em `Dispose`.)

- [ ] **Step 2: Entrada de navegação em `More.razor`**

Em `Condotify.Mobile/Components/Pages/More.razor`, logo após a linha do "Documentos" (a linha com `href="/documentos"`, dentro do branch do morador):

```razor
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Documents)) { <a class="list-row" href="/documentos"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Description" /></span><div class="list-main"><div class="list-title">Documentos</div><div class="list-meta">Atas, regimento, comunicados e mais</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
```

adicionar logo abaixo:

```razor
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Announcements)) { <a class="list-row" href="/comunicados"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.Campaign" /></span><div class="list-main"><div class="list-title">Comunicados</div><div class="list-meta">Avisos publicados pela administração</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
```

`ModuleOn` já é o helper existente usado por todas as outras linhas dessa seção — não precisa de nenhuma mudança nele, ele já lê o bitmask genericamente.

- [ ] **Step 3: Rótulo/ícone/cor da categoria nova na caixa de notificações**

Em `Condotify.Mobile/Components/Pages/Notifications.razor`, os três `switch` de categoria (linhas 118-120 hoje) têm um `_ =>` de fallback cada um — a categoria `Announcement` funcionaria sem nenhuma mudança (cairia no fallback "Sistema"/ícone genérico), mas para uma UX melhor, adicionar um caso explícito nos três:

```csharp
    private static string Label(MobileNotificationCategory category) => category switch { MobileNotificationCategory.Access => "Acessos", MobileNotificationCategory.Visitor => "Visitantes", MobileNotificationCategory.Delivery => "Encomendas", MobileNotificationCategory.Booking => "Reservas", MobileNotificationCategory.Security => "Segurança", MobileNotificationCategory.Operational => "Operação", MobileNotificationCategory.Financial => "Boletos", MobileNotificationCategory.Announcement => "Comunicados", _ => "Sistema" };
    private static string IconFor(MobileNotificationCategory category) => category switch { MobileNotificationCategory.Access => Icons.Material.Outlined.Badge, MobileNotificationCategory.Visitor => Icons.Material.Outlined.Group, MobileNotificationCategory.Delivery => Icons.Material.Outlined.Inventory2, MobileNotificationCategory.Booking => Icons.Material.Outlined.CalendarMonth, MobileNotificationCategory.Security => Icons.Material.Outlined.Security, MobileNotificationCategory.Operational => Icons.Material.Outlined.WarningAmber, MobileNotificationCategory.Financial => Icons.Material.Outlined.ReceiptLong, MobileNotificationCategory.Announcement => Icons.Material.Outlined.Campaign, _ => Icons.Material.Outlined.Info };
    private static Color ColorFor(MobileNotificationCategory category) => category switch { MobileNotificationCategory.Security => Color.Error, MobileNotificationCategory.Operational => Color.Warning, MobileNotificationCategory.Delivery => Color.Info, MobileNotificationCategory.Booking => Color.Tertiary, MobileNotificationCategory.Announcement => Color.Warning, _ => Color.Primary };
```

(Essas são as linhas 118-120 do arquivo hoje, exatamente como encontradas — cada uma ganha só o caso `MobileNotificationCategory.Announcement => ...` novo antes do `_ =>` final, sem tocar em mais nada da expressão.)

- [ ] **Step 4: Build**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android35.0 -o /tmp/comunicados-task4-check && rm -rf /tmp/comunicados-task4-check`
Expected: build limpo (usar o target Android é suficiente para pegar erros de compilação C#/Razor; não é necessário compilar todos os targets desta task — os alvos iOS têm um problema de empacotamento pré-existente e não relacionado, já documentado em trabalho anterior desta sessão).

- [ ] **Step 5: Verificação manual**

Sem suíte de testes de UI: rodar o app mobile localmente como morador, abrir "Mais" → "Comunicados", confirmar que a lista carrega, que o pull-to-refresh funciona, e que um comunicado marcado como urgente aparece destacado. Publicar um comunicado pelo portal (Task 3) e confirmar que a notificação push chega no dispositivo e que o comunicado aparece na lista após atualizar.

- [ ] **Step 6: Commit**

```bash
git add Condotify.Mobile/Components/Pages/Comunicados.razor \
        Condotify.Mobile/Components/Pages/More.razor \
        Condotify.Mobile/Components/Pages/Notifications.razor
git commit -m "feat(announcements): tela de Comunicados no app mobile, com push e pull-to-refresh"
```

---

## Final check (todas as tasks completas)

- [ ] `dotnet build Condotify.sln` limpo (usar `-o` para pasta temporária se algum `dotnet run` estiver ativo; ignorar falhas pré-existentes e não relacionadas do target iOS de `Condotify.Mobile`, já documentadas nesta sessão).
- [ ] `dotnet test CondotifyAPI.Tests` — todos os testes passam, incluindo os novos de `AnnouncementsControllerTests` e a contagem atualizada de `LicenseScopedFilterModelTests`.
- [ ] Verificação manual ponta a ponta: publicar um comunicado no portal → aparece na lista da equipe → chega push no morador → aparece na tela mobile → editar no portal → mudança reflete no mobile após atualizar → excluir no portal → some do mobile.
- [ ] Confirmar que uma licença com `EnabledModules` sem o bit `Announcements` não mostra a aba no portal nem a linha no "Mais" do mobile (comportamento correto — módulo desabilitado por padrão até o síndico/admin da plataforma habilitá-lo, mesmo padrão dos outros módulos aditivos).
