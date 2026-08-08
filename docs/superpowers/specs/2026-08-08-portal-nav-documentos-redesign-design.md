# Menu de módulos e tela de Documentos — redesign visual (portal web)

## Contexto

Disparado por um print de 08/08/2026 do ambiente do condomínio no portal
web (staff/síndico), aprovado via mockup interativo (artifact) que recriou
as duas telas usando os tokens reais de `design-system.css`/`portal.css`.
Dois problemas, ambos puramente visuais/estruturais — nenhuma mudança de
modelo de dados, permissão ou API:

1. **Menu de módulos** (`LicenseWorkspace.razor`): com todas as permissões,
   a licença expõe até 15 abas (`visao-geral`, `equipamentos`, `rotas`,
   `cameras`, `estrutura`, `credenciais`, `acessos`, `ocorrencias`,
   `automacoes`, `emergencia`, `encomendas`, `agendamento`, `boletos`,
   `documentos`, `administracao`) em uma única fila horizontal com scroll e
   dois `MudIconButton` de seta. Metade fica fora da viewport sem nenhuma
   pista visual de que existe mais conteúdo.
2. **Módulo Documentos** (`DocumentsModule.razor`, portal do síndico): título,
   filtro e botão "Novo documento" soltos sobre um estado vazio genérico
   (ícone de pasta cinza) e uma `MudTable` padrão quando há conteúdo — sem
   a identidade visual por categoria (ícone/cor) que o app do morador
   (`Condotify.Mobile/.../Documentos.razor`) já usa desde o commit
   `3a6ba0e`.

Decisão validada com o usuário no mockup: agrupar o menu por domínio (não a
alternativa "pinados + Mais módulos").

**Fora de escopo**: qualquer mudança em `LicensePermission`, nos DTOs de
documento, no fluxo de upload/exclusão, ou na tela de Documentos do app
mobile (que já está com o tratamento por categoria correto).

## 1. Menu de módulos agrupado

### Estrutura de dados

Hoje cada aba é um `@if (Has(Permission)) { @NavButton(...) }` solto em
sequência — não dá para saber, sem rodar, se um grupo inteiro ficou vazio
(e portanto se o rótulo do grupo deve sumir). Substituir por uma estrutura
de dados no `@code`:

```csharp
private sealed record WorkspaceTab(string Section, string Label, string Icon, LicensePermission Permission);

private static readonly WorkspaceTab PinnedTab =
    new("visao-geral", "Visão geral", Icons.Material.Outlined.Dashboard, LicensePermission.ViewDashboard);

private static readonly (string GroupLabel, WorkspaceTab[] Tabs)[] TabGroups =
[
    ("Monitoramento", [
        new("cameras", "Câmeras", Icons.Material.Outlined.Videocam, LicensePermission.ViewDevices),
        new("equipamentos", "Equipamentos", Icons.Material.Outlined.Sensors, LicensePermission.ViewDevices),
        new("rotas", "Rotas", Icons.Material.Outlined.AltRoute, LicensePermission.ViewDevices),
        new("acessos", "Acessos", Icons.Material.Outlined.FactCheck, LicensePermission.ViewEvents),
    ]),
    ("Operação", [
        new("ocorrencias", "Ocorrências", Icons.Material.Outlined.AssignmentLate, LicensePermission.ViewIncidents),
        new("automacoes", "Automações", Icons.Material.Outlined.AutoAwesome, LicensePermission.ViewAutomations),
        new("emergencia", "Emergência", Icons.Material.Outlined.HealthAndSafety, LicensePermission.ViewEmergency),
        new("encomendas", "Encomendas", Icons.Material.Outlined.Inventory2, LicensePermission.ViewDeliveries),
        new("agendamento", "Agendamento", Icons.Material.Outlined.Deck, LicensePermission.ViewBookings),
    ]),
    ("Financeiro & Documentos", [
        new("boletos", "Boletos", Icons.Material.Outlined.ReceiptLong, LicensePermission.ManageFinance),
        new("documentos", "Documentos", Icons.Material.Outlined.Description, LicensePermission.ManageDocuments),
    ]),
    ("Configuração", [
        new("estrutura", "Estrutura", Icons.Material.Outlined.AccountTree, LicensePermission.ViewStructure),
        new("credenciais", "Credenciais", Icons.Material.Outlined.Badge, LicensePermission.ViewCredentials),
        new("administracao", "Administração", Icons.Material.Outlined.AdminPanelSettings, LicensePermission.ViewUsers),
    ]),
];
```

Ressalva sobre "Administração": hoje sua visibilidade é
`Has(ViewUsers) || Has(ViewSettings) || Has(ViewBackups) || Has(ViewAlerts)`
— um OR de 4 permissões, não uma única. `WorkspaceTab.Permission` cobre só
o caso de 1 permissão; a aba "Administração" entra em `TabGroups` com
`Permission = LicensePermission.ViewUsers` só como placeholder (nunca é
lido para ela) e sua visibilidade real vem de uma propriedade nova:

```csharp
private bool AdministracaoVisible =>
    Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) ||
    Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts);
```

tratada como caso especial dentro do loop de renderização do grupo
"Configuração" (ver markup abaixo). `SectionAllowed` (usada para bloquear
acesso direto por URL) **não muda** — continua sua própria função de
mapeamento, independente da estrutura de renderização do menu.

### Markup

O `<div class="workspace-nav-shell">` que envolve a `<nav>` **continua
existindo** — é ele quem hoje desenha a caixa branca com borda/sombra/
`position: sticky` que aparece no print (ver seção CSS abaixo); só os dois
`MudIconButton` de seta saem de dentro dele:

```razor
<div class="workspace-nav-shell">
    <nav class="workspace-nav" aria-label="Módulos do condomínio">
        @if (Has(LicensePermission.ViewDashboard))
        {
            <div class="workspace-nav-pinned">@NavButton(PinnedTab.Section, PinnedTab.Label, PinnedTab.Icon)</div>
            <span class="workspace-nav-sep"></span>
        }
        @foreach (var (groupLabel, tabs) in TabGroups)
        {
            var visible = tabs.Where(t => t.Section == "administracao" ? AdministracaoVisible : Has(t.Permission)).ToList();
            if (visible.Count > 0)
            {
                <div class="workspace-nav-group">
                    <span class="workspace-nav-group-label">@groupLabel</span>
                    @foreach (var tab in visible)
                    {
                        @NavButton(tab.Section, tab.Label, tab.Icon)
                    }
                </div>
            }
        }
    </nav>
</div>
```

`NavButton` (o `RenderFragment` que builda o `MudButton` via
`RenderTreeBuilder`) **não muda** — mesmo Filled/Text por seção ativa.

### CSS

Achado importante ao investigar o CSS existente: a caixa visual (borda,
fundo, sombra, `position: sticky`) que aparece no print **não vem de
`portal.css`**, vem de `.workspace-nav-shell` em **`design-system.css:353-368`**
— esse arquivo é carregado depois de `portal.css` (ver `Components/App.razor`)
e por isso vence a cascata. O bloco `.workspace-nav` que existe em
`portal.css:205-216` (border/background/box-shadow próprios) está **morto
hoje** — é inteiramente sobrescrito por `design-system.css:370-388`, que
zera `border`/`background`/`box-shadow` e ativa o `overflow-x: auto` +
`scroll-snap-type` que faz a rolagem atual. Todas as edições de CSS deste
redesign vão em **`design-system.css`**; os blocos mortos em `portal.css`
são removidos para não confundir quem ler depois.

Editar em `design-system.css:353-420` (substitui `.workspace-nav-shell`,
`.workspace-nav`, `.workspace-nav::-webkit-scrollbar`,
`.workspace-nav .mud-button-root`, remove `.workspace-nav-control` por
completo — mantém `.workspace-nav .mud-button-filled` com a animação
`motion-nav-active`, que continua válida para destacar a aba ativa):

```css
.workspace-nav-shell {
    position: sticky;
    top: 76px;
    z-index: 8;
    padding: 8px;
    margin-bottom: 22px;
    border: 1px solid var(--ds-border);
    border-radius: var(--ds-radius-lg);
    background: rgba(255, 255, 255, .97);
    box-shadow: var(--ds-shadow-xs);
    backdrop-filter: blur(12px);
}

.workspace-nav {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 4px;
}

.workspace-nav .mud-button-root {
    flex: 0 0 auto;
    min-height: 38px;
    padding-inline: 12px;
    border-radius: var(--ds-radius-sm);
    font-size: .72rem;
}

.workspace-nav .mud-button-filled {
    animation: motion-nav-active var(--motion-base) var(--motion-enter);
}

.workspace-nav-pinned { display: flex; }
.workspace-nav-group { display: flex; flex-wrap: wrap; align-items: center; gap: 2px; }
.workspace-nav-group-label {
    padding: 0 8px 0 6px;
    font-size: 10px;
    font-weight: 700;
    letter-spacing: .07em;
    text-transform: uppercase;
    color: var(--ds-subtle);
}
.workspace-nav-sep { width: 1px; align-self: stretch; margin: 4px 6px; background: var(--ds-border); }
```

E a regra responsiva em `design-system.css:1218-1223` (dentro do media
query de viewport estreito que já existe) perde o `grid-template-columns`
— não há mais 3 colunas (seta/nav/seta), só a caixa encolhendo:

```css
.workspace-nav-shell {
    top: 72px;
    margin-inline: -4px;
    padding: 6px;
}
```

Em `portal.css`: remove o bloco morto `.workspace-nav { ... }` (linhas
205-216) e a regra responsiva `.workspace-nav .mud-button-root { flex: 0 0
auto; }` (linha 687) — ambos sobrescritos e sem efeito hoje, viram lixo se
ficarem.

### Componente (`LicenseWorkspace.razor`)

Remove por completo (não ficam "desligados", são deletados — este redesign
elimina a necessidade deles):
- Os dois `MudIconButton` de seta (a `<div class="workspace-nav-shell">`
  continua existindo, só perde os dois botões — ver Markup acima).
- `ElementReference _workspaceNav`, `_lastCenteredSection`,
  `ScrollWorkspaceNavAsync`, e o override inteiro de `OnAfterRenderAsync`
  (linhas 137-149) — sua única função era chamar
  `portalInterop.centerActiveNavItem` para centralizar a aba ativa depois
  de rolar; sem scroll, não há o que centralizar.
- Em `portal-interop.js`: `scrollHorizontal` e `centerActiveNavItem` —
  confirmado por busca no repositório que **nenhum outro componente** usa
  essas duas funções.

### Casos de borda

- Usuário só com `ViewDashboard` (sem mais nenhuma permissão de módulo):
  aparece só a aba pinada "Visão geral", sem separador (o `<span
  class="workspace-nav-sep">` só renderiza junto com o pin, e nenhum grupo
  renderiza por estar todos vazios) — nav fica com uma única aba, visual
  ainda correto (`.workspace-nav` com um item só, sem quebra estranha).
- Usuário sem `ViewDashboard` mas com outras permissões: pin não aparece,
  primeiro grupo visível começa direto sem separador solto à esquerda.
- Tela estreita (tablet na portaria, se existir esse uso): `flex-wrap`
  cria mais linhas — sem novo componente, é o comportamento nativo do
  layout, testado nos breakpoints já existentes em `portal.css` (~768px).

## 2. Documentos: identidade visual por categoria + cards

### Mapeamento categoria → cor (novo, em `portal.css`)

Reaproveita tokens já existentes em `design-system.css`
(`--ds-primary`/`-soft`, `--ds-teal`/`-soft`, `--ds-amber`/`-soft`,
`--ds-teal`/`-soft` já existem mas hoje não são usados por nenhum
componente do portal — este é o primeiro uso). Só "neutro" é token novo.

| Categoria (`ResourceDocumentCategoryEnum`) | Classe CSS | Cor | Ícone (igual ao já usado no app mobile) |
|---|---|---|---|
| `Minutes` (Ata) | `.category-primary` | `--ds-primary` / `--ds-primary-soft` | `Icons.Material.Outlined.Article` |
| `ByLaws` (Regimento Interno) | `.category-neutral` | `--ds-muted` / `--ds-surface-muted` | `Icons.Material.Outlined.Gavel` |
| `Covenant` (Convenção) | `.category-teal` | `--ds-teal` / `--ds-teal-soft` | `Icons.Material.Outlined.Handshake` |
| `Announcement` (Comunicado) | `.category-amber` | `--ds-amber` / `--ds-amber-soft` | `Icons.Material.Outlined.Campaign` |
| `FinancialStatement` (Prestação de Contas) | `.category-green` | `#08715f` / `#e8f5f1` (mesmo verde de `.stat-icon.green`, já em uso em `portal.css`) | `Icons.Material.Outlined.ReceiptLong` |
| `Other` (Outro) | `.category-neutral` | igual a `ByLaws` | `Icons.Material.Outlined.InsertDriveFile` |

```css
.category-badge { width: 38px; height: 38px; border-radius: 9px; display: grid; place-items: center; flex: none; }
.category-chip { display: inline-flex; align-items: center; gap: 5px; font-size: 10.5px; font-weight: 700; padding: 4px 9px 4px 7px; border-radius: 999px; white-space: nowrap; }
.category-primary { color: var(--ds-primary-strong); background: var(--ds-primary-soft); }
.category-teal     { color: var(--ds-teal); background: var(--ds-teal-soft); }
.category-amber    { color: var(--ds-amber); background: var(--ds-amber-soft); }
.category-green    { color: #08715f; background: #e8f5f1; }
.category-neutral  { color: var(--ds-muted); background: var(--ds-surface-muted); }
```

`.category-badge` recebe uma das 5 classes de cor acima; `.category-chip`
reaproveita as mesmas.

Em `DocumentsModule.razor` (`@code`), troca `CategoryColor(string) : Color`
(enum do MudBlazor, só usado pelo `MudChip` da tabela que está sendo
removida) por `CategoryClass(string) : string` retornando o nome da classe
CSS acima. `CategoryIcon`/`CategoryLabel` **não mudam**.

### Estado vazio

Troca o `<div class="empty-state compact-empty">` atual (ícone de pasta +
1 linha de texto) por:

```razor
<div class="empty-state compact-empty documents-empty">
    <span class="category-badge category-primary" style="width:56px;height:56px;border-radius:16px;">
        <MudIcon Icon="@Icons.Material.Outlined.Description" Size="Size.Large" />
    </span>
    <MudText Typo="Typo.subtitle1">@EmptyStateTitle</MudText>
    <MudText Typo="Typo.body2" Color="Color.Secondary">@EmptyStateHint</MudText>
    @if (string.IsNullOrWhiteSpace(_categoryFilter))
    {
        <div class="documents-empty-legend">
            @foreach (var option in CategoryOptions)
            {
                <span class="category-chip @CategoryClass(option.Value)">
                    <MudIcon Icon="@CategoryIcon(option.Value)" Size="Size.Small" />@option.Label
                </span>
            }
        </div>
    }
</div>
```

`EmptyStateTitle`/`EmptyStateHint` (já existentes) não mudam de lógica — só
passam a ter esse badge maior + legenda de categorias acima/abaixo delas
quando não há filtro ativo (com filtro ativo, a legenda não faz sentido —
o usuário já escolheu uma categoria).

O botão "Novo documento" já existe no `panel-heading` no topo — não é
duplicado dentro do estado vazio (mantém uma única call-to-action, evita
dois botões fazendo a mesma coisa na tela).

### Lista populada

Troca o `<MudTable>` inteiro por uma lista de cards, reaproveitando
`CategoryClass`/`CategoryIcon`/`CategoryLabel`:

```razor
<div class="document-list">
    @foreach (var document in FilteredDocuments)
    {
        <div class="document-row">
            <span class="category-badge @CategoryClass(document.Category)"><MudIcon Icon="@CategoryIcon(document.Category)" /></span>
            <div class="document-row-body">
                <div class="document-row-top">
                    <span class="document-row-title">@document.Title</span>
                    <span class="category-chip @CategoryClass(document.Category)">@CategoryLabel(document.Category)</span>
                </div>
                @if (!string.IsNullOrWhiteSpace(document.Description))
                {
                    <div class="document-row-desc">@document.Description</div>
                }
                <div class="document-row-meta">Publicado em @document.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy") · @document.UploadedByName</div>
            </div>
            <div class="document-row-actions">
                <MudTooltip Text="Abrir"><MudIconButton Icon="@Icons.Material.Outlined.OpenInNew" Size="Size.Small" OnClick="() => OpenFileAsync(document.Id)" /></MudTooltip>
                <MudTooltip Text="Excluir"><MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error" Disabled="!CanManage" OnClick="() => DeleteAsync(document.Id)" /></MudTooltip>
            </div>
        </div>
    }
</div>
```

```css
.document-list { display: flex; flex-direction: column; gap: 8px; margin-top: 14px; }
.document-row { display: flex; align-items: flex-start; gap: 14px; padding: 14px; border: 1px solid #e7eaf0; border-radius: 9px; transition: border-color .15s, box-shadow .15s; }
.document-row:hover { border-color: #cfd7e5; box-shadow: 0 6px 18px rgba(32,37,50,.06); }
.document-row-body { flex: 1; min-width: 0; }
.document-row-top { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.document-row-title { font-size: 13.5px; font-weight: 700; color: var(--ds-ink); }
.document-row-desc { font-size: 12.5px; color: var(--ds-muted); margin-top: 3px; }
.document-row-meta { font-size: 11.5px; color: var(--ds-subtle); margin-top: 6px; }
.document-row-actions { display: flex; align-items: center; gap: 2px; flex: none; }
```

Toolbar (`panel-heading` com título/subtítulo + botão "Novo documento") e o
`MudSelect` de filtro **não mudam** de posição nem de comportamento — só o
que estava abaixo deles (tabela → cards).

### Casos de borda

- Documento sem `Description`: linha `.document-row-desc` simplesmente não
  renderiza (já é o comportamento atual, só muda o container visual).
- Filtro de categoria ativo + zero resultados: mesmo empty state, mas sem
  a legenda de categorias (só faz sentido mostrar "categorias disponíveis"
  quando não há filtro escolhido).
- Nenhuma mudança no fluxo de upload/exclusão (`DocumentUploadDialog`,
  `OpenUploadAsync`, `DeleteAsync`) — só a apresentação da lista/estado
  vazio ao redor deles.

## Testes

Trabalho é 100% apresentação (Razor + CSS), sem lógica de negócio nova:
- Nenhum teste automatizado novo é necessário — não há branch de lógica
  além da já existente (`Has(permission)`, `FilteredDocuments`,
  `CategoryClass`/`Icon`/`Label`), que já não tinha testes dedicados hoje.
- Verificação manual (rodar o portal localmente): licença com todas as
  permissões (todos os 4 grupos + pin aparecem, sem seta), licença com
  permissão parcial (grupo correto some quando todas as abas dele ficam
  sem permissão), Documentos vazio sem filtro (legenda aparece), Documentos
  vazio com filtro (legenda some), Documentos com 5+ documentos de
  categorias diferentes (cores/ícones batem com a tabela abaixo).
