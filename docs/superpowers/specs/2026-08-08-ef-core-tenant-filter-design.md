# Filtro Global de Tenant no EF Core — Design

Data: 2026-08-08

## Contexto

Hoje o isolamento entre licenças (condomínios) depende inteiramente de disciplina manual: cada controller precisa lembrar de filtrar suas consultas por `LicenseId`. Não há nenhuma rede de segurança em nível de banco/ORM — um `Where(x => x.LicenseId == licenseId)` esquecido em uma consulta nova vazaria dados entre condomínios (ou entre empresas, o caso mais grave). Isso já foi identificado na auditoria de segurança original como risco estrutural, não como vulnerabilidade confirmada — os controllers amostrados hoje filtram corretamente.

A investigação para este design (agente de pesquisa, ver histórico da conversa) revelou que a recomendação original ("filtro de uma licença por requisição") **quebraria** partes centrais do sistema: o Dashboard multi-condomínio, a Busca Global, Alertas Operacionais e o próprio `LicenseAuthorizationService` dependem de consultar **várias licenças por requisição** — o modelo de acesso real é "conjunto de licenças que o usuário pode ver", não "uma licença por chamada". O design abaixo é a versão corrigida: filtra pelo **conjunto de licenças acessíveis**, não por uma única licença — o que não quebra nenhum desses fluxos porque o conjunto amplo sempre contém (ou iguala) qualquer subconjunto mais estreito que uma consulta já usa explicitamente.

Levantamento exato (não estimativa) das entidades afetadas: **28 classes** em `CondotifyAPI.Domain/DTO/**` têm uma propriedade `LicenseId` direta. Duas exigem tratamento especial:
- `OperationalAlertDTO.LicenseId` é `Guid?` — alertas podem existir sem licença (`DeleteBehavior.SetNull` na FK), e esses alertas "órfãos"/de sistema devem continuar visíveis a quem tem permissão de alerta, não desaparecer.
- `LicenseCredentialPolicyDTO.LicenseId` é a própria chave primária (política 1:1 por licença) — o filtro funciona do mesmo jeito, só é bom documentar que aqui "escondido pelo filtro" e "não existe" coincidem.

## Escopo

Dentro do escopo:
- Um accessor por requisição (`ICurrentTenantAccessor`) guardando o conjunto de licenças acessíveis, computado uma vez por requisição, **antes** de qualquer consulta ao `DatabaseContext`.
- Distinção staff vs. morador: para staff, o conjunto vem de `ILicenseAuthorizationService.GetAccessibleLicenseIdsAsync` (já existe, reaproveitado sem alteração). Para morador (`principal_type=resident`), o conjunto é a licença do próprio vínculo, via `IResidentAuthorizationService`.
- Filtro global (`HasQueryFilter`) aplicado às 27 entidades com `LicenseId` não anulável, via interface marcadora `ILicenseScoped` + um loop de reflexão em `OnModelCreating` (não 27 declarações manuais).
- Tratamento dedicado para `OperationalAlertDTO` (permite `LicenseId == null` passar).
- Duas exceções documentadas e deliberadas (`.IgnoreQueryFilters()`) nos dois pontos de `LicenseAuthorizationService` que calculam o próprio conjunto acessível — sem isso, o cálculo do conjunto dependeria circularmente do conjunto que ainda não existe.
- Testes cobrindo especificamente os dois pontos de risco: o caso morador (garantir que não fica com conjunto vazio) e o caso de circularidade (garantir que o cálculo do conjunto acessível continua funcionando com o filtro ativo).

Fora do escopo (v1):
- As entidades que só chegam à licença por navegação indireta (`Resident` via `Unit.Block.LicenseId`, e o que depende de `Resident` — mais 1-2 saltos ainda) — documentado como lacuna conhecida, não coberta agora. Cobrir exigiria filtros com navegação/JOIN embutido no lambda, uma categoria de risco diferente (mais fácil de escrever um filtro tecnicamente válido mas semanticamente errado).
- Qualquer mudança de comportamento para quem já tem acesso — o filtro é estritamente uma segunda camada; ninguém que já enxerga um registro hoje deixa de enxergar.
- Aplicar o filtro a endpoints que já usam `.IgnoreQueryFilters()` implicitamente por não passar por `DatabaseContext` (nenhum caso conhecido hoje).

## Arquitetura

### `ICurrentTenantAccessor` (novo, `CondotifyAPI/Services/Authorization/CurrentTenantAccessor.cs`)

```csharp
public interface ICurrentTenantAccessor
{
    HashSet<Guid>? AccessibleLicenseIds { get; }
    void SetAccessibleLicenseIds(HashSet<Guid> licenseIds);
}

public sealed class CurrentTenantAccessor : ICurrentTenantAccessor
{
    public HashSet<Guid>? AccessibleLicenseIds { get; private set; }
    public void SetAccessibleLicenseIds(HashSet<Guid> licenseIds) => AccessibleLicenseIds = licenseIds;
}
```

Registrado como `Scoped` (uma instância por requisição, igual ao `DatabaseContext`).

**Antes de qualquer requisição autenticada tocar o banco, o conjunto precisa estar populado.** `AccessibleLicenseIds` começa `null`; o filtro trata `null` como "conjunto vazio" (fail-closed: nada visível até o accessor ser populado), nunca como "sem filtro".

### Populando o accessor: `TenantScopeActionFilter` (novo, `CondotifyAPI/Services/Authorization/TenantScopeActionFilter.cs`)

Um `IAsyncActionFilter` global, registrado em `Program.cs` (`options.Filters.Add<TenantScopeActionFilter>()`), rodando depois de `[Authorize]` e antes da action:

```csharp
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
                tenant.SetAccessibleLicenseIds(grant is null ? [] : [grant.LicenseId]);
            }
            else
            {
                var ids = await licenseAuth.GetAccessibleLicenseIdsAsync(user, context.HttpContext.RequestAborted);
                tenant.SetAccessibleLicenseIds(ids);
            }
        }

        await next();
    }
}
```

Rodar isso em TODA requisição autenticada tem um custo: uma consulta extra (a mesma que `GetAccessibleLicenseIdsAsync` já faz) antes de cada action, mesmo em endpoints que nunca tocam uma entidade filtrada (ex.: `GET /api/auth/me`). Aceito deliberadamente — é o preço da rede de segurança ser automática em vez de depender de cada controller lembrar de ativá-la; qualquer coisa mais seletiva (rodar só nos controllers que precisam) reintroduz exatamente o tipo de "esquecimento manual" que este design existe para eliminar.

### `ILicenseScoped` + aplicação em massa (`CondotifyAPI.Domain/Interfaces/ILicenseScoped.cs`, novo)

```csharp
namespace CondotifyAPI.Domain.Interfaces;

public interface ILicenseScoped
{
    Guid LicenseId { get; }
}
```

As 27 classes (lista completa abaixo) ganham `: ILicenseScoped` na declaração — mudança mecânica, a propriedade `LicenseId` já existe com o nome/tipo certo em todas.

Em `DatabaseContext.cs`, dentro de `OnModelCreating`, depois que todas as `IEntityTypeConfiguration<>` já rodaram:

```csharp
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (!typeof(ILicenseScoped).IsAssignableFrom(entityType.ClrType)) continue;
    var method = SetLicenseScopedFilterMethod.MakeGenericMethod(entityType.ClrType);
    method.Invoke(this, [modelBuilder]);
}

// ...

private static readonly MethodInfo SetLicenseScopedFilterMethod =
    typeof(DatabaseContext).GetMethod(nameof(SetLicenseScopedFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

private void SetLicenseScopedFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, ILicenseScoped
{
    modelBuilder.Entity<TEntity>().HasQueryFilter(x =>
        _tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId));
}
```

**Atenção — `DatabaseContext` tem hoje três formas de ser construído sem contêiner de DI**, confirmado lendo o arquivo: um construtor sem parâmetros (`public DatabaseContext() { }`), um construtor só com `DbContextOptions` (usado por `DatabaseContextFactory`, a factory de design-time que as migrations usam, e por `CondotifyAPI.Tests/DatabaseModelTests.CreateContext()`). Exigir `ICurrentTenantAccessor` como parâmetro obrigatório quebraria as três. A saída é um objeto nulo (null-object pattern), não um parâmetro opcional que aceita `null` cru:

```csharp
// CondotifyAPI/Services/Authorization/CurrentTenantAccessor.cs
public sealed class NullCurrentTenantAccessor : ICurrentTenantAccessor
{
    public static readonly NullCurrentTenantAccessor Instance = new();
    public HashSet<Guid>? AccessibleLicenseIds => null;
    public void SetAccessibleLicenseIds(HashSet<Guid> licenseIds) =>
        throw new InvalidOperationException("NullCurrentTenantAccessor e somente leitura (usado fora do pipeline de requisicao).");
}
```

Em `DatabaseContext.cs`:

```csharp
private readonly ICurrentTenantAccessor _tenant;

public DatabaseContext() => _tenant = NullCurrentTenantAccessor.Instance;

public DatabaseContext(DbContextOptions<DatabaseContext> options) : base(options) =>
    _tenant = NullCurrentTenantAccessor.Instance;

public DatabaseContext(DbContextOptions<DatabaseContext> options, ICurrentTenantAccessor tenant) : base(options) =>
    _tenant = tenant;
```

A terceira sobrecarga é a que a injeção de dependência real usa em produção (registrada em `Program.cs`). As outras duas continuam existindo exatamente como estão hoje — nenhum call site existente (migrations, `DatabaseContextFactory`, `DatabaseModelTests`) precisa mudar. Com `NullCurrentTenantAccessor`, `AccessibleLicenseIds` é sempre `null`, e o filtro (`_tenant.AccessibleLicenseIds != null && ...`) já trata `null` como "esconder tudo" — falha fechado, nunca abre acidentalmente. Como migrations e `DatabaseModelTests` só leem `.Model` (metadados), nunca executam uma consulta de verdade, o filtro nunca chega a ser avaliado nesses caminhos — só a expressão é construída, o que não precisa de um `ICurrentTenantAccessor` real.

Padrão já usado no EF Core para "mesmo filtro em N tipos via marker interface" — não é uma técnica inventada para este projeto.

`OperationalAlertDTO` **não** implementa `ILicenseScoped** (por causa do `Guid?`) — recebe um `HasQueryFilter` próprio, escrito à mão, na sua `IEntityTypeConfiguration`:

```csharp
builder.HasQueryFilter(x =>
    x.LicenseId == null || (_tenant.AccessibleLicenseIds != null && _tenant.AccessibleLicenseIds.Contains(x.LicenseId.Value)));
```

### As 27 entidades `ILicenseScoped` (lista exata, levantada por grep, não estimada)

`AccessRouteDTO`, `AccessOperationAuditDTO`, `AccessBatchOperationDTO`, `AccessInventoryItemDTO`, `AccessEventRecordDTO`, `AmenityDTO`, `AmenityBookingDTO`, `ConfigurationBackupDTO`, `BackupAutomationPolicyDTO`, `BlockDTO`, `DeliveryDTO`, `ResourceDocumentDTO`, `AccessControlDeviceDTO`, `CFTVDeviceDTO`, `BoletoBatchDTO`, `AccessVisitDTO`, `AccessWatchlistEntryDTO`, `RegistrationInviteDTO`, `LicenseCredentialPolicyDTO`, `LicenseUserAccessDTO`, `IncidentDTO`, `AutomationRuleDTO`, `AutomationExecutionDTO`, `EmergencySessionDTO`, `DigitalPassDTO`, `RecycleBinItemDTO`, `TicketDTO`.

### Quebrando a circularidade: as duas exceções deliberadas

`LicenseAuthorizationService` é quem CALCULA o conjunto de licenças acessíveis — se as consultas que ele mesmo faz contra `Licenses`/`LicenseUserAccesses` também estivessem sujeitas ao filtro, o cálculo dependeria do resultado que ainda não existe (conjunto vazio na primeira consulta → filtro esconde tudo → conjunto permanece vazio para sempre). `License`/`LicenseUserAccessDTO` em si continuam com filtro (protegem outras consultas), mas estes dois métodos específicos usam `.IgnoreQueryFilters()`:

- `GetGrantAsync` (`LicenseAuthorizationService.cs:43`): `_context.Licenses.AsNoTracking().IgnoreQueryFilters().AnyAsync(...)`.
- `GetLicensePermissionsAsync` (`LicenseAuthorizationService.cs:75-83`): as duas consultas (`Licenses.Where(...EnterpriseId...)` e `LicenseUserAccesses.Where(...)`) recebem `.IgnoreQueryFilters()`.

Comentário no código em cada uma explicando por quê — essas são as ÚNICAS exceções esperadas neste subsistema; qualquer `.IgnoreQueryFilters()` novo que aparecer depois em outro lugar do código é suspeito e merece revisão extra.

## Testes

- `ICurrentTenantAccessor`/`TenantScopeActionFilter`: teste populando o accessor via um `ClaimsPrincipal` de morador — confirma que o conjunto vira exatamente `{grant.LicenseId}`, nunca vazio nem `null`, para um morador com vínculo válido.
- Teste de circularidade: com o filtro ativo e o accessor ainda não populado (`AccessibleLicenseIds = null`), chamar `GetAccessibleLicenseIdsAsync` diretamente e confirmar que retorna o conjunto correto (não vazio) — prova que as duas exceções `.IgnoreQueryFilters()` realmente quebram o ciclo.
- Teste de isolamento: duas licenças de empresas diferentes, cada uma com uma `DeliveryDTO`; um usuário com acesso só à primeira licença consulta `Deliveries` sem filtro explícito nenhum na query (simulando um controller que "esqueceu") — confirma que só a encomenda da licença acessível volta.
- Teste do caso `OperationalAlertDTO`: um alerta com `LicenseId = null` continua visível independente do conjunto acessível do usuário; um alerta com `LicenseId` de uma licença fora do conjunto não aparece.
- Teste de não-regressão: `OperationsController.GetDashboard` (ou o handler equivalente) continua retornando os mesmos números com o filtro ativo que sem ele, para um usuário com acesso a múltiplas licenças — prova que o filtro (conjunto amplo) não conflita com os filtros explícitos mais estreitos que o dashboard já usa por permissão.
