# Menu de Módulos e Documentos — Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the horizontally-scrolling module nav on the license workspace page with a grouped, wrapping layout, and replace the generic empty state / plain table on the Documentos module with a card-based, category-colored presentation — matching the mockup already approved by the user.

**Architecture:** Pure presentation change across three files (`LicenseWorkspace.razor`, `DocumentsModule.razor`, and the `design-system.css`/`portal.css` stylesheets). No data model, permission, or API changes. No new automated tests — this project has no bUnit/component test harness for Razor markup, and the spec explicitly scopes verification to `dotnet build` + manual browser checks.

**Tech Stack:** ASP.NET Core Blazor Server, MudBlazor 9.7.0, plain custom CSS (no Tailwind/Bootstrap).

## Global Constraints

- No changes to `LicensePermission`, `ResourceDocumentDTO`, or any controller/API surface (spec: "Fora de escopo").
- All new colors must reuse existing `--ds-*` tokens from `design-system.css`, or the exact hex pairs already used by `.stat-icon.green` in `portal.css` (`#08715f` / `#e8f5f1`) — no new brand colors introduced.
- All new CSS classes use kebab-case, matching existing convention (`.content-panel`, `.empty-state`, `.workspace-nav`).
- User-visible strings stay in Brazilian Portuguese, matching the rest of the page.
- Full spec: `docs/superpowers/specs/2026-08-08-portal-nav-documentos-redesign-design.md`.

---

### Task 1: Workspace nav — grouped layout

**Files:**
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor`
- Modify: `Condotify/wwwroot/css/design-system.css:353-420` (the live `.workspace-nav-shell`/`.workspace-nav` rules — this file loads *after* `portal.css` in `Components/App.razor`, so it wins the cascade)
- Modify: `Condotify/wwwroot/css/design-system.css:1218-1223` (responsive override inside the existing `@media (max-width: 760px)` block)
- Modify: `Condotify/wwwroot/css/portal.css:205-216` (delete — dead, overridden by `design-system.css`)
- Modify: `Condotify/wwwroot/css/portal.css:687` (delete — dead, same reason)
- Modify: `Condotify/wwwroot/js/portal-interop.js` (delete `scrollHorizontal` and `centerActiveNavItem`)

**Interfaces:**
- Produces: no new public interface — this is a leaf page component. Nothing else in the codebase references `LicenseWorkspace`'s private members.
- Consumes: `LicensePermission` enum (existing, unchanged), `_administration.CurrentAccess.Has(...)` (existing, unchanged).

- [ ] **Step 1: Replace the nav markup in `LicenseWorkspace.razor`**

Read the file first (`Condotify/Components/Pages/LicenseWorkspace.razor`) to confirm line numbers still match (lines 27-50 for the nav block, lines 109-207 for `@code`) before editing — if another change landed on this branch first, line numbers may have shifted.

Replace lines 27-50 (the `<div class="workspace-nav-shell">...</div>` block) with:

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

- [ ] **Step 2: Add the grouped data model and remove the scroll-centering code in `@code`**

In the `@code` block, remove these members entirely (they exist solely to support the removed chevron-scroll behavior):
- `private ElementReference _workspaceNav;` (was line 117)
- `private string? _lastCenteredSection;` (was line 118)
- The whole `OnAfterRenderAsync` override (was lines 137-149) — its only job was calling `portalInterop.centerActiveNavItem`.
- `ScrollWorkspaceNavAsync` (was lines 151-161).

Add this new data model right after the `[Parameter]` declarations (before `_loading`):

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

Add `AdministracaoVisible` next to `Has`/`SectionAllowed` (near the bottom of `@code`, since it depends on `Has`):

```csharp
    private bool AdministracaoVisible =>
        Has(LicensePermission.ViewUsers) || Has(LicensePermission.ViewSettings) ||
        Has(LicensePermission.ViewBackups) || Has(LicensePermission.ViewAlerts);
```

`NavButton`, `Has`, and `SectionAllowed` stay exactly as they are — do not modify them.

- [ ] **Step 3: Rewrite the live nav CSS in `design-system.css`**

Read `Condotify/wwwroot/css/design-system.css` around lines 350-420 first to confirm the exact current text (it should match what was captured during spec-writing: `.workspace-nav-shell` at 353, `.workspace-nav` at 370, `.workspace-nav::-webkit-scrollbar` at 390, `.workspace-nav .mud-button-root` at 394, `.workspace-nav .mud-button-filled` at 403, `.workspace-nav-control` + its `:hover`/`:focus-visible` at 407-420).

Replace that whole span (from `.workspace-nav-shell {` through the closing `}` of `.workspace-nav-control:hover, .workspace-nav-control:focus-visible { ... }`) with:

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

.workspace-nav-pinned {
    display: flex;
}

.workspace-nav-group {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 2px;
}

.workspace-nav-group-label {
    padding: 0 8px 0 6px;
    font-size: 10px;
    font-weight: 700;
    letter-spacing: .07em;
    text-transform: uppercase;
    color: var(--ds-subtle);
}

.workspace-nav-sep {
    width: 1px;
    align-self: stretch;
    margin: 4px 6px;
    background: var(--ds-border);
}
```

This removes `@keyframes motion-nav-active` usage from nowhere — the keyframe definition itself lives elsewhere (`design-system.css:1091`) and is untouched, still referenced by `.workspace-nav .mud-button-filled` above.

- [ ] **Step 4: Update the responsive override in `design-system.css`**

Find the block inside the existing `@media (max-width: 760px)` query (starts at `design-system.css:1173`) that currently reads:

```css
    .workspace-nav-shell {
        top: 72px;
        margin-inline: -4px;
        grid-template-columns: 32px minmax(0, 1fr) 32px;
        padding: 4px;
    }
```

Replace with:

```css
    .workspace-nav-shell {
        top: 72px;
        margin-inline: -4px;
        padding: 6px;
    }
```

(`grid-template-columns` is dropped — there's no grid layout anymore, just the flex-wrap nav inside.)

- [ ] **Step 5: Delete the dead nav rules in `portal.css`**

Delete this whole block (currently `portal.css:205-216`):

```css
.workspace-nav {
    display: flex;
    gap: 4px;
    overflow-x: auto;
    padding: 6px;
    margin-bottom: 20px;
    border: 1px solid #e2e6ec;
    border-radius: 6px;
    background: white;
    box-shadow: 0 2px 8px rgba(32,37,50,.035);
}
.workspace-nav .mud-button-root { min-width: max-content; flex: 1 1 auto; }
```

And delete this line from inside the `@media (max-width: 760px)` block (currently `portal.css:687`):

```css
    .workspace-nav .mud-button-root { flex: 0 0 auto; }
```

- [ ] **Step 6: Remove the unused JS interop functions**

Read `Condotify/wwwroot/js/portal-interop.js` first to see the exact surrounding commas (the two functions are the last two properties in an object literal, right before the closing `};`). Remove the `scrollHorizontal` and `centerActiveNavItem` properties and their trailing/leading commas so the object literal stays valid JS — the property immediately before `scrollHorizontal` must end with a comma only if something still follows it after removal; since these are the last two entries, the property before `scrollHorizontal` should end the object (no trailing comma) after the removal.

- [ ] **Step 7: Build and fix any compile errors**

Run: `dotnet build "D:\repos\Condotify\Condotify\Condotify.csproj"`
Expected: `Build succeeded.` — 0 errors. If `WorkspaceTab`, `PinnedTab`, `TabGroups`, or `AdministracaoVisible` produce unused-member or type-mismatch errors, fix them before proceeding; do not suppress warnings.

- [ ] **Step 8: Manual verification**

Start the portal (`dotnet run --project "D:\repos\Condotify\Condotify\Condotify.csproj"` or use the project's existing `run` workflow) and open a license workspace page (`/licencas/{id}`) logged in as a user with full permissions. Confirm:
- The nav shows "Visão geral" as a solid-primary pinned button, then a vertical separator, then four labeled groups (Monitoramento, Operação, Financeiro & Documentos, Configuração), each containing the right tabs, with no scroll and no chevrons.
- The nav bar stays visible (sticky) when scrolling down a module with long content (e.g. Estrutura).
- Clicking a tab in any group navigates and highlights that tab (solid primary background).
- Resize the browser to ~700px wide — the nav wraps to multiple lines instead of clipping or scrolling.
- Log in (or simulate via a test license/role) as a user with only `ViewDashboard` — only the pinned "Visão geral" button shows, no stray separator or empty group.

- [ ] **Step 9: Commit**

```bash
git add Condotify/Components/Pages/LicenseWorkspace.razor Condotify/wwwroot/css/design-system.css Condotify/wwwroot/css/portal.css Condotify/wwwroot/js/portal-interop.js
git commit -m "feat(portal): group workspace module nav by domain instead of horizontal scroll"
```

---

### Task 2: Documentos — category color system + empty state

**Files:**
- Modify: `Condotify/Components/LicenseModules/DocumentsModule.razor`
- Modify: `Condotify/wwwroot/css/portal.css` (add new rules near the existing `.empty-state`/`.compact-empty` block, currently around line 228-229)

**Interfaces:**
- Produces: `CategoryClass(string category) : string` — returns one of `category-primary`, `category-teal`, `category-amber`, `category-green`, `category-neutral`. Used by this task's empty-state legend and by Task 3's card list.
- Consumes: existing `CategoryIcon(string) : string` and `CategoryLabel(string) : string` (unchanged), `CategoryOptions` (unchanged), `_categoryFilter` (unchanged).

- [ ] **Step 1: Add the category color CSS to `portal.css`**

Read `Condotify/wwwroot/css/portal.css` around line 228 first to confirm current content (`.empty-state { ... }` and `.compact-empty { ... }`), then add immediately after `.compact-empty { min-height: 240px; border: 0; }`:

```css
.category-badge { width: 38px; height: 38px; border-radius: 9px; display: grid; place-items: center; flex: none; }
.category-badge-lg { width: 56px; height: 56px; border-radius: 16px; }
.category-chip { display: inline-flex; align-items: center; gap: 5px; font-size: 10.5px; font-weight: 700; padding: 4px 9px 4px 7px; border-radius: 999px; white-space: nowrap; }
.category-primary { color: var(--ds-primary-strong); background: var(--ds-primary-soft); }
.category-teal     { color: var(--ds-teal); background: var(--ds-teal-soft); }
.category-amber    { color: var(--ds-amber); background: var(--ds-amber-soft); }
.category-green    { color: #08715f; background: #e8f5f1; }
.category-neutral  { color: var(--ds-muted); background: var(--ds-surface-muted); }
.documents-empty-legend { display: flex; flex-wrap: wrap; justify-content: center; gap: 6px; margin-top: 2px; max-width: 460px; }
```

- [ ] **Step 2: Replace `CategoryColor` with `CategoryClass` in `DocumentsModule.razor`**

Read the file first (`Condotify/Components/LicenseModules/DocumentsModule.razor`) to confirm current line numbers (the method is around lines 164-171 in the version captured during spec-writing).

Replace:

```csharp
    private static Color CategoryColor(string category) => category switch
    {
        "Minutes" => Color.Primary,
        "Covenant" => Color.Info,
        "Announcement" => Color.Warning,
        "FinancialStatement" => Color.Success,
        _ => Color.Default
    };
```

with:

```csharp
    private static string CategoryClass(string category) => category switch
    {
        "Minutes" => "category-primary",
        "Covenant" => "category-teal",
        "Announcement" => "category-amber",
        "FinancialStatement" => "category-green",
        _ => "category-neutral"
    };
```

Leave `CategoryLabel` and `CategoryIcon` untouched. Do not remove the `Color` type usage elsewhere in the file (`MudProgressCircular`'s `Color="Color.Primary"`, the delete button's `Color="Color.Error"`) — those stay as-is.

- [ ] **Step 3: Replace the empty-state markup**

Replace the current empty-state block:

```razor
        @if (FilteredDocuments.Count == 0)
        {
            <div class="empty-state compact-empty">
                <MudIcon Icon="@(string.IsNullOrWhiteSpace(_categoryFilter) ? Icons.Material.Outlined.FolderOpen : CategoryIcon(_categoryFilter))" Size="Size.Large" Color="Color.Primary" />
                <MudText Typo="Typo.subtitle1">@EmptyStateTitle</MudText>
                <MudText Typo="Typo.body2" Color="Color.Secondary">@EmptyStateHint</MudText>
            </div>
        }
```

with:

```razor
        @if (FilteredDocuments.Count == 0)
        {
            <div class="empty-state compact-empty">
                <span class="category-badge category-badge-lg category-primary">
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
        }
```

Note this drops the filtered-category icon switch that used to show `CategoryIcon(_categoryFilter)` when a filter was active — the badge is now always the generic Documentos icon (`Description`), since it's a fixed hero badge, not a per-filter icon. This matches the mockup: the badge communicates "documents" as a concept, while the legend (only shown filter-less) communicates the categories.

- [ ] **Step 4: Build and fix any compile errors**

Run: `dotnet build "D:\repos\Condotify\Condotify\Condotify.csproj"`
Expected: `Build succeeded.` — 0 errors, and specifically no leftover reference to `CategoryColor` (it must be fully replaced, not left dangling unused).

- [ ] **Step 5: Manual verification**

Run the portal, open a license's Documentos module with zero documents published:
- Confirm a blue circular badge with the document icon appears, above the title/subtitle, above a row of 6 small colored category chips (Ata=blue, Regimento=gray, Convenção=teal, Comunicado=amber, Prestação de Contas=green, Outro=gray).
- Pick a category in the "Filtrar por categoria" dropdown that has zero documents — confirm the legend row disappears (only badge + title + hint remain).

- [ ] **Step 6: Commit**

```bash
git add Condotify/Components/LicenseModules/DocumentsModule.razor Condotify/wwwroot/css/portal.css
git commit -m "feat(portal): add category color system and redesign documentos empty state"
```

---

### Task 3: Documentos — card list replaces table

**Files:**
- Modify: `Condotify/Components/LicenseModules/DocumentsModule.razor`
- Modify: `Condotify/wwwroot/css/portal.css` (add near the classes added in Task 2)

**Interfaces:**
- Consumes: `CategoryClass(string) : string`, `CategoryIcon(string) : string`, `CategoryLabel(string) : string` (all from Task 2 — Task 3 must run after Task 2).
- Produces: nothing consumed elsewhere — this is the last task in the plan.

- [ ] **Step 1: Add the document row CSS to `portal.css`**

Add after the block added in Task 2 Step 1:

```css
.document-list { display: flex; flex-direction: column; gap: 8px; margin-top: 14px; }
.document-row { display: flex; align-items: flex-start; gap: 14px; padding: 14px; border: 1px solid #e7eaf0; border-radius: 9px; transition: border-color .15s ease, box-shadow .15s ease; }
.document-row:hover { border-color: #cfd7e5; box-shadow: 0 6px 18px rgba(32,37,50,.06); }
.document-row-body { flex: 1; min-width: 0; }
.document-row-top { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.document-row-title { font-size: 13.5px; font-weight: 700; color: var(--ds-ink); }
.document-row-desc { font-size: 12.5px; color: var(--ds-muted); margin-top: 3px; }
.document-row-meta { font-size: 11.5px; color: var(--ds-subtle); margin-top: 6px; }
.document-row-actions { display: flex; align-items: center; gap: 2px; flex: none; }
```

- [ ] **Step 2: Replace the `MudTable` with the card list**

Read `Condotify/Components/LicenseModules/DocumentsModule.razor` first to confirm the current `MudTable` block (lines 37-61 in the version captured during spec-writing, right after the empty-state `@if` block from Task 2).

Replace:

```razor
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
                    <MudTd DataLabel="Título">
                        <MudText Typo="Typo.body1">@context.Title</MudText>
                        @if (!string.IsNullOrWhiteSpace(context.Description))
                        {
                            <MudText Typo="Typo.body2" Color="Color.Secondary">@context.Description</MudText>
                        }
                    </MudTd>
                    <MudTd DataLabel="Categoria"><MudChip T="string" Size="Size.Small" Color="@CategoryColor(context.Category)" Variant="Variant.Outlined" Icon="@CategoryIcon(context.Category)">@CategoryLabel(context.Category)</MudChip></MudTd>
                    <MudTd DataLabel="Publicado em">@context.PublishedAt.ToLocalTime().ToString("dd/MM/yyyy")</MudTd>
                    <MudTd DataLabel="Enviado por">@context.UploadedByName</MudTd>
                    <MudTd>
                        <MudTooltip Text="Abrir"><MudIconButton Icon="@Icons.Material.Outlined.OpenInNew" Size="Size.Small" OnClick="() => OpenFileAsync(context.Id)" /></MudTooltip>
                        <MudTooltip Text="Excluir"><MudIconButton Icon="@Icons.Material.Outlined.Delete" Size="Size.Small" Color="Color.Error" Disabled="!CanManage" OnClick="() => DeleteAsync(context.Id)" /></MudTooltip>
                    </MudTd>
                </RowTemplate>
            </MudTable>
        }
```

with:

```razor
        else
        {
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
        }
```

- [ ] **Step 3: Build and fix any compile errors**

Run: `dotnet build "D:\repos\Condotify\Condotify\Condotify.csproj"`
Expected: `Build succeeded.` — 0 errors. Confirm no remaining reference to `MudTable`/`MudTh`/`MudTd`/`MudChip` in this file (they should all be gone from `DocumentsModule.razor` now that the table is fully replaced).

- [ ] **Step 4: Manual verification**

Run the portal, open a license's Documentos module, and either use existing seeded documents or publish 5 test documents (one per category: Ata, Regimento Interno, Convenção, Comunicado, Prestação de Contas) via the existing "Novo documento" dialog. Confirm:
- Each row shows a colored icon badge on the left matching its category (blue/gray/teal/amber/green), the title, a small category chip next to the title in the same color, the description (if any) on its own line, and "Publicado em DD/MM/YYYY · Nome" below.
- Hovering a row lifts it slightly (border + shadow change).
- "Abrir" opens the document (existing behavior, unchanged). "Excluir" (if `CanManage`) deletes it and the row disappears (existing behavior, unchanged).
- Filtering by category shows only matching cards.

- [ ] **Step 5: Commit**

```bash
git add Condotify/Components/LicenseModules/DocumentsModule.razor Condotify/wwwroot/css/portal.css
git commit -m "feat(portal): replace documentos table with category-colored card list"
```
