# Visão Geral Overview Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 1-row block table on the license workspace's "Visão geral" tab with a reused block-card grid, and add a two-panel side column (license details + quick links) so the tab stops leaving ~45% of the page blank — using only data `LicenseFullViewModel` already exposes.

**Architecture:** Single-component change: `OverviewModule.razor` gains a new `LicenseId` parameter and its markup is restructured into a 2-column layout; its one caller (`LicenseWorkspace.razor`) passes the new parameter; `portal.css` gains a handful of new rules for the two new side panels (the block-card grid reuses `.hierarchy-card`/`.group-card-grid`, already defined). No API, DTO, or permission changes.

**Tech Stack:** ASP.NET Core Blazor Server, MudBlazor 9.7.0, plain custom CSS.

## Global Constraints

- No changes to any API/controller, `LicenseFullViewModel`, or `LicensePermission`.
- Reuse `.hierarchy-card` / `.hierarchy-card-icon` / `.hierarchy-card-main` / `.hierarchy-card-stats` / `.hierarchy-card-action` / `.group-card-grid` from `portal.css:390-404` verbatim for the block cards — do not redefine or fork these classes.
- New CSS classes are kebab-case; colors follow this file's existing local convention of raw hex values matching neighboring rules (not `--ds-*` tokens — `portal.css` and `design-system.css` use different conventions and this file follows its own).
- User-visible strings stay in Brazilian Portuguese.
- No new automated tests — presentation-only change; verification is `dotnet build` + manual browser checks.
- Full spec: `docs/superpowers/specs/2026-08-08-visao-geral-overview-redesign-design.md`.

---

### Task 1: Visão geral — card grid + license details/quick actions side column

**Files:**
- Modify: `Condotify/Components/LicenseModules/OverviewModule.razor`
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor:102` (the `default:` case that instantiates `OverviewModule`)
- Modify: `Condotify/wwwroot/css/portal.css` (add near the existing `.hierarchy-card*`/`.group-card-grid` rules at lines 390-404, and near `.module-grid` at line 222 for the new `.overview-grid`)

**Interfaces:**
- Produces: `OverviewModule` gains `[Parameter, EditorRequired] public Guid LicenseId { get; set; }` alongside the existing `[Parameter, EditorRequired] public LicenseFullViewModel License { get; set; }`. Nothing else in the codebase instantiates `OverviewModule` besides `LicenseWorkspace.razor`.
- Consumes: `LicenseFullViewModel.Blocks` (`List<BlockFrontViewModel>`, each with `Name`/`TotalUnits`/`TotalResidents`), `.Type`/`.City`/`.State`/`.CreatedAt` (all existing fields, unused until now).

- [ ] **Step 1: Read both files first**

Read `Condotify/Components/LicenseModules/OverviewModule.razor` (currently 27 lines) and `Condotify/Components/Pages/LicenseWorkspace.razor` around line 102 (the `default: <OverviewModule License="_license" /> break;` case) to confirm current exact content before editing — this branch has had prior redesign commits, so re-verify line numbers rather than trusting the numbers in this brief.

- [ ] **Step 2: Rewrite `OverviewModule.razor`**

Replace the entire file content with:

```razor
<div class="stats-grid">
    <StatTile Label="Blocos" Value="@License.TotalBlocks.ToString()" Icon="@Icons.Material.Outlined.Apartment" Tone="blue" />
    <StatTile Label="Unidades" Value="@License.TotalUnits.ToString()" Icon="@Icons.Material.Outlined.MeetingRoom" Tone="amber" />
    <StatTile Label="Moradores" Value="@License.TotalResidents.ToString()" Icon="@Icons.Material.Outlined.Groups" Tone="green" />
    <StatTile Label="Validade" Value="@License.ExpireDate.ToString("dd/MM/yyyy")" Icon="@Icons.Material.Outlined.EventAvailable" Tone="red" />
</div>

<div class="overview-grid">
    <MudPaper Class="content-panel" Elevation="0">
        <div class="panel-toolbar">
            <div><MudText Typo="Typo.h5" Class="panel-title">Estrutura do condomínio</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">Distribuição cadastrada por bloco</MudText></div>
        </div>
        @if (License.Blocks.Count == 0)
        {
            <div class="panel-body"><MudAlert Severity="Severity.Info" Variant="Variant.Outlined">A estrutura ainda não foi cadastrada. Comece criando o primeiro bloco.</MudAlert></div>
        }
        else
        {
            <div class="panel-body">
                <div class="hierarchy-grid group-card-grid">
                    @foreach (var block in License.Blocks)
                    {
                        <div class="hierarchy-card group-card">
                            <span class="hierarchy-card-icon"><MudIcon Icon="@Icons.Material.Outlined.Apartment" /></span>
                            <span class="hierarchy-card-main"><strong>@block.Name</strong><small>Bloco</small></span>
                            <span class="hierarchy-card-stats"><span><strong>@block.TotalUnits</strong>unidade@(block.TotalUnits == 1 ? "" : "s")</span><span><strong>@block.TotalResidents</strong>pessoas</span></span>
                            <a href="/licencas/@LicenseId/estrutura" class="hierarchy-card-action"><span>Ver estrutura</span><MudIcon Icon="@Icons.Material.Outlined.ArrowForward" /></a>
                        </div>
                    }
                </div>
            </div>
        }
    </MudPaper>

    <div class="overview-side">
        <MudPaper Class="content-panel" Elevation="0">
            <div class="panel-toolbar"><MudText Typo="Typo.subtitle1" Class="panel-title">Detalhes da licença</MudText></div>
            <div class="panel-body">
                <div class="info-row"><span>Tipo</span><strong>@License.Type</strong></div>
                <div class="info-row"><span>Cidade / UF</span><strong>@License.City / @License.State</strong></div>
                <div class="info-row"><span>Criada em</span><strong>@License.CreatedAt.ToString("dd/MM/yyyy")</strong></div>
            </div>
        </MudPaper>

        <MudPaper Class="content-panel" Elevation="0">
            <div class="panel-toolbar"><MudText Typo="Typo.subtitle1" Class="panel-title">Ações rápidas</MudText></div>
            <div class="panel-body quick-actions">
                <a class="qa-item" href="/licencas/@LicenseId/estrutura"><MudIcon Icon="@Icons.Material.Outlined.AddHomeWork" />Novo bloco<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
                <a class="qa-item" href="/licencas/@LicenseId/estrutura"><MudIcon Icon="@Icons.Material.Outlined.Groups" />Ver moradores<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
                <a class="qa-item" href="/licencas/@LicenseId/credenciais"><MudIcon Icon="@Icons.Material.Outlined.Badge" />Configurar acessos<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
                <a class="qa-item" href="/licencas/@LicenseId/documentos"><MudIcon Icon="@Icons.Material.Outlined.Description" />Ver documentos<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
            </div>
        </MudPaper>
    </div>
</div>

@code {
    [Parameter, EditorRequired] public LicenseFullViewModel License { get; set; } = new();
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
}
```

Note `.overview-side` wraps the two side panels in a `display:flex; flex-direction:column; gap:14px` column (defined in Step 4) — this is a plain `<div>`, not a MudBlazor component.

- [ ] **Step 3: Update the caller in `LicenseWorkspace.razor`**

Replace:

```razor
            default:
                <OverviewModule License="_license" />
                break;
```

with:

```razor
            default:
                <OverviewModule License="_license" LicenseId="LicenseId" />
                break;
```

- [ ] **Step 4: Add the new CSS to `portal.css`**

Read `Condotify/wwwroot/css/portal.css` around lines 390-404 (the existing `.hierarchy-card*`/`.group-card-grid` block) and around line 667 (the `@media (max-width: 980px)` block, which already exists for other layout collapses) first, to place the new rules consistently with the file's existing structure.

Add near `.module-grid` (around line 222-223, same general area as other page-specific grid layouts):

```css
.overview-grid { display: grid; grid-template-columns: minmax(0, 1.65fr) minmax(280px, 1fr); gap: 16px; align-items: start; }
.overview-side { display: flex; flex-direction: column; gap: 14px; }
```

Add inside the existing `@media (max-width: 980px) { ... }` block (around line 667):

```css
    .overview-grid { grid-template-columns: 1fr; }
```

Add near the existing `.hierarchy-card*` rules (around line 404), a new block for the side-panel content:

```css
.info-row { display: flex; align-items: center; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eef1f5; font-size: 12.5px; }
.info-row:last-child { border-bottom: none; }
.info-row span { color: #697386; }
.info-row strong { color: #202532; font-weight: 600; }
.quick-actions { display: flex; flex-direction: column; gap: 2px; }
.qa-item { display: flex; align-items: center; gap: 11px; padding: 11px 4px; border-radius: 7px; font-size: 12.5px; font-weight: 600; color: #3f4a5c; text-decoration: none; }
.qa-item:hover { background: #f8fafc; }
.qa-item .mud-icon-root:first-child { color: #3156d3; font-size: 1.1rem; }
.qa-arrow { margin-left: auto; color: #8a94a5; font-size: 1rem; }
```

- [ ] **Step 5: Build and fix any compile errors**

Run: `dotnet build "Condotify/Condotify.csproj"` (run from repo root, or the appropriate worktree root if executing in one).
Expected: `Build succeeded.` — 0 errors. If `EditorRequired` on the new `LicenseId` parameter produces an analyzer warning because some other (nonexistent) caller doesn't pass it, double check no other file instantiates `<OverviewModule>` besides the one call site in `LicenseWorkspace.razor` (a repo-wide grep for `<OverviewModule` should return exactly one result, the one this task edited).

- [ ] **Step 6: Manual verification**

Run the portal, log in, open a license workspace, land on "Visão geral" (default tab):
- Confirm the 4 stat tiles render unchanged (Blocos/Unidades/Moradores/Validade).
- Confirm "Estrutura do condomínio" now shows block(s) as cards (icon, name, "Bloco" label, unit/people counts, "Ver estrutura →" link) instead of a table; clicking "Ver estrutura" navigates to the Estrutura tab.
- Confirm the right column shows "Detalhes da licença" (Tipo/Cidade-UF/Criada em, all populated) and "Ações rápidas" (4 links) below it.
- Confirm all 4 quick-action links navigate to the correct tabs (Novo bloco → Estrutura, Ver moradores → Estrutura, Configurar acessos → Credenciais, Ver documentos → Documentos).
- Resize the browser below ~980px width — confirm the 2-column layout collapses to 1 column (stats → estrutura cards → detalhes → ações rápidas, top to bottom).
- If reachable, check a license with zero blocks — confirm the original "estrutura ainda não foi cadastrada" alert still renders unchanged in that case.

- [ ] **Step 7: Commit**

```bash
git add Condotify/Components/LicenseModules/OverviewModule.razor Condotify/Components/Pages/LicenseWorkspace.razor Condotify/wwwroot/css/portal.css
git commit -m "feat(portal): redesign visão geral tab with block cards and license details/quick actions column"
```
