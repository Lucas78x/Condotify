# Trilha de auditoria genérica — Design

> Subsistema #13 (parte 1 de 2) do audit original do Condotify. Parte 2 (expansão da lixeira) é um spec separado, sequencial, que reaproveita o serviço de auditoria construído aqui.

**Contexto:** hoje o Condotify tem uma tabela genérica `AccessOperationAudits` (`AccessOperationAuditDTO`), mas seu uso é disperso — cada controller tem seu próprio helper privado duplicado (`AddManagementAudit`, `AddAudit`, `Audit`), só ~6 controllers a populam, e ela só é exposta na UI através de um diálogo ("Processamentos") restrito ao contexto de Access Control. A maioria das operações destrutivas do sistema (exclusão de anúncios, documentos, boletos, comodidades, reservas, credenciais avulsas, passes digitais, etc.) não deixa nenhum rastro de quem fez o quê. Além disso, o campo `UserId` da tabela nunca é de fato preenchido — só `UserName`, um texto solto sem vínculo com a conta.

## Arquitetura

Renomear `AccessOperationAuditDTO`/`AccessOperationAudits` para `AuditLogDTO`/`AuditLogs` (migration de rename, dados preservados) e introduzir um `IAuditService` central com um único método, substituindo os helpers duplicados de hoje. O serviço é chamado explicitamente em cada controller, logo antes/junto do `SaveChangesAsync` que executa a operação real — mesmo padrão de hoje, só que unificado. Sem interceptor de `SaveChanges`: chamada explícita preserva nuance semântica (`"Trashed"` vs `"Updated"` vs `"Cancelled"`, resumos legíveis) que um interceptor genérico perderia.

## Modelo de dados

```csharp
public class AuditLogDTO : CondotifyAPI.Domain.Interfaces.ILicenseScoped
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string Action { get; set; } = string.Empty; // Created | Updated | Trashed | Deleted | Restored | Cancelled | Revoked
    public string Status { get; set; } = string.Empty; // mantido: carrega valores reais (Success, Queued, Failed, Recovered, Alert, Warning...)
    public string Summary { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public Guid? UserId { get; set; }   // NOVO: agora de fato preenchido, via ClaimTypes.NameIdentifier
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
```

**Correção pós-aprovação (achado durante a pesquisa para o plano de implementação):** a suposição original de que `Status` era sempre `"Success"` estava errada — o campo carrega valores distintos reais em ~1/3 dos pontos de chamada (`"Queued"`, `"Recorded"`, `"Failed"`, `"Recovered"`, `"Alert"`, `"Warning"`), incluindo o status `"Failed"` de backups automáticos malsucedidos e os estados de alerta de saúde de equipamentos. **`Status` é mantido**, não removido. Mudança real em relação a `AccessOperationAuditDTO`: só `UserId`, que passa de nunca-preenchido para de fato populado. Migration: rename de tabela, sem alteração de colunas, sem período de dupla tabela.

**Retenção:** indefinida — sem expiração automática, sem worker de purga. É o histórico permanente de quem fez o quê.

## `IAuditService`

```csharp
namespace CondotifyAPI.Services.Audit;

public interface IAuditService
{
    // Para controllers HTTP: ator resolvido automaticamente do usuário autenticado da requisição.
    Task LogAsync(
        Guid licenseId,
        string entityType,
        Guid? entityId,
        string action,
        string summary,
        object? details = null,
        string status = "Success",
        CancellationToken cancellationToken = default);

    // Para workers/services em background, sem HttpContext: ator informado explicitamente
    // (ex: "Monitor de equipamentos", "Automação de backup").
    Task LogSystemAsync(
        Guid licenseId,
        string entityType,
        Guid? entityId,
        string action,
        string summary,
        string actorName,
        object? details = null,
        string status = "Success",
        CancellationToken cancellationToken = default);
}
```

**Correção pós-aprovação:** a spec original só previa `LogAsync` resolvendo o ator via `HttpContext.User` — mas ~13 dos 31 pontos de chamada existentes rodam em background workers/services (`DeviceHealthMonitoringWorker`, `AutomaticBackupRunner`, `AccessEventIngestionWorker`, `CredentialReconciliationService`, `DigitalPassIssuanceService`, `ConfigurationBackupService`, `IncidentService`, `DeviceInventoryService`, `RecycleBinService`) sem requisição HTTP em andamento — hoje eles gravam `UserName` com um texto de ator do sistema (`"Monitor de equipamentos"`, etc.), nunca um `UserId`. Por isso o serviço tem dois métodos: `LogAsync` (controllers, resolve `UserId`/`UserName` do `ClaimsPrincipal`) e `LogSystemAsync` (workers, recebe `actorName` explícito, `UserId` sempre nulo). Ambos escrevem na mesma tabela `AuditLogs`.

Implementação injeta `DatabaseContext` + `IHttpContextAccessor` (este último precisa ser registrado em `Program.cs` — não está registrado hoje; os helpers atuais leem `User` diretamente da propriedade do controller, não via accessor). Nenhum dos dois métodos chama `SaveChangesAsync` — a entrada só é persistida quando o `SaveChangesAsync` do chamador for executado, garantindo atomicidade: se a operação principal falhar antes de salvar, a entrada de auditoria correspondente também não é gravada.

`UserId` em `LogAsync` é extraído via `ClaimTypes.NameIdentifier` do `ClaimsPrincipal` atual — a mesma claim já usada em outros ~15 pontos do código, tanto para principals de equipe quanto de morador (`JwtTokenService.cs`).

## Escopo de cobertura

**Correção pós-aprovação:** a contagem original ("12 pontos de chamada em 6 controllers") só levou em conta controllers HTTP e subestimou o real. A busca por `AccessOperationAudits.Add(` no código encontrou **31 pontos de chamada reais em 24 arquivos** — 15 controllers e 9 services/workers em background. A tabela abaixo substitui a lista original.

**A. Migrar os 31 pontos de chamada já existentes (24 arquivos)** — mesmos eventos já auditados hoje, refatorados para usar `IAuditService`, ganhando `UserId` real onde houver requisição HTTP:

*Controllers (18 pontos de chamada, 15 arquivos) — usam `LogAsync`:*
`AlertNotificationsController`, `AutomationRulesController`, `AccessControlOperationsController`, `AccessRoutesController`, `ConfigurationBackupsController` (3 pontos), `ConciergeController`, `DeviceAccessControler`, `EmergencyController`, `IncidentsController`, `LicenseStructureController`, `OperationalAlertsController` (2 pontos), `PeopleManagementController`, `RecycleBinController`, `ResidentProfileController`, `StructureImportsController`.

*Services/workers em background (13 pontos de chamada, 9 arquivos) — usam `LogSystemAsync`:*
`Services/Backups/ConfigurationBackupService` (3 pontos), `Services/Backups/AutomaticBackupRunner` (2 pontos), `Services/RecycleBin/RecycleBinService`, `Services/Operations/DigitalPassIssuanceService` (2 pontos), `Services/Operations/IncidentService`, `Services/AccessControl/CredentialReconciliationService`, `Services/AccessControl/AccessEventIngestionWorker`, `Services/AccessControl/DeviceHealthMonitoringWorker` (via helper `Alert()`), `Services/AccessControl/DeviceInventoryService`.

Os arquivos e linhas exatos de cada ponto de chamada serão levantados e listados no plano de implementação (task por task), não repetidos aqui.

**B. Cobertura nova — só ações destrutivas** (Create/Update permanecem sem auditoria nestas entidades; só a ação irreversível é registrada):

| Entidade | Controller | Ação |
|---|---|---|
| Announcement | `AnnouncementsController.cs:83` | Delete |
| Amenity | `AmenitiesController.cs:178` | Delete |
| AmenityBooking | `AmenityBookingsController.cs:392` / `ResidentProfileController.cs:529` | Cancel |
| ResourceDocument | `ResourceDocumentsController.cs:127` | Delete |
| Boleto (documento) | `BoletosController.cs:409` | Delete |
| RouteOverride | `AccessControlOperationsController.cs:378` | Delete |
| Batch operation | `AccessControlOperationsController.cs:237` | Cancel |
| Digital pass | `DigitalPassesController.cs:39` / `ResidentProfileController.cs:156` | Revoke |
| Push installation | `MobileNotificationsController.cs:76` | Retire |

Total: 21 pontos de chamada em 13 arquivos de controller.

## Permissão e UI

Nova permissão `ViewAuditLog = 1L << 36` em `LicensePermissionEnum` (próximo bit livre após `ManageAnnouncements`), espelhada em `Condotify.Contracts`. Bit único, sem par `Manage` — auditoria é somente leitura por natureza (mesmo padrão de `ManageAnnouncements`, que também é um único bit cobrindo a funcionalidade inteira). Backfill nos registros existentes de Administrator/Manager (`Role IN (0,1)`) na mesma migration que renomeia a tabela, seguindo a convenção já estabelecida de backfill de permissões.

Sem novo bit em `LicenseModuleEnum` — auditoria é infraestrutura administrativa central, não um módulo opcional por licença (mesmo raciocínio da lixeira, que também não é controlada por toggle de módulo).

**UI:** nova aba "Auditoria" em `LicenseWorkspace.razor` (portal), usando o mesmo padrão de 5 pontos de integração já usado em Comunicados (`TabGroups`, switch, `DefaultSection`, `SectionAllowed`, `ModuleFor`), protegida por `ViewAuditLog`. A página: lista filtrável e paginada — filtros por tipo de entidade, ação, usuário e intervalo de data — cada linha mostrando timestamp, usuário, entidade, ação e resumo; clicar numa linha expande o `DetailsJson` formatado (não bruto). Novo endpoint `GET api/access/licenses/{licenseId}/audit-log` com esses filtros como query params, paginado (reaproveitando o padrão de paginação já existente no código para outros endpoints de listagem — o padrão exato será confirmado ao escrever o plano de implementação, não inventado aqui).

O `AccessOperationsDialog.razor` ("Processamentos") existente permanece como está — é uma visão diferente e mais estreita (status de processamento de sincronização de equipamentos ao vivo) que só por acaso lê da mesma tabela subjacente; não é substituído pela nova página neste plano.

## Testes

- `AuditServiceTests`: unitário, verifica que `LogAsync` popula todos os campos corretamente a partir de um `ClaimsPrincipal` fake (incluindo `UserId`) e que `LogSystemAsync` popula `UserName` a partir do `actorName` informado com `UserId` nulo; ambos só persistem a entrada após o `SaveChangesAsync` do chamador (não commitam sozinhos).
- Testes de regressão por controller (um por ação destrutiva nova, 9 no total): cada um exercita a ação HTTP real e, em caso de sucesso, verifica que uma linha `AuditLogDTO` foi persistida com `EntityType`/`Action`/`EntityId` corretos — mesmo padrão "exercitar a ação real, verificar linha persistida" usado no teste de regressão de push de Comunicados.
- Teste de migration: confirma que as linhas existentes de `AccessOperationAudits` sobrevivem ao rename como linhas de `AuditLogs`, com todas as colunas (incluindo `Status`) intactas (round-trip Postgres, mesma convenção de verificação de migration já usada no projeto).
- Portal: a nova aba "Auditoria" segue verificação manual/de build, como nos subsistemas anteriores (não há harness de teste de componente Blazor neste projeto ainda).

## Fora de escopo (deferido para o item #13, parte 2)

- Expansão da lixeira (`IRecycleBinService`) para cobrir as entidades que hoje fazem hard delete sem rede de segurança (Announcement, Amenity, ResourceDocument, Boleto, Digital pass, etc.) — spec e plano separados, que reaproveitam este `IAuditService`.
- Arquivamento/purga de auditoria antiga — se o volume da tabela crescer a ponto de ser um problema, isso é um item futuro distinto, não parte deste plano.
