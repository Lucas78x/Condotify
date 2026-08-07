# Área de Documentos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deixar síndico/administração publicar documentos do condomínio (atas, regimento, convenção, comunicados, prestação de contas) em PDF, visíveis a todos os moradores da licença, com notificação push, seguindo os mesmos padrões já validados no módulo de Boletos.

**Architecture:** Módulo novo e autocontido (`Documents`), sem lote nem revisão — upload já publica na hora. Uma entidade EF (`ResourceDocumentDTO`), storage criptografado dedicado (novo serviço irmão de `BoletoDocumentStore`, não reaproveitado — nomes diferentes, mesma técnica), reaproveitando `IBoletoPdfProcessor` só para validar que o arquivo é um PDF legível (não há necessidade de dividir por página aqui, então `ExtractPageAsPdf` não é usado). Upload/gestão só no portal web; visualização só no app mobile — mesma justificativa de sempre (não existe portal web de morador nesta plataforma).

**Tech Stack:** ASP.NET Core 8 API + EF Core/Npgsql, Blazor Server (MudBlazor) para o portal web, MAUI Blazor Hybrid para o app.

## Global Constraints

- Spec de referência: `docs/superpowers/specs/2026-08-07-documentos-design.md` — qualquer conflito, a spec governa.
- Só PDF, sem versionamento, sem visibilidade restrita por documento (tudo publicado é visível a todos os moradores da licença), sem upload em lote — um documento por envio.
- Upload/gestão: só portal web (`Condotify`), atrás de `LicensePermissionEnum.ManageDocuments`. Visualização/download: só app mobile (`Condotify.Mobile`), atrás de `[Authorize(Policy = "Resident")]`.
- Reaproveitar `IBoletoPdfProcessor.CountPages` (já existe, `CondotifyAPI.Services.Finance`) só para validar que o PDF é legível antes de guardar — não usar `ExtractPageAsPdf`/`ExtractPageText` (não há por que dividir ou extrair texto aqui).
- Não existe hoje um helper para "notificar todos os moradores de uma licença" — `IPlatformPushNotifier.NotifyLicenseUsersAsync` notifica a **equipe** (`LicenseUserAccesses`), não moradores. Este plano cria a seleção de destinatários (moradores com vínculo vigente na licença) como função pura testável, mesmo padrão de `BoletosController.ResolveNotificationTargets`.
- Todo texto de UI em português.

---

## File Structure

**Backend (`CondotifyAPI*`):**
- `CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs` — `ResourceDocumentDTO`, `ResourceDocumentCategoryEnum` (entidade EF, mesmo padrão de `BoletoDtos.cs`).
- `CondotifyAPI.Infrastructure/ContextConfiguration/Documents/ResourceDocumentConfiguration.cs` — `IEntityTypeConfiguration`.
- `CondotifyAPI.Infrastructure/DatabaseContext/Documents/DatabaseContext.ResourceDocument.cs` — `DbSet` + registro, partial de `DatabaseContext`.
- `CondotifyAPI/Services/Documents/ResourceDocumentStore.cs` — armazenamento cifrado do PDF (novo serviço irmão de `BoletoDocumentStore`).
- `CondotifyAPI/Data/Documents/ResourceDocumentDtos.cs` — contratos de request/response da API.
- `CondotifyAPI/Controllers/ResourceDocumentsController.cs` — endpoints de staff.
- `CondotifyAPI/Controllers/ResidentResourceDocumentsController.cs` — endpoints de morador.
- `CondotifyAPI.Tests/ResourceDocumentNotificationTests.cs`, `CondotifyAPI.Tests/ResourceDocumentsControllerTests.cs`, `CondotifyAPI.Tests/ResidentResourceDocumentsControllerTests.cs`.

**Contratos compartilhados:**
- `Condotify.Contracts/DocumentViewModels.cs` — `ResourceDocumentViewModel`, `ResidentResourceDocumentViewModel` (namespace real do projeto é `Condotify.Models`, não `Condotify.Contracts` — descoberto durante Boletos, confirmar antes de escrever).

**Portal web (`Condotify`):**
- `Condotify/Components/LicenseModules/DocumentsModule.razor` — lista + filtro por categoria.
- `Condotify/Components/Dialogs/DocumentUploadDialog.razor` — formulário de novo documento.
- `Condotify.ApiClient/CondotifyApiClient.cs` — métodos novos (staff).
- `Condotify/wwwroot/css/portal.css` — estilos novos se necessário (reaproveitar `.content-panel`/`.import-source`/`.file-button` já existentes sempre que possível).

**App mobile (`Condotify.Mobile`):**
- `Condotify.Mobile/Components/Pages/Documentos.razor` — lista do morador.
- `Condotify.Mobile/Components/Pages/More.razor` — item de navegação novo.
- `Condotify.ApiClient/CondotifyApiClient.cs` — métodos novos (morador, mesmo arquivo dos métodos de staff).

---

### Task 1: Permissões e rota de deep link

**Files:**
- Modify: `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`
- Modify: `Condotify.Contracts/LicenseManagementViewModels.cs`
- Modify: `Condotify.Contracts/MobileNotificationViewModels.cs`

**Interfaces:**
- Produces: `LicensePermissionEnum.ViewDocuments` / `.ManageDocuments` (`1L << 33` / `1L << 34`), espelhados em `LicensePermission` (mesmos valores, namespace real `Condotify.Models`). Rota `/documentos` aceita por `MobileDeepLinks.TryNormalize`.

- [ ] **Step 1: Adicionar as permissões no enum da API**

Em `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`, trocar:

```csharp
    ViewFinance = 1L << 31,
    ManageFinance = 1L << 32,
    All = (1L << 33) - 1
}
```

por:

```csharp
    ViewFinance = 1L << 31,
    ManageFinance = 1L << 32,
    ViewDocuments = 1L << 33,
    ManageDocuments = 1L << 34,
    All = (1L << 35) - 1
}
```

No método `Normalize` (mesmo arquivo), adicionar junto das demais:

```csharp
        if (permissions.HasFlag(LicensePermissionEnum.ManageDocuments)) permissions |= LicensePermissionEnum.ViewDocuments;
```

- [ ] **Step 2: Espelhar no enum do portal web**

Em `Condotify.Contracts/LicenseManagementViewModels.cs` (namespace real do arquivo é `Condotify.Models` — conferir a linha `namespace` antes de editar, não assumir), trocar:

```csharp
        ViewFinance = 1L << 31,
        ManageFinance = 1L << 32,
        All = (1L << 33) - 1
    }
```

por:

```csharp
        ViewFinance = 1L << 31,
        ManageFinance = 1L << 32,
        ViewDocuments = 1L << 33,
        ManageDocuments = 1L << 34,
        All = (1L << 35) - 1
    }
```

- [ ] **Step 3: Rota de deep link**

Em `Condotify.Contracts/MobileNotificationViewModels.cs`, dentro de `MobileDeepLinks`, trocar:

```csharp
    private static readonly string[] StaticRoutes = ["/home", "/profile", "/boletos"];
```

por:

```csharp
    private static readonly string[] StaticRoutes = ["/home", "/profile", "/boletos", "/documentos"];
```

- [ ] **Step 4: Build**

Run: `dotnet build Condotify.sln`
Expected: build limpo.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs Condotify.Contracts/LicenseManagementViewModels.cs Condotify.Contracts/MobileNotificationViewModels.cs
git commit -m "feat(documentos): add ViewDocuments/ManageDocuments permissions and deep link route"
```

---

### Task 2: Modelo de dados e migração

**Files:**
- Create: `CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs`
- Create: `CondotifyAPI.Infrastructure/ContextConfiguration/Documents/ResourceDocumentConfiguration.cs`
- Create: `CondotifyAPI.Infrastructure/DatabaseContext/Documents/DatabaseContext.ResourceDocument.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`
- Create: migração EF

**Interfaces:**
- Produces: `ResourceDocumentDTO { Id, LicenseId, License, Category, Title, Description, StorageReference, UploadedByUserId, UploadedByName, PublishedAt, CreatedAt }`, `ResourceDocumentCategoryEnum { Minutes, ByLaws, Covenant, Announcement, FinancialStatement, Other }`.

- [ ] **Step 1: Criar a entidade EF**

Create `CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs`:

```csharp
using CondotifyAPI.Domain.DTO.License;

namespace CondotifyAPI.Domain.DTO.Documents;

public sealed class ResourceDocumentDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public ResourceDocumentCategoryEnum Category { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string StorageReference { get; set; } = string.Empty;
    public Guid UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum ResourceDocumentCategoryEnum { Minutes = 0, ByLaws = 1, Covenant = 2, Announcement = 3, FinancialStatement = 4, Other = 5 }
```

- [ ] **Step 2: Configuração EF**

Create `CondotifyAPI.Infrastructure/ContextConfiguration/Documents/ResourceDocumentConfiguration.cs`:

```csharp
using CondotifyAPI.Domain.DTO.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Documents;

public sealed class ResourceDocumentConfiguration : IEntityTypeConfiguration<ResourceDocumentDTO>
{
    public void Configure(EntityTypeBuilder<ResourceDocumentDTO> builder)
    {
        builder.ToTable("ResourceDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).IsRequired().HasMaxLength(160);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.StorageReference).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UploadedByName).IsRequired().HasMaxLength(150);
        builder.HasOne(x => x.License).WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.LicenseId, x.Category, x.PublishedAt });
    }
}
```

- [ ] **Step 3: Registrar no DbContext**

Create `CondotifyAPI.Infrastructure/DatabaseContext/Documents/DatabaseContext.ResourceDocument.cs`:

```csharp
using CondotifyAPI.Domain.DTO.Documents;
using CondotifyAPI.Infrastructure.ContextConfiguration.Documents;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<ResourceDocumentDTO> ResourceDocuments { get; set; }

    internal static void ResourceDocumentEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new ResourceDocumentConfiguration());
    }
}
```

Em `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`, adicionar uma linha dentro de `OnModelCreating`, junto das outras `*EntityConfiguration`:

```csharp
        BoletoEntityConfiguration(modelBuilder);
        ResourceDocumentEntityConfiguration(modelBuilder);
```

- [ ] **Step 4: Gerar a migração**

Run: `dotnet ef migrations add AddResourceDocuments --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`

Ler a migração gerada por completo antes de confiar nela — confirmar que só cria a tabela `ResourceDocuments` com a FK/índice acima, sem tocar em nada mais. O banco de desenvolvimento tem dados reais; não aplicar (`dotnet ef database update`) sem confirmar com quem for testar antes.

- [ ] **Step 5: Build**

Run: `dotnet build Condotify.sln`
Expected: build limpo.

- [ ] **Step 6: Commit**

```bash
git add CondotifyAPI.Domain/DTO/Documents CondotifyAPI.Infrastructure/ContextConfiguration/Documents CondotifyAPI.Infrastructure/DatabaseContext/Documents CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs CondotifyAPI.Infrastructure/Migrations
git commit -m "feat(documentos): add ResourceDocument entity and migration"
```

---

### Task 3: Armazenamento criptografado do PDF

**Files:**
- Create: `CondotifyAPI/Services/Documents/ResourceDocumentStore.cs`
- Modify: `CondotifyAPI/Program.cs`

**Interfaces:**
- Produces: `IResourceDocumentStore { Task<string> StoreAsync(Guid licenseId, byte[] pdfBytes, CancellationToken), Task<byte[]?> ReadAsync(Guid licenseId, string reference, CancellationToken), Task DeleteAsync(Guid licenseId, string? reference, CancellationToken) }` — usado pela Tarefa 4.

- [ ] **Step 1: Implementar o serviço**

Create `CondotifyAPI/Services/Documents/ResourceDocumentStore.cs` — cópia adaptada de `CondotifyAPI/Services/Finance/BoletoDocumentStore.cs` (mesma técnica AES-GCM, mesmo teto de 2 MB), com pasta e prefixo de referência próprios para não misturar com os arquivos de boleto:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.Documents;

public interface IResourceDocumentStore
{
    Task<string> StoreAsync(Guid licenseId, byte[] pdfBytes, CancellationToken cancellationToken = default);
    Task<byte[]?> ReadAsync(Guid licenseId, string reference, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default);
}

public sealed class ResourceDocumentStore : IResourceDocumentStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxFileBytes = 2_000_000;
    private readonly string _root;
    private readonly byte[] _key;

    public ResourceDocumentStore(IConfiguration configuration)
    {
        _root = Environment.GetEnvironmentVariable("CONDOTIFY_DOCUMENT_STORAGE_PATH")
            ?? configuration["DocumentStorage:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "documents");
        var secret = Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET")
            ?? Environment.GetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET")
            ?? throw new InvalidOperationException("Defina CONDOTIFY_MEDIA_SECRET para proteger os documentos.");
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        Directory.CreateDirectory(_root);
    }

    public async Task<string> StoreAsync(Guid licenseId, byte[] pdfBytes, CancellationToken cancellationToken = default)
    {
        if (pdfBytes.Length is < 1 or > MaxFileBytes) throw new InvalidOperationException("O documento deve ter no maximo 2 MB.");
        var documentId = Guid.NewGuid();
        var directory = LicenseDirectory(licenseId);
        Directory.CreateDirectory(directory);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var cipher = new byte[pdfBytes.Length];
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, pdfBytes, cipher, tag);
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        await File.WriteAllBytesAsync(FilePath(licenseId, documentId), payload, cancellationToken);
        return Reference(licenseId, documentId);
    }

    public async Task<byte[]?> ReadAsync(Guid licenseId, string reference, CancellationToken cancellationToken = default)
    {
        if (!TryDocumentId(reference, out var documentId)) return null;
        var path = FilePath(licenseId, documentId);
        if (!File.Exists(path)) return null;
        var payload = await File.ReadAllBytesAsync(path, cancellationToken);
        if (payload.Length < NonceSize + TagSize) return null;
        var plain = new byte[payload.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                plain);
            return plain;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plain);
            return null;
        }
    }

    public Task DeleteAsync(Guid licenseId, string? reference, CancellationToken cancellationToken = default)
    {
        if (TryDocumentId(reference, out var documentId))
        {
            var path = FilePath(licenseId, documentId);
            if (File.Exists(path)) File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string LicenseDirectory(Guid licenseId) => Path.Combine(_root, licenseId.ToString("N"));
    private string FilePath(Guid licenseId, Guid documentId) => Path.Combine(LicenseDirectory(licenseId), $"{documentId:N}.bin");
    private static string Reference(Guid licenseId, Guid documentId) => $"/documents-media/{licenseId:D}/{documentId:D}";

    private static bool TryDocumentId(string? reference, out Guid documentId)
    {
        documentId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(reference)) return false;
        return Guid.TryParse(reference.TrimEnd('/').Split('/').LastOrDefault(), out documentId);
    }
}
```

- [ ] **Step 2: Registrar no DI**

Em `CondotifyAPI/Program.cs`, junto das outras linhas de storage:

```csharp
builder.Services.AddSingleton<IResourceDocumentStore, ResourceDocumentStore>();
```

(adicionar `using CondotifyAPI.Services.Documents;` se ainda não houver um `using` cobrindo o namespace).

- [ ] **Step 3: Build**

Run: `dotnet build Condotify.sln`
Expected: build limpo.

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI/Services/Documents/ResourceDocumentStore.cs CondotifyAPI/Program.cs
git commit -m "feat(documentos): add encrypted document storage"
```

---

### Task 4: Controller de staff — upload, listagem, exclusão

**Files:**
- Create: `CondotifyAPI/Data/Documents/ResourceDocumentDtos.cs`
- Create: `CondotifyAPI/Controllers/ResourceDocumentsController.cs`
- Create: `CondotifyAPI.Tests/ResourceDocumentNotificationTests.cs`
- Create: `CondotifyAPI.Tests/ResourceDocumentsControllerTests.cs`

**Interfaces:**
- Consumes: `IResourceDocumentStore` (Tarefa 3), `ResourceDocumentDTO`/`ResourceDocumentCategoryEnum` (Tarefa 2), `LicensePermissionEnum.ManageDocuments` (Tarefa 1), `IBoletoPdfProcessor.CountPages` (já existe, `CondotifyAPI.Services.Finance` — só para validar que o PDF e legivel, sem dividir por pagina), `IPlatformPushNotifier.NotifyResidentAsync` (já existe, `CondotifyAPI.Services.Mobile`).
- Produces: rotas `POST/GET api/access/licenses/{licenseId:guid}/documents`, `GET api/access/licenses/{licenseId:guid}/documents/{documentId:guid}/file`, `DELETE api/access/licenses/{licenseId:guid}/documents/{documentId:guid}` — consumidas pela Tarefa 6. `ResolveLicenseNotificationTargets` é consumido pelos testes desta mesma tarefa.

- [ ] **Step 1: Contratos de request/response**

Create `CondotifyAPI/Data/Documents/ResourceDocumentDtos.cs`:

```csharp
namespace CondotifyAPI.Data.Documents;

public sealed class ResourceDocumentOut
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

public sealed class ResourceDocumentUploadForm
{
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IFormFile File { get; set; } = null!;
}
```

- [ ] **Step 2: Algoritmo de seleção de destinatários (função pura)**

A regra "quais moradores devem ser notificados sobre um novo documento desta licença" precisa ser pura e testável sem banco — mesmo padrão de `BoletosController.ResolveNotificationTargets`, mas aqui recebendo a lista de vínculos já carregada (não um único documento/unidade) porque não há unidade envolvida.

No controller (Step 3 abaixo), adicionar como método `internal static`:

```csharp
    /// <summary>
    /// Quem deve ser notificado sobre um documento recem-publicado: um morador
    /// por vinculo VIGENTE (mesma regra de
    /// <see cref="ResidentAuthorizationService.LinkIsCurrentlyValid"/>) em
    /// qualquer unidade desta licenca, deduplicado (o mesmo morador com vinculo
    /// em duas unidades da mesma licenca aparece uma unica vez). Puro e recebendo
    /// <paramref name="now"/> explicitamente para ser testavel sem banco.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ResolveLicenseNotificationTargets(
        IEnumerable<ResidentUnitLinkDTO> links,
        DateTime now) =>
        links
            .Where(link => ResidentAuthorizationService.LinkIsCurrentlyValid(link, now))
            .Select(link => link.ResidentId)
            .Distinct()
            .ToList();
```

- [ ] **Step 3: Controller**

Create `CondotifyAPI/Controllers/ResourceDocumentsController.cs`:

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Documents;
using CondotifyAPI.Domain.DTO.Documents;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.Enums.Mobile;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Documents;
using CondotifyAPI.Services.Finance;
using CondotifyAPI.Services.Mobile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/documents")]
public sealed class ResourceDocumentsController(
    DatabaseContext context,
    IResourceDocumentStore store,
    IBoletoPdfProcessor pdf,
    IPlatformPushNotifier notifier) : ControllerBase
{
    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    [RequestSizeLimit(3_000_000)]
    public async Task<IActionResult> Upload(Guid licenseId, [FromForm] ResourceDocumentUploadForm form, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.Title) || form.Title.Length > 160)
            return BadRequest(new { Result = "InvalidTitle", Errors = "Informe um titulo valido." });
        if (!Enum.TryParse<ResourceDocumentCategoryEnum>(form.Category, out var category))
            return BadRequest(new { Result = "InvalidCategory", Errors = "Categoria invalida." });
        if (form.File is null || form.File.Length == 0)
            return BadRequest(new { Result = "FileRequired", Errors = "Selecione o PDF do documento." });
        if (!string.Equals(form.File.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { Result = "InvalidFileType", Errors = "O arquivo deve ser um PDF." });

        await using var stream = new MemoryStream();
        await form.File.CopyToAsync(stream, cancellationToken);
        var sourceBytes = stream.ToArray();

        try
        {
            pdf.CountPages(sourceBytes);
        }
        catch (Exception)
        {
            return BadRequest(new { Result = "InvalidPdf", Errors = "Nao foi possivel ler o PDF enviado." });
        }

        string reference;
        try
        {
            reference = await store.StoreAsync(licenseId, sourceBytes, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { Result = "FileTooLarge", Errors = "O documento deve ter no maximo 2 MB." });
        }

        var actorId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedActor) ? parsedActor : Guid.Empty;
        var actorName = User.FindFirstValue("name") ?? User.Identity?.Name ?? "Administracao";
        var now = DateTime.UtcNow;

        var document = new ResourceDocumentDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Category = category,
            Title = form.Title.Trim(),
            Description = (form.Description ?? string.Empty).Trim(),
            StorageReference = reference,
            UploadedByUserId = actorId,
            UploadedByName = actorName,
            PublishedAt = now,
            CreatedAt = now
        };
        context.ResourceDocuments.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        var links = await context.ResidentUnitLinks.AsNoTracking()
            .Where(x => x.Unit.Block.LicenseId == licenseId)
            .ToListAsync(cancellationToken);
        foreach (var residentId in ResolveLicenseNotificationTargets(links, now))
        {
            await notifier.NotifyResidentAsync(
                residentId,
                MobileNotificationCategory.Operational,
                "Novo documento disponivel",
                $"Novo documento disponivel: {document.Title}.",
                "/documentos",
                $"document-published:{document.Id:N}",
                cancellationToken);
        }

        return Ok(ToOut(document));
    }

    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> List(Guid licenseId, CancellationToken cancellationToken)
    {
        var documents = await context.ResourceDocuments.AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderByDescending(x => x.PublishedAt)
            .ToListAsync(cancellationToken);

        return Ok(documents.Select(ToOut).ToList());
    }

    [HttpGet("{documentId:guid}/file")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> GetFile(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.ResourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(licenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }

    [HttpDelete("{documentId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDocuments)]
    public async Task<IActionResult> Delete(Guid licenseId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await context.ResourceDocuments
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == licenseId, cancellationToken);
        if (document is null) return NotFound();

        await store.DeleteAsync(licenseId, document.StorageReference, cancellationToken);
        context.ResourceDocuments.Remove(document);
        await context.SaveChangesAsync(cancellationToken);
        return Ok(new { Result = "Deleted" });
    }

    private static ResourceDocumentOut ToOut(ResourceDocumentDTO document) => new()
    {
        Id = document.Id,
        Category = document.Category.ToString(),
        Title = document.Title,
        Description = document.Description,
        UploadedByName = document.UploadedByName,
        PublishedAt = document.PublishedAt
    };

    /// <summary>
    /// Quem deve ser notificado sobre um documento recem-publicado: um morador
    /// por vinculo VIGENTE (mesma regra de
    /// <see cref="ResidentAuthorizationService.LinkIsCurrentlyValid"/>) em
    /// qualquer unidade desta licenca, deduplicado (o mesmo morador com vinculo
    /// em duas unidades da mesma licenca aparece uma unica vez). Puro e recebendo
    /// <paramref name="now"/> explicitamente para ser testavel sem banco.
    /// </summary>
    internal static IReadOnlyCollection<Guid> ResolveLicenseNotificationTargets(
        IEnumerable<ResidentUnitLinkDTO> links,
        DateTime now) =>
        links
            .Where(link => ResidentAuthorizationService.LinkIsCurrentlyValid(link, now))
            .Select(link => link.ResidentId)
            .Distinct()
            .ToList();
}
```

- [ ] **Step 4: Testes da seleção de destinatários (função pura)**

Create `CondotifyAPI.Tests/ResourceDocumentNotificationTests.cs`:

```csharp
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Resident;

namespace CondotifyAPI.Tests;

public sealed class ResourceDocumentNotificationTests
{
    private static readonly DateTime Now = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ResidentA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ResidentUnitLinkDTO Link(Guid residentId, bool isActive = true, DateTime? startsAt = null, DateTime? endsAt = null) => new()
    {
        Id = Guid.NewGuid(),
        ResidentId = residentId,
        UnitId = Guid.NewGuid(),
        IsActive = isActive,
        StartsAt = startsAt ?? Now.AddDays(-30),
        EndsAt = endsAt,
        CreatedAt = Now,
        UpdatedAt = Now
    };

    [Fact]
    public void ResolveLicenseNotificationTargets_ReturnsResidentWithCurrentLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets([Link(ResidentA)], Now);

        Assert.Equal([ResidentA], result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesEndedLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, endsAt: Now.AddDays(-1))], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesInactiveLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, isActive: false)], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ExcludesNotYetStartedLink()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA, startsAt: Now.AddDays(1))], Now);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_DeduplicatesSameResidentInTwoUnits()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA), Link(ResidentA)], Now);

        Assert.Equal([ResidentA], result);
    }

    [Fact]
    public void ResolveLicenseNotificationTargets_ReturnsMultipleDistinctResidents()
    {
        var result = ResourceDocumentsController.ResolveLicenseNotificationTargets(
            [Link(ResidentA), Link(ResidentB)], Now);

        Assert.Equal(2, result.Count);
        Assert.Contains(ResidentA, result);
        Assert.Contains(ResidentB, result);
    }
}
```

- [ ] **Step 5: Testes por reflexão do controller**

Create `CondotifyAPI.Tests/ResourceDocumentsControllerTests.cs`:

```csharp
using CondotifyAPI.Controllers;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResourceDocumentsControllerTests
{
    [Fact]
    public void Controller_RequiresAuthorization()
    {
        var authorize = typeof(ResourceDocumentsController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true);
        Assert.Single(authorize);
    }

    [Theory]
    [InlineData(nameof(ResourceDocumentsController.Upload), typeof(HttpPostAttribute), null)]
    [InlineData(nameof(ResourceDocumentsController.List), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResourceDocumentsController.GetFile), typeof(HttpGetAttribute), "{documentId:guid}/file")]
    [InlineData(nameof(ResourceDocumentsController.Delete), typeof(HttpDeleteAttribute), "{documentId:guid}")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string? route)
    {
        var method = typeof(ResourceDocumentsController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);

        var permission = Assert.Single(method.GetCustomAttributes<RequireLicensePermissionAttribute>(inherit: true));
        Assert.Equal(LicensePermissionEnum.ManageDocuments, Assert.IsType<LicensePermissionEnum>(Assert.Single(permission.Arguments!)));
    }
}
```

- [ ] **Step 6: Build e testes**

Run: `dotnet build Condotify.sln && dotnet test CondotifyAPI.Tests --filter "ResourceDocument"`
Expected: build limpo, todos os testes passam. Depois, `dotnet test CondotifyAPI.Tests` completo para confirmar que nada regrediu.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Data/Documents CondotifyAPI/Controllers/ResourceDocumentsController.cs CondotifyAPI.Tests/ResourceDocumentNotificationTests.cs CondotifyAPI.Tests/ResourceDocumentsControllerTests.cs
git commit -m "feat(documentos): add staff upload/list/delete endpoints with license-wide notification"
```

---

### Task 5: Controller do morador — listagem e download

**Files:**
- Create: `CondotifyAPI/Data/Documents/ResidentResourceDocumentDtos.cs`
- Create: `CondotifyAPI/Controllers/ResidentResourceDocumentsController.cs`
- Create: `CondotifyAPI.Tests/ResidentResourceDocumentsControllerTests.cs`

**Interfaces:**
- Consumes: `IResourceDocumentStore` (Tarefa 3), `ResourceDocumentDTO` (Tarefa 2), `IResidentAuthorizationService.GetGrantAsync` (já existe) — usa só `grant.LicenseId`, não `grant.UnitIds` (documento não e escopado por unidade, diferente de Boletos).
- Produces: rotas `GET api/resident/documents`, `GET api/resident/documents/{documentId:guid}/file` — consumidas pela Tarefa 7.

- [ ] **Step 1: Contrato de saida**

Create `CondotifyAPI/Data/Documents/ResidentResourceDocumentDtos.cs`:

```csharp
namespace CondotifyAPI.Data.Documents;

public sealed class ResidentResourceDocumentOut
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
```

- [ ] **Step 2: Controller**

Create `CondotifyAPI/Controllers/ResidentResourceDocumentsController.cs`:

```csharp
using CondotifyAPI.Data.Documents;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize(Policy = "Resident")]
[Route("api/resident/documents")]
public sealed class ResidentResourceDocumentsController(
    DatabaseContext context,
    IResidentAuthorizationService authorization,
    IResourceDocumentStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var documents = await context.ResourceDocuments.AsNoTracking()
            .Where(x => x.LicenseId == grant.LicenseId)
            .OrderByDescending(x => x.PublishedAt)
            .Select(x => new ResidentResourceDocumentOut
            {
                Id = x.Id,
                Category = x.Category.ToString(),
                Title = x.Title,
                Description = x.Description,
                PublishedAt = x.PublishedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpGet("{documentId:guid}/file")]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken cancellationToken)
    {
        var grant = await authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var document = await context.ResourceDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == documentId && x.LicenseId == grant.LicenseId, cancellationToken);
        if (document is null) return NotFound();

        var bytes = await store.ReadAsync(grant.LicenseId, document.StorageReference, cancellationToken);
        return bytes is null ? NotFound() : File(bytes, "application/pdf");
    }
}
```

- [ ] **Step 3: Testes por reflexão**

Create `CondotifyAPI.Tests/ResidentResourceDocumentsControllerTests.cs`:

```csharp
using CondotifyAPI.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace CondotifyAPI.Tests;

public sealed class ResidentResourceDocumentsControllerTests
{
    [Fact]
    public void Controller_RequiresTheResidentPolicy()
    {
        var authorize = Assert.Single(typeof(ResidentResourceDocumentsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Equal("Resident", authorize.Policy);
        Assert.Empty(typeof(ResidentResourceDocumentsController)
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
    }

    [Theory]
    [InlineData(nameof(ResidentResourceDocumentsController.List), typeof(HttpGetAttribute), null)]
    [InlineData(nameof(ResidentResourceDocumentsController.Download), typeof(HttpGetAttribute), "{documentId:guid}/file")]
    public void Actions_UseExpectedRouteAndVerb(string actionName, Type httpAttributeType, string? route)
    {
        var method = typeof(ResidentResourceDocumentsController).GetMethod(actionName);

        Assert.NotNull(method);
        var httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method!.GetCustomAttributes(httpAttributeType, inherit: true).Single());
        Assert.Equal(route, httpAttribute.Template);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }
}
```

- [ ] **Step 4: Build e testes**

Run: `dotnet build Condotify.sln && dotnet test CondotifyAPI.Tests --filter ResidentResourceDocumentsControllerTests`
Expected: build limpo, todos os testes passam.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Data/Documents/ResidentResourceDocumentDtos.cs CondotifyAPI/Controllers/ResidentResourceDocumentsController.cs CondotifyAPI.Tests/ResidentResourceDocumentsControllerTests.cs
git commit -m "feat(documentos): add resident list/download endpoints scoped by license"
```

---

### Task 6: Portal web — lista, filtro por categoria e upload

**Files:**
- Create: `Condotify.Contracts/DocumentViewModels.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Create: `Condotify/Components/Dialogs/DocumentUploadDialog.razor`
- Create: `Condotify/Components/LicenseModules/DocumentsModule.razor`
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor`

**Interfaces:**
- Consumes: `POST/GET/DELETE api/access/licenses/{licenseId}/documents`, `GET .../documents/{documentId}/file` (Tarefa 4).
- Produces: `ResourceDocumentViewModel` (Contracts) e os métodos `GetDocumentsAsync`/`UploadDocumentAsync`/`DeleteDocumentAsync`/`GetDocumentFileAsync` do `CondotifyApiClient` — consumidos pela Tarefa 7 (a última reaproveitada por lá, ver nota no Step 2).

- [ ] **Step 1: ViewModel compartilhado**

Create `Condotify.Contracts/DocumentViewModels.cs` — **antes de escrever, confirmar o namespace real do projeto** (abrir `Condotify.Contracts/FinanceViewModels.cs` e copiar a linha `namespace` de lá — durante Boletos descobrimos que o `RootNamespace` do `.csproj` é `Condotify.Models`, não `Condotify.Contracts`, apesar do nome do projeto):

```csharp
namespace Condotify.Models;

public sealed class ResourceDocumentViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}

public sealed class ResidentResourceDocumentViewModel
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime PublishedAt { get; set; }
}
```

(se o namespace real for outro, usar o real — o importante é bater com o resto do projeto, não com o texto literal acima).

- [ ] **Step 2: Métodos no CondotifyApiClient**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, adicionar (perto dos métodos de Boletos, por exemplo logo depois deles):

```csharp
    public Task<ApiResult<List<ResourceDocumentViewModel>>> GetDocumentsAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<ResourceDocumentViewModel>>($"api/access/licenses/{licenseId}/documents", cancellationToken);

    public async Task<ApiResult<ResourceDocumentViewModel>> UploadDocumentAsync(
        Guid licenseId,
        string category,
        string title,
        string description,
        string fileName,
        byte[] fileBytes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = await CreateClientAsync(cancellationToken);
            using var content = new MultipartFormDataContent
            {
                { new StringContent(category), "Category" },
                { new StringContent(title), "Title" },
                { new StringContent(description ?? string.Empty), "Description" }
            };
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            content.Add(fileContent, "File", fileName);

            using var response = await client.PostAsync(BuildApiUrl($"api/access/licenses/{licenseId}/documents"), content, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return ApiResult<ResourceDocumentViewModel>.Fail(await ReadErrorAsync(response, "Nao foi possivel enviar o documento."), response.StatusCode);

            var value = await response.Content.ReadFromJsonAsync<ResourceDocumentViewModel>(GetJsonOptions, cancellationToken);
            return value is null
                ? ApiResult<ResourceDocumentViewModel>.Fail("Nao foi possivel interpretar a resposta da API.", response.StatusCode)
                : ApiResult<ResourceDocumentViewModel>.Ok(value, response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ApiResult<ResourceDocumentViewModel>.Fail("Operação cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enviar documento para a licenca {LicenseId}", licenseId);
            return ApiResult<ResourceDocumentViewModel>.Fail("A API está indisponível. Tente novamente em instantes.");
        }
    }

    public Task<ApiResult<bool>> DeleteDocumentAsync(Guid licenseId, Guid documentId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/documents/{documentId}", cancellationToken);

    public Task<ApiResult<string>> GetDocumentFileAsync(Guid licenseId, Guid documentId, CancellationToken cancellationToken = default) =>
        GetPdfDataUrlAsync($"api/access/licenses/{licenseId}/documents/{documentId}/file", cancellationToken);
```

`GetPdfDataUrlAsync` já existe nesta classe (criado durante Boletos, Tarefa 10 daquele plano) — reaproveitar, não redeclarar. Conferir antes de colar que ele so tem UMA declaração no arquivo depois desta edição.

- [ ] **Step 3: Dialog de upload**

Create `Condotify/Components/Dialogs/DocumentUploadDialog.razor` — mesmo esqueleto do `BoletoUploadDialog.razor`/`BoletoSingleUploadDialog.razor` (ambos já no repo, usar como referência de estrutura: `@inject CondotifyApiClient Api`, `InputFile`, classes CSS `.entity-dialog`/`.dialog-description`/`.import-source`/`.file-button` já existentes):

```razor
@inject CondotifyApiClient Api

<MudDialog Class="entity-dialog">
    <DialogContent>
        <div class="dialog-description">
            <MudIcon Icon="@Icons.Material.Outlined.Description" />
            <div>
                <strong>Novo documento</strong>
                <span>Fica visível pra todos os moradores desta licença assim que enviado.</span>
            </div>
        </div>

        @if (!string.IsNullOrWhiteSpace(_error))
        {
            <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert>
        }

        <MudSelect T="string" Label="Categoria" @bind-Value="_category" Class="mb-3">
            @foreach (var option in CategoryOptions)
            {
                <MudSelectItem T="string" Value="option.Value">@option.Label</MudSelectItem>
            }
        </MudSelect>
        <MudTextField T="string" Label="Título" @bind-Value="_title" Class="mb-3" MaxLength="160" />
        <MudTextField T="string" Label="Descrição (opcional)" @bind-Value="_description" Class="mb-3" Lines="2" MaxLength="1000" />

        <div class="import-source">
            <div class="import-source-copy">
                <MudIcon Icon="@Icons.Material.Outlined.PictureAsPdf" />
                <div>
                    <strong>@(string.IsNullOrWhiteSpace(_fileName) ? "Selecione o PDF" : _fileName)</strong>
                    <span>Limite de 2 MB.</span>
                </div>
            </div>
            <label class="file-button @(_submitting ? "disabled" : null)">
                <MudIcon Icon="@Icons.Material.Outlined.UploadFile" />
                <span>@(_fileBytes is null ? "Selecionar PDF" : "Trocar arquivo")</span>
                <InputFile OnChange="ReadFileAsync" accept=".pdf,application/pdf" disabled="@_submitting" />
            </label>
        </div>
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" Disabled="_submitting" OnClick="Cancel">Cancelar</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.CloudUpload"
                   Disabled="@(!CanSubmit || _submitting)" OnClick="SubmitAsync">
            @(_submitting ? "Enviando..." : "Publicar documento")
        </MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }

    private const long MaxFileSize = 2_000_000;
    private static readonly (string Value, string Label)[] CategoryOptions =
    [
        ("Minutes", "Ata"),
        ("ByLaws", "Regimento Interno"),
        ("Covenant", "Convenção"),
        ("Announcement", "Comunicado"),
        ("FinancialStatement", "Prestação de Contas"),
        ("Other", "Outro")
    ];

    private string _category = "Announcement";
    private string _title = string.Empty;
    private string _description = string.Empty;
    private string _fileName = string.Empty;
    private byte[]? _fileBytes;
    private bool _submitting;
    private string? _error;

    private bool CanSubmit => !string.IsNullOrWhiteSpace(_title) && _fileBytes is { Length: > 0 };

    private void Cancel() => Dialog.Cancel();

    private async Task ReadFileAsync(InputFileChangeEventArgs args)
    {
        _error = null;
        var file = args.File;
        if (file.Size > MaxFileSize)
        {
            _error = "O arquivo excede o limite de 2 MB.";
            return;
        }

        await using var stream = file.OpenReadStream(MaxFileSize);
        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        _fileBytes = buffer.ToArray();
        _fileName = file.Name;
    }

    private async Task SubmitAsync()
    {
        if (!CanSubmit) return;
        _submitting = true;
        _error = null;
        try
        {
            var result = await Api.UploadDocumentAsync(LicenseId, _category, _title.Trim(), _description.Trim(), _fileName, _fileBytes!);
            if (!result.Success || result.Value is null)
            {
                _error = result.Error ?? "Não foi possível enviar o documento.";
                return;
            }
            Dialog.Close(DialogResult.Ok(result.Value));
        }
        finally
        {
            _submitting = false;
        }
    }
}
```

- [ ] **Step 4: Módulo — lista com filtro por categoria**

Create `Condotify/Components/LicenseModules/DocumentsModule.razor` — segue o padrão sem `PageState` (componente que não existe no portal web, só no mobile — já corrigido uma vez durante Boletos, não reintroduzir), mesmo `@if (_loading) {...} else if (...) {...} else {...}` de `BoletosModule.razor`:

```razor
@inject CondotifyApiClient Api
@inject IDialogService DialogService
@inject ISnackbar Snackbar
@inject IJSRuntime JS

@if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }

@if (_loading)
{
    <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div>
}
else
{
    <section class="content-panel">
        <div class="panel-heading">
            <div><MudText Typo="Typo.h5">Documentos</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">Visíveis a todos os moradores desta licença</MudText></div>
            <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.UploadFile" OnClick="OpenUploadAsync" Disabled="!CanManage">Novo documento</MudButton>
        </div>

        <MudSelect T="string" Label="Filtrar por categoria" Value="_categoryFilter" ValueChanged="OnCategoryFilterChanged" Class="mb-3" Clearable Dense>
            @foreach (var option in CategoryOptions)
            {
                <MudSelectItem T="string" Value="option.Value">@option.Label</MudSelectItem>
            }
        </MudSelect>

        @if (FilteredDocuments.Count == 0)
        {
            <div class="empty-state compact-empty"><MudIcon Icon="@Icons.Material.Outlined.Description" Size="Size.Large" Color="Color.Primary" /><MudText Typo="Typo.subtitle1">Nenhum documento @(string.IsNullOrWhiteSpace(_categoryFilter) ? "publicado" : "nesta categoria")</MudText></div>
        }
        else
        {
            <MudTable Items="FilteredDocuments" Hover Dense Elevation="0">
                <HeaderContent>
                    <MudTh>Título</MudTh>
                    <MudTh>Categoria</MudTh>
                    <MudTh>Publicado em</MudTh>
                    <MudTh>Enviado por</MudTh>
                    <MudTh></MudTh>
                </HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="Título"><div class="list-main"><div class="list-title">@context.Title</div>@if (!string.IsNullOrWhiteSpace(context.Description)) { <div class="list-meta">@context.Description</div> }</div></MudTd>
                    <MudTd DataLabel="Categoria">@CategoryLabel(context.Category)</MudTd>
                    <MudTd DataLabel="Publicado em">@context.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy")</MudTd>
                    <MudTd DataLabel="Enviado por">@context.UploadedByName</MudTd>
                    <MudTd>
                        <MudTooltip Text="Abrir"><MudIconButton Icon="@Icons.Material.Outlined.OpenInNew" Size="Size.Small" OnClick="() => OpenFileAsync(context.Id)" /></MudTooltip>
                        <MudTooltip Text="Excluir"><MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error" Disabled="!CanManage" OnClick="() => DeleteAsync(context.Id)" /></MudTooltip>
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
    </section>
}

@code {
    [Parameter] public Guid LicenseId { get; set; }
    [Parameter] public bool CanManage { get; set; }

    private static readonly (string Value, string Label)[] CategoryOptions =
    [
        ("Minutes", "Ata"),
        ("ByLaws", "Regimento Interno"),
        ("Covenant", "Convenção"),
        ("Announcement", "Comunicado"),
        ("FinancialStatement", "Prestação de Contas"),
        ("Other", "Outro")
    ];

    private List<ResourceDocumentViewModel> _documents = [];
    private string _categoryFilter = string.Empty;
    private bool _loading;
    private string? _error;

    private List<ResourceDocumentViewModel> FilteredDocuments => string.IsNullOrWhiteSpace(_categoryFilter)
        ? _documents
        : _documents.Where(x => x.Category == _categoryFilter).ToList();

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        var result = await Api.GetDocumentsAsync(LicenseId);
        _loading = false;
        if (result.Success) _documents = result.Value ?? [];
        else _error = result.Error;
    }

    private void OnCategoryFilterChanged(string value) => _categoryFilter = value;

    private async Task OpenUploadAsync()
    {
        var parameters = new DialogParameters { [nameof(DocumentUploadDialog.LicenseId)] = LicenseId };
        var dialog = await DialogService.ShowAsync<DocumentUploadDialog>("Novo documento", parameters,
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        if (result is { Canceled: false }) await LoadAsync();
    }

    private async Task OpenFileAsync(Guid documentId)
    {
        var result = await Api.GetDocumentFileAsync(LicenseId, documentId);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
        {
            Snackbar.Add(result.Error ?? "Não foi possível abrir o documento.", Severity.Error);
            return;
        }
        await JS.InvokeVoidAsync("portalInterop.downloadBase64", "documento.pdf", StripDataUrlPrefix(result.Value), "application/pdf");
    }

    private async Task DeleteAsync(Guid documentId)
    {
        var result = await Api.DeleteDocumentAsync(LicenseId, documentId);
        if (!result.Success)
        {
            Snackbar.Add(result.Error ?? "Não foi possível excluir o documento.", Severity.Error);
            return;
        }
        await LoadAsync();
    }

    private static string? StripDataUrlPrefix(string dataUrl)
    {
        var separator = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return separator < 0 ? null : dataUrl[(separator + "base64,".Length)..];
    }

    private static string CategoryLabel(string category) => CategoryOptions.FirstOrDefault(x => x.Value == category).Label is { Length: > 0 } label ? label : category;
}
```

- [ ] **Step 5: Wiring no LicenseWorkspace**

Em `Condotify/Components/Pages/LicenseWorkspace.razor`:

1. No `<nav>`, logo após a linha do `boletos` (adicionada durante o plano de Boletos):
```razor
        @if (Has(LicensePermission.ManageDocuments)) { @NavButton("documentos", "Documentos", Icons.Material.Outlined.Description) }
```
2. No `@switch (CurrentSection)`, logo após o `case "boletos":`:
```razor
            case "documentos":
                <DocumentsModule LicenseId="LicenseId" CanManage="@Has(LicensePermission.ManageDocuments)" />
                break;
```
3. Em `DefaultSection`, adicionar uma linha antes de `: Has(LicensePermission.ViewUsers) ...`:
```csharp
        : Has(LicensePermission.ManageDocuments) ? "documentos"
```
4. Em `SectionAllowed`, adicionar antes do `"administracao" =>`:
```csharp
        "documentos" => Has(LicensePermission.ManageDocuments),
```

- [ ] **Step 6: Build**

Run: `dotnet build Condotify.sln`
Expected: build limpo.

- [ ] **Step 7: Commit**

```bash
git add Condotify.Contracts/DocumentViewModels.cs Condotify.ApiClient/CondotifyApiClient.cs Condotify/Components/Dialogs/DocumentUploadDialog.razor Condotify/Components/LicenseModules/DocumentsModule.razor Condotify/Components/Pages/LicenseWorkspace.razor
git commit -m "feat(documentos): add web portal document list, filter and upload"
```

---

### Task 7: App mobile — visão do morador

**Files:**
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Create: `Condotify.Mobile/Components/Pages/Documentos.razor`
- Modify: `Condotify.Mobile/Components/Pages/More.razor`

**Interfaces:**
- Consumes: `GET api/resident/documents`, `GET api/resident/documents/{documentId}/file` (Tarefa 5); `ResidentResourceDocumentViewModel` (Tarefa 6).

- [ ] **Step 1: Métodos no CondotifyApiClient**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, junto dos outros métodos `api/resident/...`. Reaproveita o helper privado `GetPdfDataUrlAsync` já existente nesta classe (não declarar de novo):

```csharp
    public Task<ApiResult<List<ResidentResourceDocumentViewModel>>> GetResidentDocumentsAsync(CancellationToken cancellationToken = default) =>
        GetAsync<List<ResidentResourceDocumentViewModel>>("api/resident/documents", cancellationToken);

    public Task<ApiResult<string>> GetResidentDocumentFileAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        GetPdfDataUrlAsync($"api/resident/documents/{documentId}/file", cancellationToken);
```

- [ ] **Step 2: Página do morador**

Create `Condotify.Mobile/Components/Pages/Documentos.razor` — mesmo padrão de `Condotify.Mobile/Components/Pages/Boletos.razor` (já no repo, usar como referência: `PageHeader`/`PageState` reais deste projeto, download via `Share.Default.RequestAsync` em vez de `window.open`/JS blob — essa é a correção já aplicada em Boletos após o teste em aparelho confirmar que a versão antiga não funcionava, não reintroduzir o padrão quebrado):

```razor
@page "/documentos"
@inject CondotifyApiClient Api
@inject ISnackbar Snackbar

<PageTitle>Documentos | Condotify</PageTitle>
<PageHeader Eyebrow="CONDOMÍNIO" Title="Documentos" Subtitle="Atas, regimento, convenção e comunicados publicados pela administração.">
    <Actions><MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" aria-label="Atualizar" /></Actions>
</PageHeader>

<PageState Loading="_loading" Error="@_error" Empty="@(_documents.Count == 0)" EmptyTitle="Nenhum documento disponível" EmptyText="Quando a administração publicar um documento, ele aparece aqui." Retry="LoadAsync">
    <section class="list-group documents-list">
        @foreach (var document in _documents)
        {
            <div class="list-row documents-row">
                <span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Description" /></span>
                <div class="list-main">
                    <div class="list-title">@document.Title</div>
                    <div class="list-meta">@CategoryLabel(document.Category) · @document.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy")</div>
                </div>
                <MudIconButton Icon="@Icons.Material.Outlined.Download" Disabled="@(_opening == document.Id)" OnClick="() => OpenAsync(document)" aria-label="@($"Abrir {document.Title}")" />
            </div>
        }
    </section>
</PageState>

@code {
    private static readonly (string Value, string Label)[] CategoryOptions =
    [
        ("Minutes", "Ata"),
        ("ByLaws", "Regimento Interno"),
        ("Covenant", "Convenção"),
        ("Announcement", "Comunicado"),
        ("FinancialStatement", "Prestação de Contas"),
        ("Other", "Outro")
    ];

    private List<ResidentResourceDocumentViewModel> _documents = [];
    private bool _loading;
    private Guid? _opening;
    private string _error = string.Empty;

    protected override Task OnInitializedAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = string.Empty;
        var result = await Api.GetResidentDocumentsAsync();
        _loading = false;
        if (result.Success) _documents = result.Value ?? [];
        else _error = result.Error ?? "Não foi possível carregar os documentos.";
    }

    private async Task OpenAsync(ResidentResourceDocumentViewModel document)
    {
        _opening = document.Id;
        var result = await Api.GetResidentDocumentFileAsync(document.Id);
        _opening = null;
        if (!result.Success || string.IsNullOrWhiteSpace(result.Value))
        {
            Snackbar.Add(result.Error ?? "Não foi possível abrir o documento.", Severity.Error);
            return;
        }

        var base64 = StripDataUrlPrefix(result.Value!);
        if (base64 is null)
        {
            Snackbar.Add("Não foi possível abrir o documento.", Severity.Error);
            return;
        }

        try
        {
            var path = Path.Combine(FileSystem.CacheDirectory, $"{SanitizeFileName(document.Title)}.pdf");
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(base64));
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = document.Title,
                File = new ShareFile(path)
            });
        }
        catch (Exception)
        {
            Snackbar.Add("Não foi possível abrir o documento.", Severity.Error);
        }
    }

    private static string? StripDataUrlPrefix(string dataUrl)
    {
        var separator = dataUrl.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
        return separator < 0 ? null : dataUrl[(separator + "base64,".Length)..];
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':']).ToHashSet();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "documento" : cleaned;
    }

    private static string CategoryLabel(string category) => CategoryOptions.FirstOrDefault(x => x.Value == category).Label is { Length: > 0 } label ? label : category;
}
```

`FileSystem`, `Share`, `ShareFileRequest` e `ShareFile` não precisam de `@using` extra neste arquivo — já vêm de `_Imports.razor` (confirmado direto em `Condotify.Mobile/Components/Pages/Boletos.razor`, que usa a mesma API sem nenhum `@using` próprio além de `CondotifyApiClient`/`ISnackbar`).

- [ ] **Step 3: Wiring no menu "Mais"**

Em `Condotify.Mobile/Components/Pages/More.razor`, na seção `else` (bloco de morador), adicionar uma linha junto do item "Meus boletos" (adicionado durante o plano de Boletos):

```razor
                <a class="list-row" href="/documentos"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Description" /></span><div class="list-main"><div class="list-title">Documentos</div><div class="list-meta">Atas, regimento, comunicados e mais</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
```

- [ ] **Step 4: Build**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android35.0`
Expected: build limpo.

- [ ] **Step 5: Commit**

```bash
git add Condotify.ApiClient/CondotifyApiClient.cs Condotify.Mobile/Components/Pages/Documentos.razor Condotify.Mobile/Components/Pages/More.razor
git commit -m "feat(documentos): add mobile resident view with native share download"
```

---

## Verificação final (depois de todas as tarefas)

- [ ] `dotnet build Condotify.sln` — build limpo.
- [ ] `dotnet test CondotifyAPI.Tests` — suíte completa passando (não só os testes novos).
- [ ] Rodar a API + Postgres localmente, aplicar a migração `AddResourceDocuments` num banco de desenvolvimento (nunca produção sem confirmação explícita), e testar manualmente pelo menos um fluxo ponta-a-ponta: publicar um documento de teste pelo portal, conferir que aparece pro morador certo no app, baixar (folha de compartilhamento nativa), excluir e confirmar que some da lista do morador.
