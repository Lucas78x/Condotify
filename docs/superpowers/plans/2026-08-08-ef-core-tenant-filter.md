# Filtro Global de Tenant no EF Core — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar uma segunda camada de defesa contra vazamento de dados entre licenças (condomínios)/empresas: um filtro global do EF Core que restringe toda consulta às 29 entidades com `LicenseId` direto (mais um caso especial, `OperationalAlertDTO`) ao conjunto de licenças que o usuário autenticado da requisição pode acessar — sem depender de cada controller lembrar de filtrar manualmente.

**Architecture:** Um accessor *scoped* por requisição (`ICurrentTenantAccessor`) populado por um `IAsyncActionFilter` global antes de qualquer action rodar; `DatabaseContext` aplica `HasQueryFilter` a toda entidade que implementa a interface marcadora `ILicenseScoped`, via um loop de reflexão em `OnModelCreating` (não 29 declarações manuais). Dois pontos em `LicenseAuthorizationService` que calculam o próprio conjunto acessível recebem `.IgnoreQueryFilters()` deliberado para quebrar uma dependência circular.

**Tech Stack:** ASP.NET Core (CondotifyAPI), EF Core + Npgsql, xUnit.

## Global Constraints

- O filtro nunca deve remover acesso que um usuário já tem hoje — é estritamente uma segunda camada. Qualquer teste que mostre uma consulta existente retornando MENOS dados que antes é uma regressão, não um resultado esperado.
- `AccessibleLicenseIds == null` (accessor ainda não populado, ou construção fora do pipeline de requisição) deve esconder tudo (fail-closed), nunca revelar tudo.
- As únicas duas exceções `.IgnoreQueryFilters()` esperadas neste subsistema são as citadas na Task 4 (`LicenseAuthorizationService.GetGrantAsync` e `GetLicensePermissionsAsync`). Qualquer outra ocorrência de `.IgnoreQueryFilters()` introduzida por uma task deste plano é um desvio que precisa de justificativa explícita no relatório da task — em particular, os workers de background (Task 2.5) NÃO devem usar `.IgnoreQueryFilters()`; eles usam `ICurrentTenantAccessor.MarkUnrestricted()` no início do próprio escopo, precisamente para não precisar tocar nos serviços compartilhados (`AccessRouteResolver`, `RecycleBinService`, `PlatformPushNotifier`) que também são chamados por controllers HTTP.
- Lista exata das 29 entidades `ILicenseScoped` (não estimativa — levantada por grep e conferida linha a linha): `AccessRouteDTO`, `AccessOperationAuditDTO`, `AccessBatchOperationDTO`, `AccessInventoryItemDTO`, `AccessEventRecordDTO`, `AmenityDTO`, `AmenityBookingDTO`, `ConfigurationBackupDTO`, `BackupAutomationPolicyDTO`, `BlockDTO`, `DeliveryDTO`, `ResourceDocumentDTO`, `AccessControlDeviceDTO`, `CFTVDeviceDTO`, `BoletoBatchDTO`, `AccessVisitDTO`, `AccessWatchlistEntryDTO`, `RegistrationInviteDTO`, `LicenseCredentialPolicyDTO`, `LicenseUserAccessDTO`, `AlertNotificationPolicyDTO`, `AlertNotificationDeliveryDTO`, `IncidentDTO`, `AutomationRuleDTO`, `AutomationExecutionDTO`, `EmergencySessionDTO`, `DigitalPassDTO`, `RecycleBinItemDTO`, `TicketDTO`. `OperationalAlertDTO` (a 30ª) NÃO está nesta lista — recebe filtro próprio na Task 3.
- Testes de integração contra banco real (Postgres local, `condotify-postgres`) são deliberados e esperados nas Tasks 4 e 6 — este subsistema é sobre isolamento entre tenants, o risco de um bug aqui é alto o suficiente para justificar sair do padrão usual do repositório (que evita testes de controller com banco real). Cada teste desses cria seus próprios dados com GUIDs únicos e limpa (`RemoveRange` + `SaveChangesAsync`) no fim, em bloco `finally` ou `IAsyncLifetime.DisposeAsync`.

---

## Task 1: Tipos fundamentais — `ICurrentTenantAccessor`, `ILicenseScoped`

**Files:**
- Create: `CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs`
- Create: `CondotifyAPI.Domain/Interfaces/ILicenseScoped.cs`
- Create: `CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs`
- Create: `CondotifyAPI.Tests/CurrentTenantAccessorTests.cs`

**Interfaces:**
- Produces: `ICurrentTenantAccessor` (namespace `CondotifyAPI.Domain.Interfaces`: `HashSet<Guid>? AccessibleLicenseIds { get; }`, `Guid? AccessibleEnterpriseId { get; }`, `void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId)`), `CurrentTenantAccessor`/`NullCurrentTenantAccessor` (namespace `CondotifyAPI.Domain.Services`), `ILicenseScoped` (namespace `CondotifyAPI.Domain.Interfaces`: `Guid LicenseId { get; }`). Consumido pelas Tasks 2, 3, 4, 5.

**Por que `CondotifyAPI.Domain` e não `CondotifyAPI/Services/Authorization`**: `CondotifyAPI.Infrastructure` (onde `DatabaseContext` mora, Task 2) só referencia `CondotifyAPI.Domain` — nunca o projeto `CondotifyAPI` em si (confirmado lendo os `.csproj`: a direção é `CondotifyAPI` → `CondotifyAPI.Infrastructure` → `CondotifyAPI.Domain`, nunca ao contrário). Se `ICurrentTenantAccessor` morasse em `CondotifyAPI`, `DatabaseContext` não conseguiria referenciá-lo — erro de compilação, não um detalhe de estilo. `CondotifyAPI.Domain` é o único lugar que tanto `CondotifyAPI.Infrastructure` (leitura, em `DatabaseContext`) quanto `CondotifyAPI` (escrita, em `TenantScopeActionFilter`, Task 5) conseguem alcançar.

- [ ] **Step 1: Escrever os testes (falham primeiro)**

```csharp
// CondotifyAPI.Tests/CurrentTenantAccessorTests.cs
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;

namespace CondotifyAPI.Tests;

public sealed class CurrentTenantAccessorTests
{
    [Fact]
    public void CurrentTenantAccessor_StartsWithNullScope()
    {
        var accessor = new CurrentTenantAccessor();

        Assert.Null(accessor.AccessibleLicenseIds);
        Assert.Null(accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void CurrentTenantAccessor_SetAccessibleScope_StoresBothValues()
    {
        var accessor = new CurrentTenantAccessor();
        var licenseIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var enterpriseId = Guid.NewGuid();

        accessor.SetAccessibleScope(licenseIds, enterpriseId);

        Assert.Equal(licenseIds, accessor.AccessibleLicenseIds);
        Assert.Equal(enterpriseId, accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void NullCurrentTenantAccessor_AlwaysReturnsNullScope()
    {
        var accessor = NullCurrentTenantAccessor.Instance;

        Assert.Null(accessor.AccessibleLicenseIds);
        Assert.Null(accessor.AccessibleEnterpriseId);
    }

    [Fact]
    public void NullCurrentTenantAccessor_SetAccessibleScope_Throws()
    {
        var accessor = NullCurrentTenantAccessor.Instance;

        Assert.Throws<InvalidOperationException>(() =>
            accessor.SetAccessibleScope([Guid.NewGuid()], Guid.NewGuid()));
    }
}
```

- [ ] **Step 2: Rodar os testes para confirmar que falham**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~CurrentTenantAccessorTests"`
Expected: FAIL — `ICurrentTenantAccessor`/`CurrentTenantAccessor`/`NullCurrentTenantAccessor` ainda não existem.

- [ ] **Step 3: Criar `ILicenseScoped`**

```csharp
// CondotifyAPI.Domain/Interfaces/ILicenseScoped.cs
namespace CondotifyAPI.Domain.Interfaces;

// Marca uma entidade como pertencente a uma unica licenca (condominio).
// DatabaseContext.OnModelCreating aplica HasQueryFilter a toda entidade
// que implementa esta interface, via reflexao -- ver Task 2. Nao adicione
// esta interface a uma entidade sem entender essa consequencia: a partir
// do momento que implementa, toda consulta e restrita ao conjunto de
// licencas acessiveis da requisicao atual.
public interface ILicenseScoped
{
    Guid LicenseId { get; }
}
```

- [ ] **Step 4: Criar `ICurrentTenantAccessor` + implementações**

```csharp
// CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs
namespace CondotifyAPI.Domain.Interfaces;

public interface ICurrentTenantAccessor
{
    HashSet<Guid>? AccessibleLicenseIds { get; }
    Guid? AccessibleEnterpriseId { get; }
    void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId);
}
```

```csharp
// CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs
using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.Services;

// Uma instancia por requisicao (Scoped, registrada em Program.cs).
// Populada uma vez, no inicio do pipeline, por TenantScopeActionFilter
// (Task 5) -- nunca antes disso.
public sealed class CurrentTenantAccessor : ICurrentTenantAccessor
{
    public HashSet<Guid>? AccessibleLicenseIds { get; private set; }
    public Guid? AccessibleEnterpriseId { get; private set; }

    public void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId)
    {
        AccessibleLicenseIds = licenseIds;
        AccessibleEnterpriseId = enterpriseId;
    }
}

// Usado como valor padrao nos construtores de DatabaseContext que nao
// passam por injecao de dependencia (migrations, DatabaseContextFactory,
// DatabaseModelTests). AccessibleLicenseIds sempre null -> o filtro
// esconde tudo (fail-closed). Nenhum desses caminhos executa uma consulta
// de verdade contra uma entidade filtrada, entao isso nunca e observado
// na pratica -- e so a rede de seguranca para o caso de alguem um dia
// executar uma consulta por esse caminho.
public sealed class NullCurrentTenantAccessor : ICurrentTenantAccessor
{
    public static readonly NullCurrentTenantAccessor Instance = new();
    public HashSet<Guid>? AccessibleLicenseIds => null;
    public Guid? AccessibleEnterpriseId => null;

    public void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId) =>
        throw new InvalidOperationException(
            "NullCurrentTenantAccessor e somente leitura (usado fora do pipeline de requisicao HTTP).");
}
```

- [ ] **Step 5: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~CurrentTenantAccessorTests"`
Expected: PASS (4/4).

- [ ] **Step 6: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task1-check && rm -rf /tmp/tenantfilter-task1-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, todos os testes passam (nenhuma regressão).

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs CondotifyAPI.Domain/Interfaces/ILicenseScoped.cs CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs CondotifyAPI.Tests/CurrentTenantAccessorTests.cs
git commit -m "feat(api): add ICurrentTenantAccessor and ILicenseScoped foundational types"
```

---

## Task 2: Marcar as 29 entidades + aplicar o filtro em `DatabaseContext`

**Files:**
- Modify (adicionar `: ILicenseScoped` à declaração da classe): `CondotifyAPI.Domain/DTO/AccessControl/AccessRouteDTO.cs`, `CondotifyAPI.Domain/DTO/Amenities/AmenityDTO.cs`, `CondotifyAPI.Domain/DTO/Backup/ConfigurationBackupDTO.cs`, `CondotifyAPI.Domain/DTO/Block/BlockDTO.cs`, `CondotifyAPI.Domain/DTO/Delivers/DeliveryDTO.cs`, `CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs`, `CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs`, `CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs`, `CondotifyAPI.Domain/DTO/Finance/BoletoDtos.cs`, `CondotifyAPI.Domain/DTO/Invitation/AccessVisitDTO.cs`, `CondotifyAPI.Domain/DTO/Invitation/AccessWatchlistEntryDTO.cs`, `CondotifyAPI.Domain/DTO/Invitation/RegistrationInviteDTO.cs`, `CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs`, `CondotifyAPI.Domain/DTO/License/LicenseUserAccessDTO.cs`, `CondotifyAPI.Domain/DTO/Observability/OperationalAlertDTO.cs`, `CondotifyAPI.Domain/DTO/Operations/SafetyOperationsDTO.cs`, `CondotifyAPI.Domain/DTO/RecycleBin/RecycleBinItemDTO.cs`, `CondotifyAPI.Domain/DTO/Ticket/TicketDTO.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`
- Modify: `CondotifyAPI/Program.cs`
- Create: `CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs`

**Interfaces:**
- Consumes: `ILicenseScoped`, `ICurrentTenantAccessor`, `NullCurrentTenantAccessor` (Task 1).
- Produces: `DatabaseContext(DbContextOptions<DatabaseContext>, ICurrentTenantAccessor)` — a terceira sobrecarga de construtor, usada pela injeção de dependência real. Consumido pela Task 5 (registro em `Program.cs`) e pelas Tasks 4/6 (testes com banco real).

- [ ] **Step 1: Escrever o teste de modelo (falha primeiro)**

Este teste segue o padrão já existente em `CondotifyAPI.Tests/DatabaseModelTests.cs` (mesmo `CreateContext()`, mesma ideia de checar `.Model` sem precisar de conexão real):

```csharp
// CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Interfaces;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.DTO.Ticket;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class LicenseScopedFilterModelTests
{
    private static DatabaseContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;

        return new DatabaseContext(options);
    }

    [Theory]
    [InlineData(typeof(AccessRouteDTO))]
    [InlineData(typeof(AmenityDTO))]
    [InlineData(typeof(AmenityBookingDTO))]
    [InlineData(typeof(DeliveryDTO))]
    [InlineData(typeof(TicketDTO))]
    [InlineData(typeof(LicenseCredentialPolicyDTO))]
    [InlineData(typeof(LicenseUserAccessDTO))]
    [InlineData(typeof(IncidentDTO))]
    [InlineData(typeof(AutomationRuleDTO))]
    [InlineData(typeof(EmergencySessionDTO))]
    [InlineData(typeof(DigitalPassDTO))]
    public void LicenseScopedEntities_HaveAQueryFilterRegistered(Type entityClrType)
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(entityClrType);

        Assert.NotNull(entityType);
        Assert.True(typeof(ILicenseScoped).IsAssignableFrom(entityClrType), $"{entityClrType.Name} deveria implementar ILicenseScoped.");
        Assert.NotNull(entityType!.GetQueryFilter());
    }

    [Fact]
    public void AllILicenseScopedEntities_HaveAQueryFilterRegistered()
    {
        using var context = CreateContext();

        var licenseScopedTypes = context.Model.GetEntityTypes()
            .Where(x => typeof(ILicenseScoped).IsAssignableFrom(x.ClrType))
            .ToList();

        Assert.Equal(29, licenseScopedTypes.Count);
        Assert.All(licenseScopedTypes, entityType => Assert.NotNull(entityType.GetQueryFilter()));
    }
}
```

(A lista `[InlineData]` cobre uma amostra representativa espalhada pelos 18 arquivos, não as 29 uma a uma — o teste `AllILicenseScopedEntities_HaveAQueryFilterRegistered` já garante as 29 de uma vez, checando a contagem exata.)

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseScopedFilterModelTests"`
Expected: FAIL — nenhuma classe implementa `ILicenseScoped` ainda, `Assert.Equal(29, ...)` falha com `0`.

- [ ] **Step 3: Marcar as 29 classes com `: ILicenseScoped`**

Para cada arquivo, adicionar `: CondotifyAPI.Domain.Interfaces.ILicenseScoped` (fully-qualified, para não depender de `using` novo em cada arquivo) à declaração da classe. Tabela exata (old → new), uma linha por classe:

`CondotifyAPI.Domain/DTO/AccessControl/AccessRouteDTO.cs`:
- `public class AccessRouteDTO` → `public class AccessRouteDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public class AccessOperationAuditDTO` → `public class AccessOperationAuditDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public class AccessBatchOperationDTO` → `public class AccessBatchOperationDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public class AccessInventoryItemDTO` → `public class AccessInventoryItemDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public class AccessEventRecordDTO` → `public class AccessEventRecordDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Amenities/AmenityDTO.cs`:
- `public class AmenityDTO` → `public class AmenityDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public class AmenityBookingDTO` → `public class AmenityBookingDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Backup/ConfigurationBackupDTO.cs`:
- `public sealed class ConfigurationBackupDTO` → `public sealed class ConfigurationBackupDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class BackupAutomationPolicyDTO` → `public sealed class BackupAutomationPolicyDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Block/BlockDTO.cs` (atenção: esta classe está indentada em 4 espaços dentro de um bloco `namespace X { }`, não namespace de arquivo):
- `    public class BlockDTO` → `    public class BlockDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Delivers/DeliveryDTO.cs` (mesma indentação de 4 espaços):
- `    public class DeliveryDTO` → `    public class DeliveryDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs`:
- `public sealed class ResourceDocumentDTO` → `public sealed class ResourceDocumentDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs` (indentação de 4 espaços):
- `    public class AccessControlDeviceDTO` → `    public class AccessControlDeviceDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs` (indentação de 4 espaços):
- `    public class CFTVDeviceDTO` → `    public class CFTVDeviceDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Finance/BoletoDtos.cs` (só `BoletoBatchDTO` — `BoletoDocumentDTO` não tem `LicenseId` direto, fica de fora):
- `public sealed class BoletoBatchDTO` → `public sealed class BoletoBatchDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Invitation/AccessVisitDTO.cs`:
- `public class AccessVisitDTO` → `public class AccessVisitDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Invitation/AccessWatchlistEntryDTO.cs`:
- `public sealed class AccessWatchlistEntryDTO` → `public sealed class AccessWatchlistEntryDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Invitation/RegistrationInviteDTO.cs`:
- `public class RegistrationInviteDTO` → `public class RegistrationInviteDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs`:
- `public class LicenseCredentialPolicyDTO` → `public class LicenseCredentialPolicyDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/License/LicenseUserAccessDTO.cs`:
- `public class LicenseUserAccessDTO` → `public class LicenseUserAccessDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Observability/OperationalAlertDTO.cs` (só `AlertNotificationPolicyDTO` e `AlertNotificationDeliveryDTO` — `OperationalAlertDTO` em si NÃO entra aqui, ganha filtro próprio na Task 3):
- `public sealed class AlertNotificationPolicyDTO` → `public sealed class AlertNotificationPolicyDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class AlertNotificationDeliveryDTO` → `public sealed class AlertNotificationDeliveryDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Operations/SafetyOperationsDTO.cs` (5 classes; `IncidentTimelineEntryDTO` fica de fora, não tem `LicenseId` direto):
- `public sealed class IncidentDTO` → `public sealed class IncidentDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class AutomationRuleDTO` → `public sealed class AutomationRuleDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class AutomationExecutionDTO` → `public sealed class AutomationExecutionDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class EmergencySessionDTO` → `public sealed class EmergencySessionDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`
- `public sealed class DigitalPassDTO` → `public sealed class DigitalPassDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/RecycleBin/RecycleBinItemDTO.cs`:
- `public sealed class RecycleBinItemDTO` → `public sealed class RecycleBinItemDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

`CondotifyAPI.Domain/DTO/Ticket/TicketDTO.cs` (indentação de 4 espaços):
- `    public class TicketDTO` → `    public class TicketDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped`

Antes de cada edição, ler o arquivo e confirmar que a string antiga bate exatamente (indentação incluída) — vários destes arquivos têm mais de uma classe com nomes parecidos, usar contexto suficiente para a substituição ser inequívoca.

- [ ] **Step 4: Adicionar o construtor com `ICurrentTenantAccessor` e o loop de filtro em `DatabaseContext.cs`**

Arquivo atual (`CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`) tem exatamente:

```csharp
public partial class DatabaseContext : DbContext
{
    public DatabaseContext() { }

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) { }
```

Trocar por:

```csharp
public partial class DatabaseContext : DbContext
{
    private readonly CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor _tenant;

    public DatabaseContext() => _tenant = CondotifyAPI.Domain.Services.NullCurrentTenantAccessor.Instance;

    public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) =>
        _tenant = CondotifyAPI.Domain.Services.NullCurrentTenantAccessor.Instance;

    public DatabaseContext(DbContextOptions<DatabaseContext> options, CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor tenant) : base(options) =>
        _tenant = tenant;
```

`CondotifyAPI.Infrastructure` já referencia `CondotifyAPI.Domain` hoje (confirmado lendo `CondotifyAPI.Infrastructure.csproj`), então isso compila sem precisar de nenhuma referência de projeto nova.

No final de `OnModelCreating` (depois do loop existente que ajusta colunas `DateTime`, antes do fechamento do método):

```csharp
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(CondotifyAPI.Domain.Interfaces.ILicenseScoped).IsAssignableFrom(entityType.ClrType)) continue;
            var method = SetLicenseScopedFilterMethod.MakeGenericMethod(entityType.ClrType);
            method.Invoke(this, [modelBuilder]);
        }
    }

    private static readonly System.Reflection.MethodInfo SetLicenseScopedFilterMethod =
        typeof(DatabaseContext).GetMethod(nameof(SetLicenseScopedFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

    private void SetLicenseScopedFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, CondotifyAPI.Domain.Interfaces.ILicenseScoped
    {
        modelBuilder.Entity<TEntity>().HasQueryFilter(x =>
            _tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId));
    }
}
```

- [ ] **Step 5: Registrar `ICurrentTenantAccessor` em `Program.cs`**

Em `CondotifyAPI/Program.cs`, logo após a linha `builder.Services.AddScoped<ILicenseAuthorizationService, LicenseAuthorizationService>();` (linha 214):

```csharp
builder.Services.AddScoped<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor, CondotifyAPI.Domain.Services.CurrentTenantAccessor>();
```

(Sem isso, `AddDbContext<DatabaseContext>` não tem como resolver a terceira sobrecarga do construtor — o EF Core escolhe automaticamente o construtor com mais parâmetros resolvíveis via DI, então só precisa que `ICurrentTenantAccessor` esteja registrado no container; nenhuma configuração extra em `AddDbContext` é necessária.)

- [ ] **Step 6: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseScopedFilterModelTests"`
Expected: PASS — a amostra do `[Theory]` e o `Assert.Equal(29, ...)` do teste de contagem total.

- [ ] **Step 7: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task2-check && rm -rf /tmp/tenantfilter-task2-check`
Run: `dotnet build CondotifyAPI.Infrastructure/CondotifyAPI.Infrastructure.csproj -o /tmp/tenantfilter-task2b-check && rm -rf /tmp/tenantfilter-task2b-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: builds limpos, toda a suíte passa. **Preste atenção especial a qualquer teste existente que comece a falhar aqui** — um teste que fura o filtro (por construir `DatabaseContext` sem `ICurrentTenantAccessor` real E tentar executar uma consulta contra uma entidade agora filtrada) é esperado continuar passando (usa `NullCurrentTenantAccessor`, que só afeta consultas, e a suíte atual não executa consultas ao vivo contra essas entidades pelo padrão já estabelecido no repositório) — se algo quebrar, investigar antes de seguir, não ignorar.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI.Domain/DTO/AccessControl/AccessRouteDTO.cs CondotifyAPI.Domain/DTO/Amenities/AmenityDTO.cs CondotifyAPI.Domain/DTO/Backup/ConfigurationBackupDTO.cs CondotifyAPI.Domain/DTO/Block/BlockDTO.cs CondotifyAPI.Domain/DTO/Delivers/DeliveryDTO.cs CondotifyAPI.Domain/DTO/Documents/ResourceDocumentDtos.cs CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs CondotifyAPI.Domain/DTO/Finance/BoletoDtos.cs CondotifyAPI.Domain/DTO/Invitation/AccessVisitDTO.cs CondotifyAPI.Domain/DTO/Invitation/AccessWatchlistEntryDTO.cs CondotifyAPI.Domain/DTO/Invitation/RegistrationInviteDTO.cs CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs CondotifyAPI.Domain/DTO/License/LicenseUserAccessDTO.cs CondotifyAPI.Domain/DTO/Observability/OperationalAlertDTO.cs CondotifyAPI.Domain/DTO/Operations/SafetyOperationsDTO.cs CondotifyAPI.Domain/DTO/RecycleBin/RecycleBinItemDTO.cs CondotifyAPI.Domain/DTO/Ticket/TicketDTO.cs CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs CondotifyAPI/Program.cs CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs
git commit -m "feat(db): apply license-scoped query filter to the 29 ILicenseScoped entities"
```

---

## Task 2.5: Escopo "sem restrição" para os 13 workers de background

**Por que esta task existe:** a revisão da Task 2 encontrou um problema real que nenhuma task original previa. Os 13 workers registrados como `AddHostedService` em `CondotifyAPI/Program.cs:225-237` criam seu próprio escopo de DI fora do pipeline HTTP (`scopeFactory.CreateScope()`), então `TenantScopeActionFilter` (Task 5) nunca roda para eles — o `ICurrentTenantAccessor` de cada worker fica com `AccessibleLicenseIds = null` para sempre, e o filtro (fail-closed) esconde tudo. Investigação adicional (dois agentes de pesquisa, ver ledger) encontrou **mais de 30 pontos de consulta** afetados nos 13 arquivos de worker, e — mais importante — **três serviços compartilhados** (`AccessRouteResolver`, `RecycleBinService`, `PlatformPushNotifier`) que são chamados tanto por controllers HTTP (onde o filtro TEM que continuar valendo) quanto pelos workers (onde precisa ser ignorado). Adicionar `.IgnoreQueryFilters()` direto nesses três serviços compartilhados seria uma **regressão de segurança** no caminho HTTP — removeria o isolamento entre tenants exatamente onde ele mais importa.

**Desenho da correção:** em vez de `.IgnoreQueryFilters()` espalhado por dezenas de pontos de consulta (incluindo três que não podem receber isso incondicionalmente), o worker marca o **accessor da sua própria instância de `DatabaseContext`** como "sem restrição" uma vez, no início do seu escopo. Como cada requisição HTTP e cada execução de worker tem sua própria instância de `ICurrentTenantAccessor` (ambos são *Scoped*), isso não vaza para o caminho HTTP — é uma propriedade da instância do accessor, não do método chamado. `AccessRouteResolver`/`RecycleBinService`/`PlatformPushNotifier` não precisam de nenhuma alteração: eles simplesmente herdam o comportamento de qualquer `DatabaseContext` que receberem.

**Files:**
- Modify: `CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs`
- Modify: `CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`
- Modify (adicionar `tenant.MarkUnrestricted();` logo após resolver `DatabaseContext` do escopo): `CondotifyAPI/Services/AccessControl/ExpiredCredentialCleanupService.cs`, `CondotifyAPI/Services/AccessControl/CredentialReconciliationWorker.cs`, `CondotifyAPI/Services/AccessControl/AccessEventIngestionWorker.cs`, `CondotifyAPI/Services/AccessControl/DeviceHealthMonitoringWorker.cs`, `CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs`, `CondotifyAPI/Services/RecycleBin/RecycleBinCleanupService.cs`, `CondotifyAPI/Services/Backups/AutomaticBackupWorker.cs`, `CondotifyAPI/Services/Observability/OperationalAlertWorker.cs`, `CondotifyAPI/Services/Observability/AlertNotificationWorker.cs`, `CondotifyAPI/Services/Mobile/PushNotificationWorker.cs`, `CondotifyAPI/Services/Operations/AutomationRuleEvaluationService.cs` (a classe `AutomationRuleWorker`, não `AutomationRuleEvaluationService`, vive neste arquivo), `CondotifyAPI/Services/Lpr/LprPollingService.cs`
- Create: `CondotifyAPI.Tests/CurrentTenantAccessorUnrestrictedTests.cs`

**NÃO modificar:** `CondotifyAPI/Services/CFTV/CftvPathReaperWorker.cs` — confirmado por pesquisa que este worker nunca injeta `DatabaseContext`/`IServiceScopeFactory`, só fala com `IMediaGatewayClient` por HTTP. Nada a fazer aqui.

**NÃO modificar:** `AccessRouteResolver.cs`, `RecycleBinService.cs`, `PlatformPushNotifier.cs`, `CredentialReconciliationService.cs`, ou qualquer outro serviço "delegado" chamado pelos workers — o desenho desta task deliberadamente evita tocar neles. Se algum teste mostrar que um desses ainda retorna vazio quando chamado por um worker, o bug está em o worker não ter chamado `MarkUnrestricted()` no `DatabaseContext` certo antes de invocar o serviço, não no serviço em si.

**Interfaces:**
- Consumes: `ICurrentTenantAccessor`, `CurrentTenantAccessor`, `NullCurrentTenantAccessor` (Task 1), a `DatabaseContext` da Task 2.
- Produces: `ICurrentTenantAccessor.IsUnrestricted` (bool) + `void MarkUnrestricted()`. Consumido pela Task 3 (o filtro de `OperationalAlertDTO` precisa do mesmo tratamento) e implicitamente por toda consulta feita através de um `DatabaseContext` de worker.

- [ ] **Step 1: Escrever os testes (falham primeiro)**

```csharp
// CondotifyAPI.Tests/CurrentTenantAccessorUnrestrictedTests.cs
using CondotifyAPI.Domain.DTO.Delivers;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class CurrentTenantAccessorUnrestrictedTests
{
    [Fact]
    public void CurrentTenantAccessor_StartsNotUnrestricted()
    {
        var accessor = new CurrentTenantAccessor();

        Assert.False(accessor.IsUnrestricted);
    }

    [Fact]
    public void CurrentTenantAccessor_MarkUnrestricted_SetsFlag()
    {
        var accessor = new CurrentTenantAccessor();

        accessor.MarkUnrestricted();

        Assert.True(accessor.IsUnrestricted);
    }

    [Fact]
    public void NullCurrentTenantAccessor_IsNeverUnrestricted()
    {
        Assert.False(NullCurrentTenantAccessor.Instance.IsUnrestricted);
    }

    [Fact]
    public void NullCurrentTenantAccessor_MarkUnrestricted_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => NullCurrentTenantAccessor.Instance.MarkUnrestricted());
    }
}
```

E um teste de integração (banco real, mesmo padrão da Task 4/6) provando o comportamento fim-a-fim, adicionado a `CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs` (mesma fixture já criada na Task 6 — se a Task 6 ainda não rodou quando esta task for executada, criar a fixture aqui mesmo, com o mesmo formato já usado nas Tasks 4/6, e a Task 6 reaproveita):

```csharp
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
```

(Se esta task rodar antes da Task 6 existir, crie `CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs` com a fixture completa como descrita na Task 6 deste mesmo plano — leia a Task 6 abaixo para o `InitializeAsync`/`DisposeAsync` exatos antes de duplicar esforço.)

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~CurrentTenantAccessorUnrestrictedTests"`
Expected: FAIL — `IsUnrestricted`/`MarkUnrestricted` ainda não existem.

- [ ] **Step 3: Estender `ICurrentTenantAccessor` e as duas implementações**

Em `CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs`, adicionar à interface:

```csharp
    bool IsUnrestricted { get; }
    void MarkUnrestricted();
```

Em `CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs`, em `CurrentTenantAccessor`:

```csharp
    public bool IsUnrestricted { get; private set; }
    public void MarkUnrestricted() => IsUnrestricted = true;
```

E em `NullCurrentTenantAccessor`:

```csharp
    public bool IsUnrestricted => false;
    public void MarkUnrestricted() =>
        throw new InvalidOperationException("NullCurrentTenantAccessor e somente leitura (usado fora do pipeline de requisicao HTTP).");
```

- [ ] **Step 4: Atualizar o filtro em `DatabaseContext.cs` para checar `IsUnrestricted`**

Em `SetLicenseScopedFilter<TEntity>` (adicionada na Task 2), trocar:

```csharp
        modelBuilder.Entity<TEntity>().HasQueryFilter(x =>
            _tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId));
```

por:

```csharp
        modelBuilder.Entity<TEntity>().HasQueryFilter(x =>
            _tenant.IsUnrestricted || (_tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId)));
```

- [ ] **Step 5: Marcar cada worker como "sem restrição" no início do seu escopo**

Para cada um dos 12 arquivos de worker listados em **Files** (todos os 13 registrados em `Program.cs`, exceto `CftvPathReaperWorker`), localizar onde o escopo de DI é criado — o padrão já confirmado (idêntico em `RecycleBinCleanupService.cs:22-23`) é:

```csharp
using var scope = scopeFactory.CreateScope();
var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
```

Logo depois dessa linha (antes de qualquer consulta), adicionar:

```csharp
scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
```

Ler cada arquivo antes de editar — a maioria segue esse padrão exato, mas confirme a variável real usada para o escopo (`scope` vs outro nome) e o ponto exato antes de inserir. Para os dois workers cuja lógica real vive numa classe "delegada" no mesmo arquivo (`AutomationRuleEvaluationService.cs`: a classe `AutomationRuleWorker` cria o escopo; `OperationalAlertWorker.cs`/`LprPollingService.cs`: idem), o `MarkUnrestricted()` vai no ponto onde o `BackgroundService` (não o delegado) cria o escopo — a instância de `DatabaseContext`/`ICurrentTenantAccessor` resolvida ali é a mesma passada adiante para `OperationalAlertEvaluationService`/`AutomationRuleEvaluationService`/`LprDeviceProcessor`, então marcar uma vez no ponto de criação do escopo cobre tudo que é chamado a partir dali.

- [ ] **Step 6: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~CurrentTenantAccessorUnrestrictedTests|FullyQualifiedName~UnrestrictedAccessor_SeesAllLicensesRegardlessOfAccessibleSet"`
Expected: PASS.

- [ ] **Step 7: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task2b-check && rm -rf /tmp/tenantfilter-task2b-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI.Domain/Interfaces/ICurrentTenantAccessor.cs CondotifyAPI.Domain/Services/CurrentTenantAccessor.cs CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs CondotifyAPI/Services/AccessControl/ExpiredCredentialCleanupService.cs CondotifyAPI/Services/AccessControl/CredentialReconciliationWorker.cs CondotifyAPI/Services/AccessControl/AccessEventIngestionWorker.cs CondotifyAPI/Services/AccessControl/DeviceHealthMonitoringWorker.cs CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs CondotifyAPI/Services/RecycleBin/RecycleBinCleanupService.cs CondotifyAPI/Services/Backups/AutomaticBackupWorker.cs CondotifyAPI/Services/Observability/OperationalAlertWorker.cs CondotifyAPI/Services/Observability/AlertNotificationWorker.cs CondotifyAPI/Services/Mobile/PushNotificationWorker.cs CondotifyAPI/Services/Operations/AutomationRuleEvaluationService.cs CondotifyAPI/Services/Lpr/LprPollingService.cs CondotifyAPI.Tests/CurrentTenantAccessorUnrestrictedTests.cs CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs
git commit -m "fix(api): let background workers mark their own scope unrestricted instead of filtering out their data"
```

---

## Task 2.6: Marcar o escopo de inicialização do `Program.cs` como sem restrição

**Por que esta task existe:** a revisão da Task 2.5 encontrou mais um lugar com o mesmo problema, fora do escopo dos 13 workers: o bloco de inicialização em `CondotifyAPI/Program.cs` (linhas 276-296) cria seu próprio escopo (`app.Services.CreateScope()`) para rodar `db.Database.Migrate()`, uma passagem de recriptografia de senhas legadas de equipamentos (consulta `db.Devices`/`db.CFTVDevices`, ambas `ILicenseScoped`) e `DevelopmentDataSeeder.SeedAsync` em desenvolvimento. Sem marcar esse escopo, a recriptografia de senha legada para de encontrar dispositivos para sempre (nunca mais migra senhas antigas) e o seeder de desenvolvimento pode não conseguir checar dados já existentes corretamente. Varredura confirmada: este é o único lugar além dos 13 workers que cria um escopo de DI fora do pipeline HTTP em todo o projeto `CondotifyAPI` (`grep -rn "CreateScope()" CondotifyAPI --include=*.cs`, excluindo os arquivos de worker já tratados na Task 2.5, retorna exatamente esta ocorrência).

**Files:**
- Modify: `CondotifyAPI/Program.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor.MarkUnrestricted()` (Task 2.5).

- [ ] **Step 1: Editar o bloco de inicialização**

Em `CondotifyAPI/Program.cs`, o bloco atual (linhas 276-296) começa assim:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    db.Database.Migrate();
```

Adicionar a marcação logo após resolver `db`, antes de `Migrate()` (a migration em si não é afetada pelo filtro — é DDL bruto — mas as consultas logo depois são):

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
    scope.ServiceProvider.GetRequiredService<CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor>().MarkUnrestricted();
    db.Database.Migrate();
```

O resto do bloco (recriptografia de senha legada, `DevelopmentDataSeeder.SeedAsync`) fica exatamente como está — não precisa de nenhuma outra mudança.

- [ ] **Step 2: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task2.6-check && rm -rf /tmp/tenantfilter-task2.6-check`
Expected: build limpo. Este bloco só roda no boot da aplicação (não há como testar automaticamente sem subir o processo inteiro) — a verificação aqui é o build limpo mais a leitura cuidadosa confirmando que a chamada está no lugar certo, antes de qualquer consulta.

- [ ] **Step 3: Verificação manual (opcional, se o ambiente permitir)**

Se for possível rodar `dotnet run --project CondotifyAPI` localmente com o Postgres já populado por uma sessão anterior, confirmar nos logs que a passagem de recriptografia de senha não lança exceção e que o app sobe normalmente. Se não for possível rodar, pular esta etapa e confiar no build limpo.

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI/Program.cs
git commit -m "fix(api): mark the app-startup DI scope unrestricted so device password re-encryption and dev seeding still see data"
```

---

## Task 3: Filtro especial de `OperationalAlertDTO`

**Files:**
- Modify: `CondotifyAPI.Infrastructure/ContextConfiguration/Observability/OperationalAlertConfiguration.cs`
- Modify: `CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor` (Task 1), passado à classe de configuração via construtor. Também consome `IsUnrestricted` (Task 2.5) — o filtro abaixo já inclui o bypass de worker desde o início, para não precisar de um terceiro patch quando os workers que leem `OperationalAlertDTO` (`AlertNotificationWorker`, `OperationalAlertWorker`/`OperationalAlertEvaluationService`, `LprPollingService`/`LprDeviceProcessor`) forem marcados como sem restrição na Task 2.5.

- [ ] **Step 1: Escrever o teste (falha primeiro)**

Adicionar a `CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs`:

```csharp
[Fact]
public void OperationalAlertDTO_HasAQueryFilterRegistered()
{
    using var context = CreateContext();
    var entityType = context.Model.FindEntityType(typeof(OperationalAlertDTO));

    Assert.NotNull(entityType);
    Assert.False(typeof(ILicenseScoped).IsAssignableFrom(typeof(OperationalAlertDTO)), "OperationalAlertDTO nao deve implementar ILicenseScoped (LicenseId e anulavel).");
    Assert.NotNull(entityType!.GetQueryFilter());
}
```

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~OperationalAlertDTO_HasAQueryFilterRegistered"`
Expected: FAIL — nenhum filtro registrado ainda.

- [ ] **Step 3: Implementar o filtro**

`OperationalAlertConfiguration.cs` hoje é `IEntityTypeConfiguration<OperationalAlertDTO>` sem construtor (classe sem estado). Precisa passar a receber `ICurrentTenantAccessor`:

```csharp
public sealed class OperationalAlertConfiguration(CondotifyAPI.Domain.Interfaces.ICurrentTenantAccessor tenant) : IEntityTypeConfiguration<OperationalAlertDTO>
{
    public void Configure(EntityTypeBuilder<OperationalAlertDTO> builder)
    {
        builder.ToTable("OperationalAlerts");
        // ... (todo o conteudo existente do metodo Configure permanece igual, sem remover nada) ...

        builder.HasQueryFilter(x =>
            tenant.IsUnrestricted ||
            (x.EnterpriseId == tenant.AccessibleEnterpriseId &&
             (x.LicenseId == null || (tenant.AccessibleLicenseIds != null && tenant.AccessibleLicenseIds.Contains(x.LicenseId.Value)))));
    }
}
```

`IEntityTypeConfiguration<T>` é instanciada onde? Verificar como `ObservabilityEntityConfiguration(modelBuilder)` (chamado em `DatabaseContext.OnModelCreating`, linha 46) constrói `OperationalAlertConfiguration` hoje — se for via `new OperationalAlertConfiguration()` direto (sem DI), o construtor precisa receber `_tenant` do próprio `DatabaseContext` (`new OperationalAlertConfiguration(_tenant)`), não de um container de DI separado — `IEntityTypeConfiguration` não é resolvido via injeção de dependência neste projeto. Ler `ObservabilityEntityConfiguration` (provavelmente em `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.Observability.cs` ou arquivo parecido) antes de editar, para confirmar o padrão exato de instanciação usado pelas outras `*Configuration` classes deste mesmo arquivo (`AlertNotificationPolicyConfiguration`, `AlertNotificationDeliveryConfiguration` continuam sem precisar de `tenant` — já ganharam `ILicenseScoped` na Task 2, não precisam de configuração de filtro manual).

- [ ] **Step 4: Rodar o teste de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~OperationalAlertDTO_HasAQueryFilterRegistered"`
Expected: PASS.

- [ ] **Step 5: Build + suíte completa**

Run: `dotnet build CondotifyAPI.Infrastructure/CondotifyAPI.Infrastructure.csproj -o /tmp/tenantfilter-task3-check && rm -rf /tmp/tenantfilter-task3-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 6: Commit**

```bash
git add CondotifyAPI.Infrastructure/ContextConfiguration/Observability/OperationalAlertConfiguration.cs CondotifyAPI.Tests/LicenseScopedFilterModelTests.cs
git commit -m "feat(db): scope OperationalAlertDTO by enterprise, allowing license-less alerts through"
```

---

## Task 4: Quebrar a circularidade em `LicenseAuthorizationService`

**Files:**
- Modify: `CondotifyAPI/Services/Authorization/LicenseAuthorizationService.cs`
- Create: `CondotifyAPI.Tests/LicenseAuthorizationServiceTenantFilterTests.cs`

**Interfaces:**
- Consumes: `DatabaseContext(options, ICurrentTenantAccessor)` (Task 2), `CurrentTenantAccessor` (Task 1).

- [ ] **Step 1: Escrever o teste de circularidade (falha primeiro)**

Este é um teste de integração contra o Postgres local (ver Global Constraints) — cria uma Enterprise, uma License e um User (Admin) reais, aponta um `CurrentTenantAccessor` vazio (simulando o estado antes de `TenantScopeActionFilter` rodar, que é exatamente o cenário de circularidade: o próprio `GetLicensePermissionsAsync` está sendo chamado para *calcular* o conjunto, então nenhum conjunto existe ainda), e confirma que o método ainda retorna a licença esperada:

```csharp
// CondotifyAPI.Tests/LicenseAuthorizationServiceTenantFilterTests.cs
using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Users;
using CondotifyAPI.Domain.Enums.Users;
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
```

Antes de escrever isto, ler `CondotifyAPI.Domain/DTO/Enterprise/EnterpriseDTO.cs` e `CondotifyAPI.Domain/DTO/Users/UserAccessDTO.cs` para confirmar os nomes exatos de propriedades obrigatórias (`Name`, `CNPJ`, `Email` para `EnterpriseDTO`; `Name`, `Email` para `UserAccessDTO`) — ajustar o teste se algum campo obrigatório tiver nome diferente do assumido aqui.

- [ ] **Step 2: Confirmar que o Postgres local está acessível e rodar os testes (esperado falhar)**

Run: `docker ps --filter "name=condotify-postgres" --format "{{.Status}}"` — confirmar `healthy` antes de continuar; se não estiver rodando, `docker-compose up -d postgres` primeiro.
Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAuthorizationServiceTenantFilterTests"`
Expected: FAIL — o filtro global (Task 2) já esconde `Licenses`/`Users` porque `_tenant.AccessibleLicenseIds` está vazio (`[]`), e nada em `LicenseAuthorizationService` ainda usa `.IgnoreQueryFilters()`.

- [ ] **Step 3: Adicionar as duas exceções deliberadas**

Em `CondotifyAPI/Services/Authorization/LicenseAuthorizationService.cs`:

`GetGrantAsync` (linha 41 e 43 hoje):
```csharp
        var user = await _context.Users.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == userId && x.EnterpriseId == enterpriseId, cancellationToken);
        if (user is null) return null;
        if (!await _context.Licenses.AsNoTracking().IgnoreQueryFilters().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId, cancellationToken)) return null;
```

`GetLicensePermissionsAsync` (linhas 68-83 hoje):
```csharp
        var user = await _context.Users.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == userId && x.EnterpriseId == enterpriseId, cancellationToken);
        if (user is null)
            return new Dictionary<Guid, LicensePermissionEnum>();

        if (user.AccessType is AccessTypeEnum.Developer or AccessTypeEnum.Admin)
        {
            return await _context.Licenses.AsNoTracking().IgnoreQueryFilters()
                .Where(x => x.EnterpriseId == enterpriseId)
                .ToDictionaryAsync(x => x.Id, _ => LicensePermissionEnum.All, cancellationToken);
        }

        var grants = await _context.LicenseUserAccesses.AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.UserId == userId && x.IsActive && x.License.EnterpriseId == enterpriseId)
            .Select(x => new { x.LicenseId, x.Permissions })
            .ToListAsync(cancellationToken);
```

Comentário acima de cada bloco, explicando por quê (não é opcional — é o único jeito de um próximo desenvolvedor não "corrigir" isso removendo o `.IgnoreQueryFilters()` sem entender a circularidade):

```csharp
        // IgnoreQueryFilters() deliberado: este metodo CALCULA o conjunto de
        // licencas acessiveis que o filtro global usa. Sem isso, a consulta
        // dependeria circularmente do resultado que ainda nao existe.
        // Unicas duas excecoes esperadas neste projeto -- ver
        // docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md.
```

Note que `Users` e `LicenseUserAccesses` não estão na lista de 29 entidades `ILicenseScoped` (não têm `LicenseId` como FK própria da forma que o filtro cobre — `Users` é escopado por `EnterpriseId`, não por `LicenseId`; `LicenseUserAccessDTO` SIM está na lista das 29). Adicionar `.IgnoreQueryFilters()` em `_context.Users` aqui é inofensivo (não tem filtro mesmo) mas mantém o padrão visualmente consistente com as linhas que de fato precisam — deixe como está acima por clareza, não remova.

- [ ] **Step 4: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAuthorizationServiceTenantFilterTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task4-check && rm -rf /tmp/tenantfilter-task4-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 6: Commit**

```bash
git add CondotifyAPI/Services/Authorization/LicenseAuthorizationService.cs CondotifyAPI.Tests/LicenseAuthorizationServiceTenantFilterTests.cs
git commit -m "fix(api): break circular dependency between the tenant filter and its own accessible-set calculation"
```

---

## Task 5: `TenantScopeActionFilter`

**Files:**
- Create: `CondotifyAPI/Services/Authorization/TenantScopeActionFilter.cs`
- Modify: `CondotifyAPI/Program.cs`
- Create: `CondotifyAPI.Tests/TenantScopeActionFilterTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor` (Task 1), `ILicenseAuthorizationService.GetAccessibleLicenseIdsAsync` (já existe), `IResidentAuthorizationService.GetGrantAsync` (já existe).
- Produces: `TenantScopeActionFilter`, registrado globalmente. Consumido pelas Tasks 4 (indiretamente, via `LicenseAuthorizationService`) e 6 (fluxo ponta-a-ponta).

- [ ] **Step 1: Escrever os testes (falham primeiro)**

Este filtro só depende de interfaces (`ILicenseAuthorizationService`, `IResidentAuthorizationService`) — testável com implementações falsas, sem banco:

```csharp
// CondotifyAPI.Tests/TenantScopeActionFilterTests.cs
using System.Security.Claims;
using CondotifyAPI.Domain.Interfaces;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace CondotifyAPI.Tests;

public sealed class TenantScopeActionFilterTests
{
    private sealed class FakeLicenseAuthorizationService(HashSet<Guid> ids) : ILicenseAuthorizationService
    {
        public Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(ids);
        public Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(ClaimsPrincipal principal, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeResidentAuthorizationService(ResidentAccessGrant? grant) : IResidentAuthorizationService
    {
        public Task<ResidentAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => Task.FromResult(grant);
    }

    private static ActionExecutingContext BuildContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionContext = new Microsoft.AspNetCore.Mvc.ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);
    }

    private static ClaimsPrincipal StaffPrincipal(Guid enterpriseId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("enterprise_id", enterpriseId.ToString()),
        new Claim("principal_type", "user")
    ], "TestAuth"));

    private static ClaimsPrincipal ResidentPrincipal() => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim("principal_type", "resident")
    ], "TestAuth"));

    [Fact]
    public async Task Staff_PopulatesAccessorFromLicenseAuthorizationService()
    {
        var enterpriseId = Guid.NewGuid();
        var licenseIds = new HashSet<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService(licenseIds), new FakeResidentAuthorizationService(null));
        var context = BuildContext(StaffPrincipal(enterpriseId));
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () => { nextCalled = true; return Task.FromResult<ActionExecutedContext>(null!); });

        Assert.True(nextCalled);
        Assert.Equal(licenseIds, tenant.AccessibleLicenseIds);
        Assert.Equal(enterpriseId, tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_PopulatesAccessorWithOwnLicenseOnly()
    {
        var grant = new ResidentAccessGrant(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()], ResidentAccessTypeEnum.Responsible, true);
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(grant));
        var context = BuildContext(ResidentPrincipal());

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Single(tenant.AccessibleLicenseIds!);
        Assert.Contains(grant.LicenseId, tenant.AccessibleLicenseIds!);
        Assert.Null(tenant.AccessibleEnterpriseId);
    }

    [Fact]
    public async Task Resident_WithNoGrant_PopulatesEmptySet_NotNull()
    {
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([]), new FakeResidentAuthorizationService(null));
        var context = BuildContext(ResidentPrincipal());

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.NotNull(tenant.AccessibleLicenseIds);
        Assert.Empty(tenant.AccessibleLicenseIds!);
    }

    [Fact]
    public async Task UnauthenticatedRequest_LeavesAccessorUnpopulated()
    {
        var tenant = new CurrentTenantAccessor();
        var filter = new TenantScopeActionFilter(tenant, new FakeLicenseAuthorizationService([Guid.NewGuid()]), new FakeResidentAuthorizationService(null));
        var context = BuildContext(new ClaimsPrincipal(new ClaimsIdentity()));

        await filter.OnActionExecutionAsync(context, () => Task.FromResult<ActionExecutedContext>(null!));

        Assert.Null(tenant.AccessibleLicenseIds);
    }
}
```

Antes de escrever isto, ler `CondotifyAPI/Services/Authorization/ResidentAuthorizationService.cs` para confirmar a ordem exata dos parâmetros posicionais de `ResidentAccessGrant` (`ResidentId, LicenseId, UnitIds, AccessType, IsResponsible` — já verificado durante o design, mas confirme antes de usar) e `LicensePermissionEnum`/`ResidentAccessTypeEnum` — os `using` corretos para esses tipos.

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~TenantScopeActionFilterTests"`
Expected: FAIL — `TenantScopeActionFilter` ainda não existe.

- [ ] **Step 3: Implementar `TenantScopeActionFilter`**

```csharp
// CondotifyAPI/Services/Authorization/TenantScopeActionFilter.cs
using Microsoft.AspNetCore.Mvc.Filters;

namespace CondotifyAPI.Services.Authorization;

public sealed class TenantScopeActionFilter(
    ICurrentTenantAccessor tenant,
    ILicenseAuthorizationService licenseAuth,
    IResidentAuthorizationService residentAuth) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var principalType = user.FindFirstValue("principal_type");
            if (principalType == "resident")
            {
                var grant = await residentAuth.GetGrantAsync(user, context.HttpContext.RequestAborted);
                tenant.SetAccessibleScope(grant is null ? [] : [grant.LicenseId], null);
            }
            else
            {
                var ids = await licenseAuth.GetAccessibleLicenseIdsAsync(user, context.HttpContext.RequestAborted);
                var enterpriseId = Guid.TryParse(user.FindFirstValue("enterprise_id"), out var eid) ? eid : (Guid?)null;
                tenant.SetAccessibleScope(ids, enterpriseId);
            }
        }

        await next();
    }
}
```

(`using System.Security.Claims;` para `FindFirstValue` já deve vir de um `using` implícito ou precisa ser adicionado — conferir ao compilar.)

- [ ] **Step 4: Registrar o filtro globalmente em `Program.cs`**

Logo após a linha adicionada na Task 2 (`AddScoped<ICurrentTenantAccessor, ...>`), adicionar:

```csharp
builder.Services.AddScoped<CondotifyAPI.Services.Authorization.TenantScopeActionFilter>();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
    options.Filters.AddService<CondotifyAPI.Services.Authorization.TenantScopeActionFilter>());
```

(`AddService<T>` em vez de `Add<T>()` porque o filtro tem dependências que precisam vir do container de DI a cada requisição — `Add<T>()` sozinho também resolveria via DI automaticamente pelo tipo, mas `AddService<T>` é mais explícito sobre a intenção e é o padrão recomendado pela documentação do ASP.NET Core para filtros com dependências scoped. Se `AddService<T>` não compilar por algum motivo, usar `options.Filters.Add(typeof(CondotifyAPI.Services.Authorization.TenantScopeActionFilter));` como alternativa equivalente.)

- [ ] **Step 5: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~TenantScopeActionFilterTests"`
Expected: PASS (4/4).

- [ ] **Step 6: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/tenantfilter-task5-check && rm -rf /tmp/tenantfilter-task5-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Services/Authorization/TenantScopeActionFilter.cs CondotifyAPI/Program.cs CondotifyAPI.Tests/TenantScopeActionFilterTests.cs
git commit -m "feat(api): populate the tenant accessor via a global action filter, branching staff vs resident"
```

---

## Task 6: Testes ponta-a-ponta — isolamento e não-regressão

**Files:**
- Create: `CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs`

**Interfaces:**
- Consumes: tudo das Tasks 1-5.

- [ ] **Step 1: Escrever o teste de isolamento (falha primeiro se algo nas tasks anteriores estiver errado — deve passar direto se tudo estiver certo, já que é um teste de regressão/confirmação final, não uma nova funcionalidade)**

```csharp
// CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs
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
}
```

Antes de escrever isto, ler `CondotifyAPI.Domain/DTO/Delivers/DeliveryDTO.cs` para confirmar quais propriedades são obrigatórias além de `Name`/`LicenseId`/`CreatedAt`/`UpdatedAt` (a Task 1 do subsistema de feature flags já leu este arquivo — `Type`/`Status` podem ter default; ajustar o objeto de inserção se o `SaveChangesAsync` falhar por causa de um campo obrigatório sem valor).

- [ ] **Step 2: Rodar os testes**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~TenantIsolationIntegrationTests"`
Expected: PASS (4/4) — se qualquer um falhar, é sinal de que alguma task anterior tem um problema real, não um teste mal escrito; investigar a task correspondente antes de ajustar o teste.

- [ ] **Step 3: Rodar toda a suíte uma última vez**

Run: `dotnet test CondotifyAPI.Tests`
Expected: todos os testes passam, incluindo os ~585 pré-existentes.

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI.Tests/TenantIsolationIntegrationTests.cs
git commit -m "test(api): end-to-end proof the tenant filter isolates licenses without relying on explicit query filters"
```

---

## Final check (all tasks complete)

- [ ] `dotnet build Condotify.sln` limpo (usar `-o` para pasta temporária se algum `dotnet run` estiver ativo).
- [ ] `dotnet test CondotifyAPI.Tests` — todos passam, incluindo os novos testes de isolamento/circularidade/filtro.
- [ ] `MultiLicenseHashSetQuery_MatchesDashboardPattern_ReturnsBothWhenBothAreAccessible` (Task 6) cobre automaticamente o padrão `HashSet<Guid>.Contains` que `OperationsController.GetDashboard` usa — não depende de comparação manual. Se quiser confiança extra, rodar o portal localmente e comparar os números do Dashboard antes/depois deste plano para um usuário com acesso a múltiplas licenças.
- [ ] Revisar se alguma `.IgnoreQueryFilters()` além das duas da Task 4 apareceu em algum lugar do diff total — se sim, é um desvio que precisa de explicação clara no relatório da task correspondente.
