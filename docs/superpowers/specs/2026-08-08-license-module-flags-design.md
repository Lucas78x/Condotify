# Feature Flags de Módulos por Condomínio — Design

Data: 2026-08-08

## Contexto

Hoje todo condomínio (`License`) enxerga os 15 módulos do workspace (`Condotify/Components/Pages/LicenseWorkspace.razor`) incondicionalmente. A única forma de "esconder" um módulo de um usuário é retirar a permissão correspondente (`LicensePermissionEnum`) dele — o que não escala: um condomínio sem área comum reservável, por exemplo, não tem como deixar de mostrar "Agendamento" para todo mundo sem reconfigurar a permissão de cada usuário individualmente.

Este design nasceu de uma auditoria da plataforma (relatório em artifact separado) que identificou a ausência de feature flags por condomínio como lacuna crítica, tanto do ponto de vista de produto (condomínios pequenos veem módulos que nunca usam) quanto de arquitetura (não existe o conceito `Condomínio → Módulos ativos → Permissões → Usuários`).

Decisões já fechadas com o usuário antes deste documento:
- Escopo: por `License` (condomínio), não por usuário/perfil.
- Enforcement: só oculta na UI (portal e mobile). Nenhum controller de API passa a rejeitar chamadas por módulo desativado nesta v1.
- Quem controla: apenas usuários com `AccessType` `Developer` ou `Admin` (administrador da plataforma) — síndico/gestor da licença não vê nem altera esse controle.
- Superfícies: portal web **e** app mobile, ambos devem respeitar o flag.
- Default: todo condomínio (existente ou novo) nasce com todos os módulos opcionais ligados. Nada muda no comportamento atual até um admin desligar algo.

## Escopo

Dentro do escopo:
- Novo bitmask `EnabledModules` na licença, cobrindo os 10 módulos opcionais definidos abaixo.
- Endpoint de leitura (embutido na resposta de licença já existente) e endpoint de escrita (novo, restrito a `Developer`/`Admin`).
- Filtro de navegação no portal (`LicenseWorkspace.razor`) e no mobile (`MobileNavigation`) respeitando o flag.
- Tela de administração (dentro da aba "Administração", visível só para `Developer`/`Admin`) para ligar/desligar cada módulo.

Fora do escopo (v1):
- Bloqueio em nível de API/controller — um `POST` direto num módulo desativado continua funcionando; só a UI oculta o caminho até lá.
- Síndico/gestor da licença poder alterar isso — fica só com a plataforma.
- Qualquer granularidade abaixo de "módulo inteiro" (não há flag por sub-recurso).

### Módulos sempre ativos (não fazem parte do bitmask)
Visão geral, Estrutura, Credenciais, Administração, Acessos.

### Módulos opcionais (cobertos pelo bitmask)
Câmeras, Equipamentos, Rotas, Ocorrências, Automações, Emergência, Encomendas, Agendamento, Boletos, Documentos.

## Modelo de dados

### `LicenseModuleEnum` (novo, `CondotifyAPI.Domain/Enums/License/LicenseModuleEnum.cs`)

```csharp
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

Segue o mesmo estilo bitmask já usado por `LicensePermissionEnum` (`CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`) — não introduz um padrão novo na base.

### Coluna nova em `Licenses`

A cadeia atual é `LicenseDTO` (EF, `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs`) → `License` (modelo de domínio, `CondotifyAPI.Domain/Models/License/License.cs`, mapeado via AutoMapper em `CondotifyProfile.cs`) → `LicenseSummaryViewModel` (`CondotifyAPI/ViewModel/LicenseSummaryViewModel.cs`, resposta de `GET api/access/licenses/{id}` em `LicenseAccessController`) → `LicenseFullViewModel` (`Condotify.Contracts`, o que o cliente desserializa). O campo precisa existir nas quatro camadas — é o preço da estrutura em camadas já existente, não algo criado por este design.

- `LicenseDTO.EnabledModules` (`long`, novo).
- `LicenseConfiguration.cs`: `builder.Property(l => l.EnabledModules).HasConversion(new ValueConverter<LicenseModuleEnum, long>(x => (long)x, x => (LicenseModuleEnum)x)).HasDefaultValue(LicenseModuleEnum.All).IsRequired();` — mesmo padrão de conversor já usado para `Organization`/`Building`. `HasDefaultValue(LicenseModuleEnum.All)` faz o Postgres preencher `1023` em todas as linhas existentes na migration, sem script de backfill manual.
- `License.EnabledModules` (modelo de domínio) e `LicenseSummaryViewModel.EnabledModules`/`LicenseFullViewModel.EnabledModules` (`long`) — mapeamento automático por nome via AutoMapper/atribuição direta, sem `.ForMember` extra necessário.

### Migration
Uma migration (`AddLicenseEnabledModules`) adicionando a coluna com o default acima. Aplicar e verificar contra o banco local, como nas migrations anteriores desta sessão.

## Backend

### Leitura
Nenhum endpoint novo — `EnabledModules` passa a vir dentro da resposta que `CondotifyApiClient.GetLicenseAsync` já busca (`GET api/access/licenses/{id}`, `LicenseAccessController`). Qualquer usuário com grant na licença já pode ler essa resposta hoje; nenhuma mudança de autorização aqui.

### Escrita
Novo `PUT api/access/licenses/{id}/modules` em `LicenseAccessController` (mesmo arquivo do `CreateByEnterprise`, que já tem `_context` disponível e já implementa a checagem "só Developer/Admin"):

```csharp
[HttpPut("{id:guid}/modules")]
[Authorize]
public async Task<IActionResult> UpdateModules(Guid id, [FromBody] UpdateLicenseModulesIn input)
{
    if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ||
        !Guid.TryParse(User.FindFirstValue("enterprise_id"), out var enterpriseId))
        return Forbid();

    var canManage = await _context.Users.AsNoTracking().AnyAsync(x =>
        x.Id == userId && x.EnterpriseId == enterpriseId &&
        (x.AccessType == AccessTypeEnum.Admin || x.AccessType == AccessTypeEnum.Developer));
    if (!canManage) return Forbid();

    var license = await _context.Licenses.FirstOrDefaultAsync(x => x.Id == id);
    if (license is null) return NotFound();

    license.EnabledModules = (LicenseModuleEnum)input.EnabledModules;
    await _context.SaveChangesAsync();
    return Ok(new { EnabledModules = (long)license.EnabledModules });
}
```

Reaproveita literalmente a mesma checagem inline que `CreateByEnterprise` já usa (linha ~112-115 do arquivo) — não existe hoje um atributo genérico "só Developer/Admin", então este design não inventa um novo mecanismo de autorização, só repete o padrão existente uma segunda vez. Se um terceiro caso do mesmo tipo aparecer depois, aí sim vale extrair um atributo/policy compartilhado — não agora (YAGNI).

`UpdateLicenseModulesIn` é um DTO de request simples: `{ long EnabledModules }`.

## Frontend (Blazor + MudBlazor)

### `LicenseWorkspace.razor`
A lista `TabGroups` ganha um `LicenseModuleEnum?` opcional por `WorkspaceTab` (`null` = sempre ativo, os 5 módulos fixos). O filtro de visibilidade (hoje só `Has(t.Permission)`) passa a exigir também `IsModuleEnabled(tab.Module)`:

```csharp
private bool IsModuleEnabled(LicenseModuleEnum? module) =>
    module is null || (_license?.EnabledModules & (long)module.Value) != 0;
```

`SectionAllowed` e `DefaultSection` recebem o mesmo segundo critério, para impedir acesso direto por URL a um módulo desativado (o usuário digitando a rota manualmente cai no mesmo aviso "sem permissão" que já existe para módulos sem permissão — reaproveita a UI de bloqueio existente, não cria uma nova).

### Nova seção "Módulos" (dentro da aba Administração)
Visível só quando a claim `access_type` do usuário logado é `Admin` ou `Developer` — mesma checagem que `NewLicense.razor:59` já usa para restringir a criação de condomínio (`(await AuthenticationStateProvider.GetAuthenticationStateAsync()).User.FindFirst("access_type")?.Value is ("Admin" or "Developer")`), reaproveitada aqui, não uma checagem nova. Lista os 10 módulos opcionais com um `MudSwitch` cada, salva via `PUT .../modules`, snackbar de confirmação. Segue o padrão visual das "Políticas de credenciais" já existentes na mesma aba.

## Mobile (MAUI Blazor Hybrid)

`Condotify.Contracts.LicenseFullViewModel.EnabledModules` chega de graça ao app mobile porque `Condotify.ApiClient` já é compartilhado entre `Condotify` e `Condotify.Mobile` (mesmo client, mesmos contratos). `MobileNavigation.For(principal)` (`Condotify.Mobile.Core`) passa a receber também o `EnabledModules` da licença ativa e filtrar os itens de navegação inferior/menu "Mais" cujo módulo correspondente esteja desligado, além do filtro por `MobilePrincipalKind` que já existe. Como `MobileNavigation` já é coberto por testes em `Condotify.Mobile.Tests` (navegação por perfil), o novo filtro por módulo se encaixa no mesmo arquivo de teste.

## Testes

- `LicenseModuleEnum`: teste de que `All` cobre exatamente os 10 bits esperados (evita erro de digitação ao adicionar/remover um módulo).
- Migration: aplicar contra o banco local e confirmar que licenças existentes ficam com `EnabledModules = 1023` (All) sem script manual.
- `LicenseAccessController.UpdateModules`: teste de autorização (Developer/Admin passa, qualquer outro AccessType recebe Forbid) e teste de persistência (grava e o `GET` seguinte reflete o novo valor).
- `MobileNavigation.For`: estender os testes existentes de navegação por perfil para cobrir também a filtragem por módulo desativado (caso feliz: item some; caso módulo sempre-ativo: nunca some).
- Manual: confirmar no portal que desligar "Agendamento" via admin remove a aba do síndico sem precisar mexer em permissão nenhuma, e que o síndico não vê a seção "Módulos".
