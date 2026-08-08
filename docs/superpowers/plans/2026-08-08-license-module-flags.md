# Feature Flags de Módulos por Condomínio — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que um administrador da plataforma (Developer/Admin) ligue/desligue, por condomínio, 10 dos 15 módulos do workspace — ocultando-os do portal web e do app mobile — sem tocar em permissões individuais de usuário.

**Architecture:** Bitmask `EnabledModules` (long) na tabela `Licenses`, espelhado em dois enums `[Flags]` paralelos (`CondotifyAPI.Domain.Enums.License.LicenseModuleEnum` no servidor, `Condotify.Models.LicenseModuleEnum` nos contratos de cliente — o servidor nunca é referenciado pelo portal/mobile, então os dois têm que existir separadamente, mesmo padrão de duplicação que `LicenseSummaryViewModel`/`LicenseFullViewModel` já usam hoje). O valor viaja embutido nas respostas de licença que portal e mobile já buscam; só a escrita ganha um endpoint novo, restrito a Developer/Admin.

**Tech Stack:** ASP.NET Core (CondotifyAPI), EF Core + Npgsql, Blazor Server + MudBlazor (Condotify), MAUI Blazor Hybrid (Condotify.Mobile), xUnit.

## Global Constraints

- Escopo por `License` (condomínio), nunca por usuário/perfil.
- Nesta v1, a desativação só oculta na UI — nenhum controller de API passa a rejeitar chamadas por módulo desativado.
- Só usuários com claim/AccessType `Developer` ou `Admin` podem ligar/desligar módulos — síndico/gestor da licença nunca vê esse controle.
- Portal **e** mobile respeitam o flag.
- Todo condomínio (existente ou novo) nasce com os 10 módulos opcionais ligados — `EnabledModules` default = `LicenseModuleEnum.All` (1023).
- Módulos sempre ativos (fora do bitmask): Visão geral, Estrutura, Credenciais, Administração, Acessos.
- Módulos opcionais (bitmask, ordem fixa dos bits): Câmeras(0), Equipamentos(1), Rotas(2), Ocorrências(3), Automações(4), Emergência(5), Encomendas(6), Agendamento(7), Boletos(8), Documentos(9).

---

## Task 1: Schema do servidor — `LicenseModuleEnum`, coluna e migration

**Files:**
- Create: `CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs`
- Modify: `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs`
- Modify: `CondotifyAPI.Infrastructure/ContextConfiguration/License/LicenseConfiguration.cs`
- Create: migration via `dotnet ef migrations add AddLicenseEnabledModules --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`
- Modify: `CondotifyAPI.Tests/DatabaseModelTests.cs`

**Interfaces:**
- Produces: `LicenseModuleEnum` (namespace `CondotifyAPI.Domain.Enums.License`), `LicenseDTO.EnabledModules` (`long`), default value `LicenseModuleEnum.All` = `1023`. Consumido pelas Tasks 2 e 4.

- [ ] **Step 1: Criar o enum**

```csharp
// CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs
namespace CondotifyAPI.Domain.Enums.License;

// Espelha Condotify.Models.LicenseModuleEnum (Condotify.Contracts) bit a bit.
// Mudar um sem mudar o outro quebra a leitura do bitmask no cliente.
[Flags]
public enum LicenseModuleEnum : long
{
    None = 0,
    Cameras = 1L << 0,
    Devices = 1L << 1,
    Routes = 1L << 2,
    Incidents = 1L << 3,
    Automations = 1L << 4,
    Emergency = 1L << 5,
    Deliveries = 1L << 6,
    Bookings = 1L << 7,
    Finance = 1L << 8,
    Documents = 1L << 9,
    All = (1L << 10) - 1
}
```

- [ ] **Step 2: Escrever o teste do bitmask (falha primeiro)**

Adicione ao final de `CondotifyAPI.Tests/DatabaseModelTests.cs`, dentro da classe `DatabaseModelTests`, antes do fechamento (`}` na linha 486):

```csharp
[Fact]
public void LicenseModuleEnum_AllCoversExactlyTheTenOptionalModules()
{
    var expected = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Devices
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Routes
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Incidents
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Automations
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Emergency
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Deliveries
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Finance
        | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Documents;

    Assert.Equal(expected, CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All);
    Assert.Equal(1023L, (long)CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All);
}

[Fact]
public void License_EnabledModulesDefaultsToAllOptionalModules()
{
    using var context = CreateContext();
    var entity = context.Model.FindEntityType(typeof(CondotifyAPI.Domain.DTO.License.LicenseDTO));

    Assert.NotNull(entity);
    var property = entity!.FindProperty(nameof(CondotifyAPI.Domain.DTO.License.LicenseDTO.EnabledModules));
    Assert.NotNull(property);
    Assert.Equal(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All, property!.GetDefaultValue());
}
```

- [ ] **Step 3: Rodar os testes para confirmar que falham**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseModuleEnum_AllCoversExactlyTheTenOptionalModules|FullyQualifiedName~License_EnabledModulesDefaultsToAllOptionalModules"`
Expected: FAIL — `LicenseModuleEnum` ainda não existe / `EnabledModules` ainda não existe em `LicenseDTO`.

- [ ] **Step 4: Adicionar a coluna ao `LicenseDTO`**

Em `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs`, adicionar após a linha `public string Code { get; set; } = string.Empty;` (linha 30):

```csharp
        public CondotifyAPI.Domain.Enums.License.LicenseModuleEnum EnabledModules { get; set; } = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All;
```

- [ ] **Step 5: Configurar a coluna no EF Core**

Em `CondotifyAPI.Infrastructure/ContextConfiguration/License/LicenseConfiguration.cs`, adicionar logo após o bloco `builder.Property(l => l.UnitLabelPlural)...` (linha 27), antes de `builder.Property(l => l.ExpireDate)`:

```csharp
            builder.Property(l => l.EnabledModules)
                .HasConversion(new ValueConverter<CondotifyAPI.Domain.Enums.License.LicenseModuleEnum, long>(
                    x => (long)x,
                    x => (CondotifyAPI.Domain.Enums.License.LicenseModuleEnum)x))
                .HasDefaultValue(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All)
                .IsRequired();
```

(Este arquivo já usa `Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter` para `Organization`/`Building` — confirme que o `using` já existe no topo do arquivo antes de compilar.)

- [ ] **Step 6: Gerar e aplicar a migration**

Run: `dotnet ef migrations add AddLicenseEnabledModules --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI --output-dir Migrations`
Run: `CONDOTIFY_DB_CONNECTION="Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres" dotnet ef database update --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`
Expected: migration aplica sem erro; `SELECT "EnabledModules" FROM "Licenses" LIMIT 1;` no banco local retorna `1023` para licenças existentes.

- [ ] **Step 7: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseModuleEnum_AllCoversExactlyTheTenOptionalModules|FullyQualifiedName~License_EnabledModulesDefaultsToAllOptionalModules"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs CondotifyAPI.Domain/DTO/License/LicenseDTO.cs CondotifyAPI.Infrastructure/ContextConfiguration/License/LicenseConfiguration.cs CondotifyAPI.Infrastructure/Migrations/*AddLicenseEnabledModules* CondotifyAPI.Infrastructure/Migrations/DatabaseContextModelSnapshot.cs CondotifyAPI.Tests/DatabaseModelTests.cs
git commit -m "feat(db): add EnabledModules bitmask to License, default all-on"
```

---

## Task 2: Propagar `EnabledModules` pela cadeia domínio → resposta de API

**Files:**
- Modify: `CondotifyAPI.Domain/Models/License/License.cs`
- Modify: `CondotifyAPI/ViewModel/LicenseSummaryViewModel.cs`
- Modify: `CondotifyAPI/Data/Licenses/LicenseSummaryDto.cs`
- Modify: `CondotifyAPI/Query/GetLicensesSummariesByUserQuery.cs` (método `ToSummary`)
- Modify: `CondotifyAPI/Data/Login/ResidentProfileDtos.cs` (`ResidentMeOut`)
- Modify: `CondotifyAPI/Controllers/ResidentProfileController.cs`
- Modify: `CondotifyAPI.Tests/LicenseSummaryTests.cs`

**Interfaces:**
- Consumes: `LicenseDTO.EnabledModules` (Task 1), mapeado automaticamente para `License.EnabledModules` via `CreateMap<LicenseDTO, License>()` já existente em `CondotifyAPI.Infrastructure/Mapping/CondotifyProfile.cs:34` (mesmo nome/tipo, sem `.ForMember` extra necessário).
- Produces: `LicenseSummaryViewModel.EnabledModules`, `LicenseSummaryDto.EnabledModules`, `ResidentMeOut.EnabledModules` (todos `long`) — respostas de `GET api/access/licenses/{id}`, `GET api/access/licenses` (lista) e `GET api/resident/me`. Consumido pela Task 3 (contratos de cliente) e pelas Tasks 5/7/8 (filtros de UI).

- [ ] **Step 1: `License.cs` (modelo de domínio)**

Em `CondotifyAPI.Domain/Models/License/License.cs`, adicionar após `public string Code { get; set; } = string.Empty;` (linha 15):

```csharp
        public CondotifyAPI.Domain.Enums.License.LicenseModuleEnum EnabledModules { get; set; } = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.All;
```

- [ ] **Step 2: Escrever o teste de propagação (falha primeiro)**

Adicionar a `CondotifyAPI.Tests/LicenseSummaryTests.cs`:

```csharp
[Fact]
public void Summary_ShouldExposeEnabledModulesFromDomain()
{
    var license = new License
    {
        Name = "Condomínio com módulos",
        Code = "TEST-02",
        City = "Salvador",
        Country = "Brasil",
        Blocks = [],
        EnabledModules = CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings
    };

    var summary = GetLicenseSummariesByUserQueryHandler.ToSummary(license);

    Assert.Equal((long)(CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Cameras | CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Bookings), summary.EnabledModules);
}
```

- [ ] **Step 3: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~Summary_ShouldExposeEnabledModulesFromDomain"`
Expected: FAIL — `LicenseSummaryDto` ainda não tem `EnabledModules`.

- [ ] **Step 4: `LicenseSummaryDto` (usado pela listagem `GET api/access/licenses`)**

Em `CondotifyAPI/Data/Licenses/LicenseSummaryDto.cs`, adicionar um campo `public long EnabledModules { get; set; }` na classe.

- [ ] **Step 5: `ToSummary` em `GetLicensesSummariesByUserQuery.cs`**

Em `CondotifyAPI/Query/GetLicensesSummariesByUserQuery.cs`, no método `ToSummary` (linha 38-52), adicionar dentro do inicializador de objeto, após `Estado = license.Country`:

```csharp
                Estado = license.Country,
                EnabledModules = (long)license.EnabledModules
```

- [ ] **Step 6: `LicenseSummaryViewModel` (usado por `GET api/access/licenses/{id}`)**

Em `CondotifyAPI/ViewModel/LicenseSummaryViewModel.cs`, adicionar `public long EnabledModules { get; set; }` à classe, e dentro de `FromDomain` (após `IsExpired = license.IsExpired(),`):

```csharp
                IsExpired = license.IsExpired(),
                EnabledModules = (long)license.EnabledModules,
```

- [ ] **Step 7: Rodar o teste de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~Summary_ShouldExposeEnabledModulesFromDomain"`
Expected: PASS.

- [ ] **Step 8: `ResidentMeOut` + `ResidentProfileController.Me`**

Em `CondotifyAPI/Data/Login/ResidentProfileDtos.cs`, adicionar `public long EnabledModules { get; set; }` a `ResidentMeOut`.

Em `CondotifyAPI/Controllers/ResidentProfileController.cs`, dentro de `Me` (método que começa na linha 43), adicionar logo após a consulta de `licenseName` (depois da linha 67, antes do `return Ok(...)`):

```csharp
        var enabledModules = await _context.Licenses.AsNoTracking()
            .Where(x => x.Id == grant.LicenseId)
            .Select(x => (long)x.EnabledModules)
            .FirstOrDefaultAsync(cancellationToken);
```

E adicionar `EnabledModules = enabledModules,` dentro do `new ResidentMeOut { ... }` (logo após `LicenseName = licenseName,` na linha 73).

- [ ] **Step 9: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/condotify-plan-check`
Expected: build limpo. Depois: `rm -rf /tmp/condotify-plan-check`.

- [ ] **Step 10: Rodar toda a suíte**

Run: `dotnet test CondotifyAPI.Tests`
Expected: todos os testes passam (nenhuma regressão nos ~570 existentes).

- [ ] **Step 11: Commit**

```bash
git add CondotifyAPI.Domain/Models/License/License.cs CondotifyAPI/ViewModel/LicenseSummaryViewModel.cs CondotifyAPI/Data/Licenses/LicenseSummaryDto.cs CondotifyAPI/Query/GetLicensesSummariesByUserQuery.cs CondotifyAPI/Data/Login/ResidentProfileDtos.cs CondotifyAPI/Controllers/ResidentProfileController.cs CondotifyAPI.Tests/LicenseSummaryTests.cs
git commit -m "feat(api): expose EnabledModules on license and resident profile responses"
```

---

## Task 3: Contratos de cliente (`Condotify.Contracts`)

**Files:**
- Create: `Condotify.Contracts/LicenseModuleEnum.cs`
- Modify: `Condotify.Contracts/LicenseFullViewModel.cs`
- Modify: `Condotify.Contracts/LicenseViewModel.cs`
- Modify: `Condotify.Contracts/ResidentMobileViewModels.cs`

**Interfaces:**
- Produces: `Condotify.Models.LicenseModuleEnum` (mesmos valores de bit do enum do servidor, Task 1), `LicenseFullViewModel.EnabledModules`, `LicenseViewModel.EnabledModules`, `ResidentProfileViewModel.EnabledModules` (todos `long`, desserializados diretamente do JSON produzido na Task 2 — nomes de propriedade já batem, nenhum mapeamento extra necessário). Consumido pelas Tasks 5, 6, 7, 8.

- [ ] **Step 1: Criar o enum espelho**

```csharp
// Condotify.Contracts/LicenseModuleEnum.cs
namespace Condotify.Models;

// Espelha CondotifyAPI.Domain.Enums.License.LicenseModuleEnum bit a bit.
// Mudar um sem mudar o outro quebra a leitura do bitmask no cliente.
[Flags]
public enum LicenseModuleEnum : long
{
    None = 0,
    Cameras = 1L << 0,
    Devices = 1L << 1,
    Routes = 1L << 2,
    Incidents = 1L << 3,
    Automations = 1L << 4,
    Emergency = 1L << 5,
    Deliveries = 1L << 6,
    Bookings = 1L << 7,
    Finance = 1L << 8,
    Documents = 1L << 9,
    All = (1L << 10) - 1
}
```

- [ ] **Step 2: Adicionar o campo aos três view models**

Em `Condotify.Contracts/LicenseFullViewModel.cs`, dentro de `LicenseFullViewModel`, após `public bool IsExpired { get; set; }` (linha 17):

```csharp
        public long EnabledModules { get; set; }
```

Em `Condotify.Contracts/LicenseViewModel.cs`, dentro de `LicenseViewModel`, após `public int ProjetoId { get; set; }` (linha 11):

```csharp
        public long EnabledModules { get; set; }
```

Em `Condotify.Contracts/ResidentMobileViewModels.cs`, dentro de `ResidentProfileViewModel`, após `public bool AllowResidentDigitalPass { get; set; } = true;` (linha 23):

```csharp
    public long EnabledModules { get; set; }
```

- [ ] **Step 3: Build**

Run: `dotnet build Condotify.Contracts/Condotify.Contracts.csproj`
Expected: build limpo.

- [ ] **Step 4: Commit**

```bash
git add Condotify.Contracts/LicenseModuleEnum.cs Condotify.Contracts/LicenseFullViewModel.cs Condotify.Contracts/LicenseViewModel.cs Condotify.Contracts/ResidentMobileViewModels.cs
git commit -m "feat(contracts): mirror LicenseModuleEnum and expose EnabledModules to clients"
```

---

## Task 4: Endpoint de escrita — `PUT api/access/licenses/{id}/modules`

**Files:**
- Create: `CondotifyAPI/Data/Licenses/UpdateLicenseModulesIn.cs`
- Modify: `CondotifyAPI/Controllers/LicenseAccessController.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Create: `Condotify.Contracts/UpdateLicenseModulesOut.cs`
- Create: `CondotifyAPI.Tests/LicenseAccessControllerTests.cs`

**Interfaces:**
- Consumes: `LicenseDTO.EnabledModules` (Task 1).
- Produces: `LicenseAccessController.CanManageModules(UserAccessDTO?, Guid)` (`internal static bool`, testável sem banco), endpoint `PUT api/access/licenses/{id:guid}/modules`, `CondotifyApiClient.UpdateLicenseModulesAsync(Guid, long, CancellationToken)`. Consumido pela Task 6.

- [ ] **Step 1: DTO de request**

```csharp
// CondotifyAPI/Data/Licenses/UpdateLicenseModulesIn.cs
namespace CondotifyAPI.Data.Licenses;

public sealed class UpdateLicenseModulesIn
{
    public long EnabledModules { get; set; }
}
```

- [ ] **Step 2: Escrever os testes (falham primeiro)**

```csharp
// CondotifyAPI.Tests/LicenseAccessControllerTests.cs
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Enums.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CondotifyAPI.Tests;

public sealed class LicenseAccessControllerTests
{
    [Fact]
    public void UpdateModules_HasExpectedRouteVerbAndAuthorization()
    {
        var method = typeof(LicenseAccessController).GetMethod(nameof(LicenseAccessController.UpdateModules));

        Assert.NotNull(method);
        var route = Assert.IsType<HttpPutAttribute>(
            Assert.Single(method!.GetCustomAttributes(typeof(HttpPutAttribute), inherit: true)));
        Assert.Equal("{id:guid}/modules", route.Template);
        Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    private static UserAccessDTO User(Guid enterpriseId, AccessTypeEnum type) => new()
    {
        Id = Guid.NewGuid(),
        EnterpriseId = enterpriseId,
        AccessType = type,
        Email = "user@condotify.local"
    };

    [Theory]
    [InlineData(AccessTypeEnum.Developer, true)]
    [InlineData(AccessTypeEnum.Admin, true)]
    [InlineData(AccessTypeEnum.Manager, false)]
    [InlineData(AccessTypeEnum.Editor, false)]
    [InlineData(AccessTypeEnum.Viewer, false)]
    [InlineData(AccessTypeEnum.Default, false)]
    public void CanManageModules_OnlyDeveloperOrAdminOfSameEnterprise(AccessTypeEnum type, bool expected)
    {
        var enterpriseId = Guid.NewGuid();
        var user = User(enterpriseId, type);

        Assert.Equal(expected, LicenseAccessController.CanManageModules(user, enterpriseId));
    }

    [Fact]
    public void CanManageModules_RejectsDifferentEnterprise()
    {
        var user = User(Guid.NewGuid(), AccessTypeEnum.Admin);

        Assert.False(LicenseAccessController.CanManageModules(user, Guid.NewGuid()));
    }

    [Fact]
    public void CanManageModules_RejectsNullUser()
    {
        Assert.False(LicenseAccessController.CanManageModules(null, Guid.NewGuid()));
    }
}
```

- [ ] **Step 3: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAccessControllerTests"`
Expected: FAIL — `UpdateModules`/`CanManageModules` ainda não existem.

- [ ] **Step 4: Implementar o endpoint**

Em `CondotifyAPI/Controllers/LicenseAccessController.cs`, adicionar logo após o método `CreateByEnterprise` (que termina por volta da linha 140, antes do fechamento da classe). Confirme os `using` já existentes no topo do arquivo (`System.Security.Claims`, `Microsoft.EntityFrameworkCore` — `CreateByEnterprise` já os usa, então já devem estar presentes):

```csharp
    [HttpPut("{id:guid}/modules")]
    [Authorize]
    public async Task<IActionResult> UpdateModules(Guid id, [FromBody] CondotifyAPI.Data.Licenses.UpdateLicenseModulesIn input)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
            !Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId))
            return Forbid();

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (!CanManageModules(user, enterpriseId))
            return Forbid();

        var license = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id);
        if (license is null) return NotFound();

        license.EnabledModules = (CondotifyAPI.Domain.Enums.License.LicenseModuleEnum)input.EnabledModules;
        await _context.SaveChangesAsync();

        return Ok(new { EnabledModules = (long)license.EnabledModules });
    }

    // Extraido para ser testavel sem banco: CreateByEnterprise faz a mesma
    // checagem inline (unico outro lugar que restringe uma acao a Developer/
    // Admin da propria enterprise); aqui vira um predicado puro reutilizavel.
    internal static bool CanManageModules(CondotifyAPI.Domain.DTO.Users.UserAccessDTO? user, Guid enterpriseId) =>
        user is not null &&
        user.EnterpriseId == enterpriseId &&
        user.AccessType is CondotifyAPI.Domain.Enums.Users.AccessTypeEnum.Admin or CondotifyAPI.Domain.Enums.Users.AccessTypeEnum.Developer;
```

- [ ] **Step 5: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAccessControllerTests"`
Expected: PASS.

- [ ] **Step 6: Cliente HTTP**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, adicionar logo após `GetStructureAsync` (linha 353-354):

```csharp
    public Task<ApiResult<UpdateLicenseModulesOut>> UpdateLicenseModulesAsync(Guid licenseId, long enabledModules, CancellationToken cancellationToken = default) =>
        SendForAsync<UpdateLicenseModulesOut>(HttpMethod.Put, $"api/access/licenses/{licenseId}/modules", new { EnabledModules = enabledModules }, cancellationToken);
```

E em `Condotify.Contracts` (mesmo arquivo da Task 3, `LicenseModuleEnum.cs`, ou um arquivo novo `UpdateLicenseModulesOut.cs` — use um arquivo novo para não misturar enum com view model):

```csharp
// Condotify.Contracts/UpdateLicenseModulesOut.cs
namespace Condotify.Models;

public sealed class UpdateLicenseModulesOut
{
    public long EnabledModules { get; set; }
}
```

- [ ] **Step 7: Build de todo o backend + toda a suíte**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/condotify-plan-check && rm -rf /tmp/condotify-plan-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, todos os testes passam.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI/Data/Licenses/UpdateLicenseModulesIn.cs CondotifyAPI/Controllers/LicenseAccessController.cs Condotify.ApiClient/CondotifyApiClient.cs Condotify.Contracts/UpdateLicenseModulesOut.cs CondotifyAPI.Tests/LicenseAccessControllerTests.cs
git commit -m "feat(api): add PUT licenses/{id}/modules, restricted to platform Developer/Admin"
```

---

## Task 5: Portal — filtrar abas do `LicenseWorkspace.razor`

**Files:**
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor`

**Interfaces:**
- Consumes: `LicenseFullViewModel.EnabledModules` (Task 3), já carregado em `_license` por `OnParametersSetAsync` (linha 169-176 do arquivo).

- [ ] **Step 1: Adicionar o módulo a cada `WorkspaceTab`**

Em `Condotify/Components/Pages/LicenseWorkspace.razor`, mudar o record (linha 112):

```csharp
    private sealed record WorkspaceTab(string Section, string Label, string Icon, LicensePermission Permission, Condotify.Models.LicenseModuleEnum? Module = null);
```

E preencher `Module` nos 10 tabs opcionais dentro de `TabGroups` (linhas 117-141) — os 5 tabs sem módulo correspondente (`estrutura`, `credenciais`, `administracao`, `acessos`, e o `PinnedTab` "visao-geral") ficam sem o parâmetro (default `null`):

```csharp
    private static readonly (string GroupLabel, WorkspaceTab[] Tabs)[] TabGroups =
    [
        ("Monitoramento", [
            new("cameras", "Câmeras", Icons.Material.Outlined.Videocam, LicensePermission.ViewDevices, Condotify.Models.LicenseModuleEnum.Cameras),
            new("equipamentos", "Equipamentos", Icons.Material.Outlined.Sensors, LicensePermission.ViewDevices, Condotify.Models.LicenseModuleEnum.Devices),
            new("rotas", "Rotas", Icons.Material.Outlined.AltRoute, LicensePermission.ViewDevices, Condotify.Models.LicenseModuleEnum.Routes),
            new("acessos", "Acessos", Icons.Material.Outlined.FactCheck, LicensePermission.ViewEvents),
        ]),
        ("Operação", [
            new("ocorrencias", "Ocorrências", Icons.Material.Outlined.AssignmentLate, LicensePermission.ViewIncidents, Condotify.Models.LicenseModuleEnum.Incidents),
            new("automacoes", "Automações", Icons.Material.Outlined.AutoAwesome, LicensePermission.ViewAutomations, Condotify.Models.LicenseModuleEnum.Automations),
            new("emergencia", "Emergência", Icons.Material.Outlined.HealthAndSafety, LicensePermission.ViewEmergency, Condotify.Models.LicenseModuleEnum.Emergency),
            new("encomendas", "Encomendas", Icons.Material.Outlined.Inventory2, LicensePermission.ViewDeliveries, Condotify.Models.LicenseModuleEnum.Deliveries),
            new("agendamento", "Agendamento", Icons.Material.Outlined.Deck, LicensePermission.ViewBookings, Condotify.Models.LicenseModuleEnum.Bookings),
        ]),
        ("Financeiro & Documentos", [
            new("boletos", "Boletos", Icons.Material.Outlined.ReceiptLong, LicensePermission.ManageFinance, Condotify.Models.LicenseModuleEnum.Finance),
            new("documentos", "Documentos", Icons.Material.Outlined.Description, LicensePermission.ManageDocuments, Condotify.Models.LicenseModuleEnum.Documents),
        ]),
        ("Configuração", [
            new("estrutura", "Estrutura", Icons.Material.Outlined.AccountTree, LicensePermission.ViewStructure),
            new("credenciais", "Credenciais", Icons.Material.Outlined.Badge, LicensePermission.ViewCredentials),
            new("administracao", "Administração", Icons.Material.Outlined.AdminPanelSettings, LicensePermission.ViewUsers),
        ]),
    ];
```

- [ ] **Step 2: Adicionar o critério de módulo ao filtro de visibilidade**

Adicionar um novo método privado e usar no filtro `visible` (linha 36):

```csharp
    private bool IsModuleEnabled(Condotify.Models.LicenseModuleEnum? module) =>
        module is null || (_license?.EnabledModules & (long)module.Value) != 0;
```

Trocar a linha 36 de:
```csharp
                var visible = tabs.Where(t => t.Section == "administracao" ? AdministracaoVisible : Has(t.Permission)).ToList();
```
para:
```csharp
                var visible = tabs.Where(t => (t.Section == "administracao" ? AdministracaoVisible : Has(t.Permission)) && IsModuleEnabled(t.Module)).ToList();
```

- [ ] **Step 3: Bloquear acesso direto por URL a módulo desativado**

Em `SectionAllowed` (linhas 197-213), envolver o `switch` existente:

```csharp
    private bool SectionAllowed(string section) => IsModuleEnabled(ModuleFor(section)) && section switch
    {
        "visao-geral" => Has(LicensePermission.ViewDashboard),
        "equipamentos" or "rotas" or "cameras" => Has(LicensePermission.ViewDevices),
        "estrutura" => Has(LicensePermission.ViewStructure),
        "credenciais" => Has(LicensePermission.ViewCredentials),
        "acessos" => Has(LicensePermission.ViewEvents),
        "ocorrencias" => Has(LicensePermission.ViewIncidents),
        "automacoes" => Has(LicensePermission.ViewAutomations),
        "emergencia" => Has(LicensePermission.ViewEmergency),
        "encomendas" => Has(LicensePermission.ViewDeliveries),
        "agendamento" => Has(LicensePermission.ViewBookings),
        "boletos" => Has(LicensePermission.ManageFinance),
        "documentos" => Has(LicensePermission.ManageDocuments),
        "administracao" => Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) || Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts),
        _ => false
    };

    private static Condotify.Models.LicenseModuleEnum? ModuleFor(string section) => section switch
    {
        "cameras" => Condotify.Models.LicenseModuleEnum.Cameras,
        "equipamentos" => Condotify.Models.LicenseModuleEnum.Devices,
        "rotas" => Condotify.Models.LicenseModuleEnum.Routes,
        "ocorrencias" => Condotify.Models.LicenseModuleEnum.Incidents,
        "automacoes" => Condotify.Models.LicenseModuleEnum.Automations,
        "emergencia" => Condotify.Models.LicenseModuleEnum.Emergency,
        "encomendas" => Condotify.Models.LicenseModuleEnum.Deliveries,
        "agendamento" => Condotify.Models.LicenseModuleEnum.Bookings,
        "boletos" => Condotify.Models.LicenseModuleEnum.Finance,
        "documentos" => Condotify.Models.LicenseModuleEnum.Documents,
        _ => null
    };
```

- [ ] **Step 4: Ajustar `DefaultSection` para pular módulos desativados**

Em `DefaultSection` (linhas 150-163), cada ramo já testado por `Has(...)` também precisa checar `IsModuleEnabled(ModuleFor(...))`. Reescrever:

```csharp
    private string DefaultSection => Has(LicensePermission.ViewDashboard) ? "visao-geral"
        : Has(LicensePermission.ViewStructure) ? "estrutura"
        : Has(LicensePermission.ViewDevices) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Devices) ? "equipamentos"
        : Has(LicensePermission.ViewCredentials) ? "credenciais"
        : Has(LicensePermission.ViewEvents) ? "acessos"
        : Has(LicensePermission.ViewIncidents) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Incidents) ? "ocorrencias"
        : Has(LicensePermission.ViewAutomations) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Automations) ? "automacoes"
        : Has(LicensePermission.ViewEmergency) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Emergency) ? "emergencia"
        : Has(LicensePermission.ViewDeliveries) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Deliveries) ? "encomendas"
        : Has(LicensePermission.ViewBookings) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Bookings) ? "agendamento"
        : Has(LicensePermission.ManageFinance) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Finance) ? "boletos"
        : Has(LicensePermission.ManageDocuments) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Documents) ? "documentos"
        : Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) || Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts) ? "administracao"
        : "sem-acesso";
```

(`equipamentos`/`rotas`/`cameras` compartilham `ViewDevices`; `DefaultSection` só precisa de um caminho de entrada então mantém apenas `equipamentos` como já fazia — sem mudança de comportamento aí além do novo filtro de módulo.)

- [ ] **Step 5: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/condotify-plan-check && rm -rf /tmp/condotify-plan-check`
Expected: build limpo.

- [ ] **Step 6: Verificação manual**

Suba a API e o portal localmente (`dotnet run --project CondotifyAPI`, `dotnet run --project Condotify`), abra um condomínio, confirme que todas as abas aparecem normalmente (default `All`). Depois, via `psql` ou uma chamada `PUT` manual contra o endpoint da Task 4, desligue `Cameras` (`EnabledModules = 1023 - 1 = 1022`) e confirme que a aba "Câmeras" some do menu e que `/licencas/{id}/cameras` direto na URL mostra o aviso "sem permissão".

- [ ] **Step 7: Commit**

```bash
git add Condotify/Components/Pages/LicenseWorkspace.razor
git commit -m "feat(portal): hide workspace tabs for modules disabled on the license"
```

---

## Task 6: Portal — seção "Módulos" (admin da plataforma)

**Files:**
- Modify: `Condotify/Components/LicenseModules/AdministrationModule.razor`

**Interfaces:**
- Consumes: `CondotifyApiClient.UpdateLicenseModulesAsync` (Task 4), `_license.EnabledModules` (carregado pelo componente pai `LicenseWorkspace.razor` e hoje não repassado a `AdministrationModule` — precisa virar um `[Parameter]`).

- [ ] **Step 1: Repassar `EnabledModules` do pai para o módulo**

Em `Condotify/Components/Pages/LicenseWorkspace.razor`, no `case "administracao":` (linha 98-100), trocar:
```csharp
            case "administracao":
                <AdministrationModule LicenseId="LicenseId" InitialData="_administration" />
                break;
```
por:
```csharp
            case "administracao":
                <AdministrationModule LicenseId="LicenseId" InitialData="_administration" EnabledModules="_license.EnabledModules" OnModulesChanged="@(value => _license.EnabledModules = value)" />
                break;
```

- [ ] **Step 2: Adicionar a claim-check e o novo parâmetro em `AdministrationModule.razor`**

No bloco `@code` (linha 54 em diante), adicionar:

```csharp
    [Parameter] public long EnabledModules { get; set; }
    [Parameter] public EventCallback<long> OnModulesChanged { get; set; }
    private bool _isPlatformAdmin;
    private bool _savingModules;
```

E em `@inject`, no topo do arquivo, adicionar:
```razor
@inject Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider AuthStateProvider
```

Em `OnInitializedAsync` (linha 69), adicionar a checagem antes do `if (InitialData is not null)`:

```csharp
    protected override async Task OnInitializedAsync()
    {
        var user = (await AuthStateProvider.GetAuthenticationStateAsync()).User;
        _isPlatformAdmin = user.FindFirst("access_type")?.Value is "Admin" or "Developer";
        if (InitialData is not null) { _data = InitialData; _loading = false; } else await LoadAsync();
    }
```

(Mesma claim `access_type` que `NewLicense.razor:59` já usa para restringir a criação de condomínio.)

- [ ] **Step 3: Método de salvar**

Adicionar ao `@code`:

```csharp
    private static readonly (Condotify.Models.LicenseModuleEnum Module, string Label)[] ModuleOptions =
    [
        (Condotify.Models.LicenseModuleEnum.Cameras, "Câmeras"),
        (Condotify.Models.LicenseModuleEnum.Devices, "Equipamentos"),
        (Condotify.Models.LicenseModuleEnum.Routes, "Rotas"),
        (Condotify.Models.LicenseModuleEnum.Incidents, "Ocorrências"),
        (Condotify.Models.LicenseModuleEnum.Automations, "Automações"),
        (Condotify.Models.LicenseModuleEnum.Emergency, "Emergência"),
        (Condotify.Models.LicenseModuleEnum.Deliveries, "Encomendas"),
        (Condotify.Models.LicenseModuleEnum.Bookings, "Agendamento"),
        (Condotify.Models.LicenseModuleEnum.Finance, "Boletos"),
        (Condotify.Models.LicenseModuleEnum.Documents, "Documentos"),
    ];

    private bool IsModuleOn(Condotify.Models.LicenseModuleEnum module) => (EnabledModules & (long)module) != 0;

    private async Task ToggleModuleAsync(Condotify.Models.LicenseModuleEnum module, bool value)
    {
        var next = value ? EnabledModules | (long)module : EnabledModules & ~(long)module;
        _savingModules = true;
        var result = await Api.UpdateLicenseModulesAsync(LicenseId, next);
        _savingModules = false;
        if (!result.Success) { Snackbar.Add(result.Error ?? "Falha ao atualizar os módulos.", Severity.Error); return; }
        EnabledModules = result.Value?.EnabledModules ?? next;
        await OnModulesChanged.InvokeAsync(EnabledModules);
        Snackbar.Add("Módulos atualizados.", Severity.Success);
    }
```

- [ ] **Step 4: Markup da nova aba**

Adicionar um novo `MudTabPanel` dentro de `<MudTabs Elevation="0" Class="administration-tabs">` (após o `@if (CanViewAlerts) { ... }` que fecha na linha 48, antes de `</MudTabs>` na linha 49):

```razor
                @if (_isPlatformAdmin)
                {
                    <MudTabPanel Text="Módulos" Icon="@Icons.Material.Outlined.ToggleOn">
                        <div class="admin-tab-head"><div><strong>Módulos ativos deste condomínio</strong><span>Controle exclusivo da plataforma — o síndico não vê nem altera esta lista.</span></div></div>
                        <div class="policy-switches">
                            @foreach (var option in ModuleOptions)
                            {
                                <MudSwitch T="bool" Value="@IsModuleOn(option.Module)" ValueChanged="@(value => ToggleModuleAsync(option.Module, value))" Disabled="_savingModules" Label="@option.Label" Color="Color.Primary" />
                            }
                        </div>
                    </MudTabPanel>
                }
```

- [ ] **Step 5: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/condotify-plan-check && rm -rf /tmp/condotify-plan-check`
Expected: build limpo.

- [ ] **Step 6: Verificação manual**

Logado como um usuário `Admin`/`Developer`, abrir Administração → aba "Módulos" deve aparecer; desligar "Boletos" e confirmar snackbar de sucesso + a aba "Boletos" some do menu lateral sem recarregar a página (graças ao `OnModulesChanged`). Logado como síndico (sem `access_type` Admin/Developer), confirmar que a aba "Módulos" não aparece.

- [ ] **Step 7: Commit**

```bash
git add Condotify/Components/Pages/LicenseWorkspace.razor Condotify/Components/LicenseModules/AdministrationModule.razor
git commit -m "feat(portal): add platform-admin-only Módulos toggle section"
```

---

## Task 7: Mobile — filtrar a navegação inferior (`MobileNavigation.For`)

**Files:**
- Modify: `Condotify.Mobile.Core/MobileNavigation.cs`
- Create: `Condotify.Mobile.Tests/MobileNavigationTests.cs`

**Interfaces:**
- Consumes: `Condotify.Models.LicenseModuleEnum` (Task 3).
- Produces: nova sobrecarga `MobileNavigation.For(MobilePrincipalKind, long enabledModules)`. Consumido pela Task 8.

- [ ] **Step 1: Escrever os testes (falham primeiro)**

```csharp
// Condotify.Mobile.Tests/MobileNavigationTests.cs
using Condotify.Mobile.Core;
using Condotify.Models;

namespace Condotify.Mobile.Tests;

public sealed class MobileNavigationTests
{
    [Fact]
    public void For_Staff_WithAllModulesEnabled_IncludesCameras()
    {
        var items = MobileNavigation.For(MobilePrincipalKind.Staff, (long)LicenseModuleEnum.All);

        Assert.Contains(items, x => x.Route == "/cameras");
    }

    [Fact]
    public void For_Staff_WithCamerasDisabled_ExcludesCamerasButKeepsCore()
    {
        var enabled = (long)(LicenseModuleEnum.All & ~LicenseModuleEnum.Cameras);

        var items = MobileNavigation.For(MobilePrincipalKind.Staff, enabled);

        Assert.DoesNotContain(items, x => x.Route == "/cameras");
        Assert.Contains(items, x => x.Route == "/home");
        Assert.Contains(items, x => x.Route == "/concierge");
        Assert.Contains(items, x => x.Route == "/more");
    }

    [Fact]
    public void For_Resident_WithBookingsDisabled_ExcludesBookingsButKeepsCore()
    {
        var enabled = (long)(LicenseModuleEnum.All & ~LicenseModuleEnum.Bookings);

        var items = MobileNavigation.For(MobilePrincipalKind.Resident, enabled);

        Assert.DoesNotContain(items, x => x.Route == "/bookings");
        Assert.Contains(items, x => x.Route == "/home");
        Assert.Contains(items, x => x.Route == "/visitors");
    }

    [Fact]
    public void For_DefaultsToAllModulesWhenOverloadOmitted()
    {
        var items = MobileNavigation.For(MobilePrincipalKind.Staff);

        Assert.Contains(items, x => x.Route == "/cameras");
    }
}
```

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test Condotify.Mobile.Tests --filter "FullyQualifiedName~MobileNavigationTests"`
Expected: FAIL — a sobrecarga com `enabledModules` ainda não existe.

- [ ] **Step 3: Implementar o filtro**

Reescrever `Condotify.Mobile.Core/MobileNavigation.cs`:

```csharp
using Condotify.Models;

namespace Condotify.Mobile.Core;

public sealed record MobileNavigationItem(string Route, string Label, string IconKey, LicenseModuleEnum? Module = null);

public static class MobileNavigation
{
    private static readonly MobileNavigationItem[] StaffItems =
    [
        new("/home", "Início", "home"),
        new("/concierge", "Portaria", "concierge"),
        new("/cameras", "Câmeras", "camera", LicenseModuleEnum.Cameras),
        new("/more", "Mais", "more")
    ];

    private static readonly MobileNavigationItem[] ResidentItems =
    [
        new("/home", "Início", "home"),
        new("/visitors", "Visitantes", "visitors"),
        new("/bookings", "Reservas", "calendar", LicenseModuleEnum.Bookings),
        new("/more", "Mais", "more")
    ];

    public static IReadOnlyList<MobileNavigationItem> For(MobilePrincipalKind principal) =>
        For(principal, (long)LicenseModuleEnum.All);

    public static IReadOnlyList<MobileNavigationItem> For(MobilePrincipalKind principal, long enabledModules)
    {
        var items = principal == MobilePrincipalKind.Resident ? ResidentItems : StaffItems;
        return items.Where(x => x.Module is null || (enabledModules & (long)x.Module.Value) != 0).ToList();
    }

    public static bool TryResolveDeepLink(string? value, out string route) =>
        MobileDeepLinks.TryNormalize(value, out route);
}
```

- [ ] **Step 4: Rodar os testes de novo**

Run: `dotnet test Condotify.Mobile.Tests --filter "FullyQualifiedName~MobileNavigationTests"`
Expected: PASS.

- [ ] **Step 5: Rodar toda a suíte do mobile (garantir que nada quebrou)**

Run: `dotnet test Condotify.Mobile.Tests`
Expected: todos os testes passam.

- [ ] **Step 6: Commit**

```bash
git add Condotify.Mobile.Core/MobileNavigation.cs Condotify.Mobile.Tests/MobileNavigationTests.cs
git commit -m "feat(mobile): filter bottom navigation by enabled license modules"
```

---

## Task 8: Mobile — `AppState`, `Home.razor`, `MainLayout.razor` e `More.razor`

**Files:**
- Modify: `Condotify.Mobile/Services/MobileAppState.cs`
- Modify: `Condotify.Mobile/Components/Pages/Home.razor`
- Modify: `Condotify.Mobile/Components/Layout/MainLayout.razor`
- Modify: `Condotify.Mobile/Components/Pages/More.razor`

**Interfaces:**
- Consumes: `MobileNavigation.For(MobilePrincipalKind, long)` (Task 7), `LicenseViewModel.EnabledModules`/`ResidentProfileViewModel.EnabledModules` (Task 3).

- [ ] **Step 1: `MobileAppState` — guardar o bitmask do morador**

Em `Condotify.Mobile/Services/MobileAppState.cs`, adicionar após `public event Action? Changed;` (linha 12):

```csharp
    public long ResidentEnabledModules { get; private set; } = (long)Condotify.Models.LicenseModuleEnum.All;

    public void SetResidentModules(long enabledModules)
    {
        if (ResidentEnabledModules == enabledModules) return;
        ResidentEnabledModules = enabledModules;
        Changed?.Invoke();
    }
```

E em `Clear()` (linha 37-43), resetar para o default:
```csharp
    public void Clear()
    {
        Licenses = [];
        SelectedLicenseId = null;
        ResidentEnabledModules = (long)Condotify.Models.LicenseModuleEnum.All;
        Preferences.Default.Remove(LicenseKey);
        Changed?.Invoke();
    }
```

- [ ] **Step 2: `Home.razor` — alimentar o AppState após carregar o perfil do morador**

Abra `Condotify.Mobile/Components/Pages/Home.razor` e localize onde `Api.GetResidentProfileAsync()`/equivalente é chamado e o resultado é atribuído (mesmo carregamento citado no relatório de auditoria mobile, linhas ~32-36 e ~148-167 do arquivo). Logo após a atribuição bem-sucedida do perfil (ex.: `_profile = result.Value;`), adicionar:

```csharp
        if (result.Success && result.Value is not null) AppState.SetResidentModules(result.Value.EnabledModules);
```

(`AppState` já precisa estar `@inject`ado em `Home.razor` — se ainda não estiver, adicionar `@inject MobileAppState AppState` no topo do arquivo.)

- [ ] **Step 3: `MainLayout.razor` — passar o bitmask certo para `MobileNavigation.For`**

Em `Condotify.Mobile/Components/Layout/MainLayout.razor`, trocar a propriedade `NavigationItems` (linha 108):

```csharp
    private IReadOnlyList<MobileNavigationItem> NavigationItems => MobileNavigation.For(
        Session.Current?.Principal ?? MobilePrincipalKind.Staff,
        Session.Current?.Principal == MobilePrincipalKind.Resident
            ? AppState.ResidentEnabledModules
            : AppState.SelectedLicense?.EnabledModules ?? (long)Condotify.Models.LicenseModuleEnum.All);
```

- [ ] **Step 4: `More.razor` — esconder os atalhos de módulos desativados**

Em `Condotify.Mobile/Components/Pages/More.razor`, adicionar ao `@code` (linha 85 em diante):

```csharp
    private long EnabledModules => Session.Current?.Principal == MobilePrincipalKind.Resident
        ? AppState.ResidentEnabledModules
        : AppState.SelectedLicense?.EnabledModules ?? (long)Condotify.Models.LicenseModuleEnum.All;
    private bool ModuleOn(Condotify.Models.LicenseModuleEnum module) => (EnabledModules & (long)module) != 0;
```

E envolver cada link de módulo opcional com `@if (ModuleOn(...))`. Nos "Atalhos" (linhas 21-34):
```razor
                @if (Session.Current?.Principal == MobilePrincipalKind.Staff)
                {
                    <a href="/concierge"><span class="settings-icon primary"><MudIcon Icon="@Icons.Material.Outlined.SensorDoor" /></span><strong>Portaria</strong></a>
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Cameras)) { <a href="/cameras"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Videocam" /></span><strong>Câmeras</strong></a> }
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Deliveries)) { <a href="/deliveries"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></span><strong>Encomendas</strong></a> }
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Bookings)) { <a href="/bookings"><span class="settings-icon success"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" /></span><strong>Reservas</strong></a> }
                }
                else
                {
                    <a href="/visitors"><span class="settings-icon primary"><MudIcon Icon="@Icons.Material.Outlined.PersonAddAlt1" /></span><strong>Visitantes</strong></a>
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Cameras)) { <a href="/cameras"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Videocam" /></span><strong>Câmeras</strong></a> }
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Deliveries)) { <a href="/deliveries"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></span><strong>Encomendas</strong></a> }
                    @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Bookings)) { <a href="/bookings"><span class="settings-icon success"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" /></span><strong>Reservas</strong></a> }
                }
```

Na seção "Operação" (staff, linhas 40-49), envolver `/devices` e `/alerts`:
```razor
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Devices)) { <a class="list-row" href="/devices"><span class="settings-icon success"><MudIcon Icon="@Icons.Material.Outlined.DoorFront" /></span><div class="list-main"><div class="list-title">Equipamentos</div><div class="list-meta">Estado e acionamentos</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Incidents)) { <a class="list-row" href="/alerts"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" /></span><div class="list-main"><div class="list-title">Alertas operacionais</div><div class="list-meta">Ocorrências que exigem atenção</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
```
(`/people` e `/audit` não correspondem a nenhum dos 10 módulos opcionais — ficam sempre visíveis, sem `@if`.)

Na seção "Condomínio" (staff, linhas 51-58), envolver `/deliveries` e `/bookings`:
```razor
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Deliveries)) { <a class="list-row" href="/deliveries"><span class="settings-icon primary"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></span><div class="list-main"><div class="list-title">Encomendas</div><div class="list-meta">Recebimentos e entregas</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Bookings)) { <a class="list-row" href="/bookings"><span class="settings-icon success"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" /></span><div class="list-main"><div class="list-title">Reservas</div><div class="list-meta">Aprovações e agenda de áreas comuns</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
```
(`/licenses` "Trocar condomínio" não é um módulo — fica sempre visível.)

Na seção "Condomínio" (resident, linhas 62-70), envolver `/deliveries`, `/boletos`, `/documentos`, `/cameras`:
```razor
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Deliveries)) { <a class="list-row" href="/deliveries"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></span><div class="list-main"><div class="list-title">Minhas encomendas</div><div class="list-meta">Volumes destinados às suas unidades</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Finance)) { <a class="list-row" href="/boletos"><span class="settings-icon warning"><MudIcon Icon="@Icons.Material.Outlined.ReceiptLong" /></span><div class="list-main"><div class="list-title">Meus boletos</div><div class="list-meta">Boletos disponibilizados pela administração</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Documents)) { <a class="list-row" href="/documentos"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Description" /></span><div class="list-main"><div class="list-title">Documentos</div><div class="list-meta">Atas, regimento, comunicados e mais</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
                @if (ModuleOn(Condotify.Models.LicenseModuleEnum.Cameras)) { <a class="list-row" href="/cameras"><span class="settings-icon info"><MudIcon Icon="@Icons.Material.Outlined.Videocam" /></span><div class="list-main"><div class="list-title">Câmeras compartilhadas</div><div class="list-meta">Visualização autorizada pelo condomínio</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a> }
```

- [ ] **Step 5: Build**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0 -o /tmp/condotify-plan-check && rm -rf /tmp/condotify-plan-check`
Expected: build limpo.

- [ ] **Step 6: Rodar toda a suíte do mobile**

Run: `dotnet test Condotify.Mobile.Tests`
Expected: todos os testes passam.

- [ ] **Step 7: Verificação manual**

Rodar o app mobile (Windows), logar como morador de um condomínio com "Reservas" desligado (via PUT da Task 4) e confirmar que o item "Reservas" some da navegação inferior e do menu "Mais". Repetir como staff com "Câmeras" desligado.

- [ ] **Step 8: Commit**

```bash
git add Condotify.Mobile/Services/MobileAppState.cs Condotify.Mobile/Components/Pages/Home.razor Condotify.Mobile/Components/Layout/MainLayout.razor Condotify.Mobile/Components/Pages/More.razor
git commit -m "feat(mobile): wire enabled-modules bitmask into shell navigation and Mais page"
```

---

## Final check (all tasks complete)

- [ ] `dotnet build Condotify.sln` limpo (usar `-o` para uma pasta temporária vazia se algum processo `dotnet run` estiver ativo, para não colidir com bin/ locked).
- [ ] `dotnet test CondotifyAPI.Tests` — todos passam.
- [ ] `dotnet test Condotify.Mobile.Tests` — todos passam.
- [ ] Fluxo manual completo: como Admin/Developer, desligar 2-3 módulos de um condomínio de teste pela nova aba "Módulos"; confirmar que somem do portal (síndico logado) e do app mobile (staff e morador); religar e confirmar que voltam.
