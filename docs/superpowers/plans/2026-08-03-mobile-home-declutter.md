# Início sem redundância de navegação Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the "Acesso rápido" grid from the Home screen (it duplicates the bottom nav and the "Mais" page almost entirely), simplify the page header to stop repeating the condo name twice on screen, and add a floating "+" action button — matching the mockup the user approved.

**Architecture:** Pure Razor markup + CSS changes in the existing `Home.razor`/`More.razor`/`app.css` files. No new components, no new routes, no C# logic changes beyond renaming/adding two computed properties. Removing the action-grid markup makes a dozen scattered `.action-grid`/`.action-tile`/`.dashboard-section-title` CSS rules dead (confirmed via repo-wide grep: those classes are used nowhere else) — this plan removes them alongside the markup that used them, rather than leaving orphaned CSS.

**Tech Stack:** .NET MAUI Blazor Hybrid, MudBlazor 9.7.0, plain CSS.

**Reference spec:** `docs/superpowers/specs/2026-08-03-mobile-home-declutter-design.md`

## Global Constraints

- Only `Condotify.Mobile/Components/Pages/Home.razor`, `Condotify.Mobile/Components/Pages/More.razor`, and `Condotify.Mobile/wwwroot/css/app.css` are touched.
- "Reservas" (`/bookings`) is the only Home quick-access item with no other path (staff side) — it must be added to `More.razor`'s staff branch so nothing becomes unreachable. Every other removed quick-access item already has a path via the bottom nav or "Mais" — verified in the design spec's redundancy table; no other page needs a new entry.
- The floating action button ("+") goes to `/visitors` for both principals — a "create" shortcut alongside the existing "browse" entry points (bottom nav for residents, "Mais" for staff), not a replacement for them.
- Page header: title becomes the condo/context name (previously the subtitle), subtitle becomes "Olá, {name}" (previously the title) — eliminates showing the condo name twice on screen (it already appears in the app bar above).
- No automated UI tests exist for this project — verification is `dotnet build` for both `net9.0-android` and `net9.0-windows10.0.19041.0`, plus an actual install + screenshot on the connected Android device for both principals (staff and resident), compared against the approved mockup.

---

## File Structure

- Modify `Condotify.Mobile/Components/Pages/Home.razor` — remove both "Acesso rápido" blocks, swap header title/subtitle, add the FAB.
- Modify `Condotify.Mobile/Components/Pages/More.razor` — add a "Reservas" row to the staff branch.
- Modify `Condotify.Mobile/wwwroot/css/app.css` — remove dead `.action-grid`/`.action-tile`/`.dashboard-section-title` rules (and their responsive overrides), add `.home-fab`.

---

### Task 1: Declutter Home, backfill Reservas in Mais, clean up CSS

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Home.razor`
- Modify: `Condotify.Mobile/Components/Pages/More.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:** None — no new components, no new routes. `MudIcon`/`MudIconButton`/`PageHeader`/`PageState` usage patterns are unchanged from the rest of the codebase.

- [ ] **Step 1: Swap the page header's title and subtitle**

Edit `Condotify.Mobile/Components/Pages/Home.razor`. Find:

```razor
<PageTitle>Inicio | Condotify</PageTitle>
<PageHeader Title="@Greeting" Subtitle="@Subtitle">
    <Actions>
        <MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" aria-label="Atualizar" />
    </Actions>
</PageHeader>
```

Replace with:

```razor
<PageTitle>Inicio | Condotify</PageTitle>
<PageHeader Title="@ContextTitle" Subtitle="@Greeting">
    <Actions>
        <MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" aria-label="Atualizar" />
    </Actions>
</PageHeader>
```

- [ ] **Step 2: Remove the resident "Acesso rápido" block**

Find:

```razor
            <div class="metric warning"><span class="metric-value">@(_notifications?.Unread ?? 0)</span><span class="metric-label">Novidades</span><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.NotificationsNone" /></span></div>
        </div>
        <div class="dashboard-section-title"><MudText Typo="Typo.h5">Acesso rápido</MudText></div>
        <div class="action-grid staff-actions">
            <a class="action-tile" href="/visitors"><MudIcon Icon="@Icons.Material.Outlined.PersonAdd" Color="Color.Primary" /><span>Visitantes</span></a>
            <a class="action-tile" href="/bookings"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" Color="Color.Tertiary" /><span>Reservas</span></a>
            <a class="action-tile" href="/deliveries"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" Color="Color.Secondary" /><span>Encomendas</span></a>
            <a class="action-tile" href="/cameras"><MudIcon Icon="@Icons.Material.Outlined.Videocam" Color="Color.Info" /><span>Câmeras</span></a>
            <a class="action-tile" href="/notifications"><MudIcon Icon="@Icons.Material.Outlined.NotificationsNone" Color="Color.Warning" /><span>Notificações</span></a>
            <a class="action-tile" href="/profile"><MudIcon Icon="@Icons.Material.Outlined.PersonOutline" Color="Color.Info" /><span>Meu cadastro</span></a>
        </div>
        <section class="content-panel">
            <div class="panel-heading"><MudText Typo="Typo.h5">Minhas unidades</MudText></div>
```

Replace with:

```razor
            <div class="metric warning"><span class="metric-value">@(_notifications?.Unread ?? 0)</span><span class="metric-label">Novidades</span><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.NotificationsNone" /></span></div>
        </div>
        <section class="content-panel">
            <div class="panel-heading"><MudText Typo="Typo.h5">Minhas unidades</MudText></div>
```

- [ ] **Step 3: Remove the staff "Acesso rápido" block**

Find:

```razor
            <div class="metric warning"><span class="metric-value">@(_dashboard?.Alerts.Count ?? 0)</span><span class="metric-label">Alertas recentes</span><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" /></span></div>
        </div>
        <div class="dashboard-section-title"><MudText Typo="Typo.h5">Acesso rápido</MudText></div>
        <div class="action-grid">
            <a class="action-tile" href="/people"><MudIcon Icon="@Icons.Material.Outlined.Groups" Color="Color.Primary" /><span>Pessoas</span></a>
            <a class="action-tile" href="/visitors"><MudIcon Icon="@Icons.Material.Outlined.PersonAddAlt1" Color="Color.Info" /><span>Visitantes</span></a>
            <a class="action-tile" href="/concierge"><MudIcon Icon="@Icons.Material.Outlined.SensorDoor" Color="Color.Primary" /><span>Portaria</span></a>
            <a class="action-tile" href="/devices"><MudIcon Icon="@Icons.Material.Outlined.DoorFront" Color="Color.Tertiary" /><span>Acionamentos</span></a>
            <a class="action-tile" href="/cameras"><MudIcon Icon="@Icons.Material.Outlined.Videocam" Color="Color.Info" /><span>Câmeras</span></a>
            <a class="action-tile" href="/alerts"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" Color="Color.Warning" /><span>Alertas</span></a>
            <a class="action-tile" href="/deliveries"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" Color="Color.Secondary" /><span>Encomendas</span></a>
            <a class="action-tile" href="/bookings"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" Color="Color.Tertiary" /><span>Reservas</span></a>
        </div>
        <section class="content-panel">
            <div class="panel-heading"><MudText Typo="Typo.h5">Atividade recente</MudText></div>
```

Replace with:

```razor
            <div class="metric warning"><span class="metric-value">@(_dashboard?.Alerts.Count ?? 0)</span><span class="metric-label">Alertas recentes</span><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" /></span></div>
        </div>
        <section class="content-panel">
            <div class="panel-heading"><MudText Typo="Typo.h5">Atividade recente</MudText></div>
```

- [ ] **Step 4: Add the floating action button after `PageState`**

Find:

```razor
    }
</PageState>

@code {
```

Replace with:

```razor
    }
</PageState>
<a class="home-fab" href="/visitors" aria-label="Registrar visitante">
    <MudIcon Icon="@Icons.Material.Outlined.Add" />
</a>

@code {
```

- [ ] **Step 5: Replace the `Subtitle` property with `ContextTitle`**

Find:

```csharp
    private string Greeting => $"Olá, {FirstName}";
    private string FirstName => (Session.Current?.Name ?? "Usuário").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Usuário";
    private string Subtitle => Session.Current?.Principal == MobilePrincipalKind.Resident ? Session.Current.LicenseName : AppState.SelectedLicense?.Nome ?? "Visão consolidada da operação";
```

Replace with:

```csharp
    private string ContextTitle => Session.Current?.Principal == MobilePrincipalKind.Resident ? Session.Current.LicenseName : AppState.SelectedLicense?.Nome ?? "Central operacional";
    private string Greeting => $"Olá, {FirstName}";
    private string FirstName => (Session.Current?.Name ?? "Usuário").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Usuário";
```

- [ ] **Step 6: Add "Reservas" to the staff branch of Mais**

Edit `Condotify.Mobile/Components/Pages/More.razor`. Find:

```razor
        <a class="list-row" href="/alerts"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" Color="Color.Warning" /><div class="list-main"><div class="list-title">Alertas operacionais</div><div class="list-meta">Ocorrências que exigem atenção</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
        <a class="list-row" href="/licenses"><MudIcon Icon="@Icons.Material.Outlined.Apartment" Color="Color.Secondary" /><div class="list-main"><div class="list-title">Trocar condomínio</div><div class="list-meta">Selecionar ambiente de trabalho</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
```

Replace with:

```razor
        <a class="list-row" href="/alerts"><MudIcon Icon="@Icons.Material.Outlined.WarningAmber" Color="Color.Warning" /><div class="list-main"><div class="list-title">Alertas operacionais</div><div class="list-meta">Ocorrências que exigem atenção</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
        <a class="list-row" href="/bookings"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" Color="Color.Tertiary" /><div class="list-main"><div class="list-title">Reservas</div><div class="list-meta">Aprovações e agenda de áreas comuns</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
        <a class="list-row" href="/licenses"><MudIcon Icon="@Icons.Material.Outlined.Apartment" Color="Color.Secondary" /><div class="list-main"><div class="list-title">Trocar condomínio</div><div class="list-meta">Selecionar ambiente de trabalho</div></div><MudIcon Icon="@Icons.Material.Outlined.ChevronRight" /></a>
```

- [ ] **Step 7: Remove the now-dead `.action-grid.staff-actions` rule near `.directory-toolbar`**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.directory-toolbar { margin-bottom: 14px; }
.action-grid.staff-actions { grid-template-columns: repeat(4, minmax(0, 1fr)); }
.directory-toolbar .mud-input-control { margin: 0; }
```

Replace with:

```css
.directory-toolbar { margin-bottom: 14px; }
.directory-toolbar .mud-input-control { margin: 0; }
```

- [ ] **Step 8: Remove the base `.action-grid`/`.action-tile`/`.dashboard-section-title` rules, add `.home-fab`**

Find:

```css
.status-dot.warning { background: #a96300; }
.action-grid { display: grid; grid-template-columns: repeat(6, minmax(0, 1fr)); gap: 10px; margin-bottom: 20px; }
.action-tile { min-width: 0; min-height: 88px; display: flex; flex-direction: column; align-items: flex-start; justify-content: center; gap: 9px; padding: 13px 14px; color: inherit; text-decoration: none; background: var(--mud-palette-surface); border: 1px solid #eef1f5; border-radius: 14px; box-shadow: 0 4px 14px rgba(31, 48, 78, .05); transition: border-color 140ms ease, transform 140ms ease, box-shadow 140ms ease; }
.action-tile:hover { border-color: color-mix(in srgb, var(--mud-palette-primary) 55%, var(--mud-palette-lines-default)); transform: translateY(-2px); box-shadow: 0 8px 18px rgba(31, 48, 78, .07); }
.action-tile .mud-icon-root { width: 32px; height: 32px; padding: 6px; border-radius: 6px; background: color-mix(in srgb, currentColor 9%, transparent); }
.action-tile span { max-width: 100%; font-size: .79rem; font-weight: 700; overflow-wrap: anywhere; }
.dashboard-section-title { margin: 2px 0 10px; }
.dashboard-section-title .mud-typography-h5 { font-size: .92rem; }
.settings-panel { max-width: 820px; }
```

Replace with:

```css
.status-dot.warning { background: #a96300; }
.home-fab { position: fixed; z-index: 1150; right: max(20px, env(safe-area-inset-right)); bottom: max(20px, env(safe-area-inset-bottom)); width: 52px; height: 52px; border-radius: 16px; display: grid; place-items: center; background: var(--mud-palette-primary); color: #ffffff; box-shadow: 0 10px 20px rgba(49, 86, 211, .35); text-decoration: none; }
.home-fab .mud-icon-root { width: 24px; height: 24px; font-size: 24px; }
.settings-panel { max-width: 820px; }
```

- [ ] **Step 9: Remove the now-empty 901-1200px `.action-grid` media query**

Find:

```css
.terms-copy { display: block; max-height: 96px; overflow: auto; padding: 10px 12px; border-left: 3px solid var(--mud-palette-primary); background: var(--mud-palette-background-gray); }

@media (max-width: 1200px) and (min-width: 901px) {
    .action-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
}

@media (max-width: 900px) {
```

Replace with:

```css
.terms-copy { display: block; max-height: 96px; overflow: auto; padding: 10px 12px; border-left: 3px solid var(--mud-palette-primary); background: var(--mud-palette-background-gray); }

@media (max-width: 900px) {
```

- [ ] **Step 10: Remove the dead `.action-grid`/`.action-tile` overrides inside the 900px block, add the FAB's raised bottom offset**

Find:

```css
    .bottom-nav-link.active { color: var(--mud-palette-primary); background: color-mix(in srgb, var(--mud-palette-primary) 9%, transparent); }
    .metric-grid { grid-template-columns: 1fr 1fr; }
    .skeleton-metrics { grid-template-columns: 1fr 1fr; }
    .action-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    .action-tile { min-height: 82px; padding: 12px; }
    .list-row { padding-inline: 12px; }
```

Replace with:

```css
    .bottom-nav-link.active { color: var(--mud-palette-primary); background: color-mix(in srgb, var(--mud-palette-primary) 9%, transparent); }
    .home-fab { bottom: calc(var(--mobile-nav-height) + 20px + env(safe-area-inset-bottom)); }
    .metric-grid { grid-template-columns: 1fr 1fr; }
    .skeleton-metrics { grid-template-columns: 1fr 1fr; }
    .list-row { padding-inline: 12px; }
```

(The FAB sits above the bottom nav bar only where that bar is actually visible — this breakpoint. At wider widths, where the side rail is used instead of a bottom bar, the base `.home-fab` rule from Step 8 already anchors it to the bottom of the viewport with no nav bar to clear.)

- [ ] **Step 11: Remove the dead `.action-tile`/`.action-grid.staff-actions` overrides in the two narrowest breakpoints**

Find:

```css
    .metric { min-height: 82px; padding: 14px; }
    .metric-icon { top: 14px; right: 14px; }
    .action-tile { min-height: 78px; padding: 10px; }
    .action-tile span { font-size: .72rem; }
    .action-grid.staff-actions { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}

@media (max-width: 560px) and (min-width: 391px) {
    .action-grid.staff-actions { grid-template-columns: repeat(2, minmax(0, 1fr)); }
}
```

Replace with:

```css
    .metric { min-height: 82px; padding: 14px; }
    .metric-icon { top: 14px; right: 14px; }
}
```

- [ ] **Step 12: Confirm no leftover references**

Run: `grep -rn "action-grid\|action-tile\|dashboard-section-title" Condotify.Mobile/wwwroot/css/app.css Condotify.Mobile/Components`
Expected: no output (zero matches) — confirms every reference to these three classes was removed from both the CSS and the markup that used to consume them.

- [ ] **Step 13: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 14: Commit**

```bash
git add Condotify.Mobile/Components/Pages/Home.razor Condotify.Mobile/Components/Pages/More.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "refactor: remove redundant quick-access grid from Home, add FAB, backfill Reservas in Mais"
```

---

### Task 2: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Build both targets**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android -p:CondotifyApiBaseUrl=http://172.30.2.163:5093`
Expected: both succeed, 0 errors.

- [ ] **Step 2: Install and screenshot on the connected Android device**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android -p:CondotifyApiBaseUrl=http://172.30.2.163:5093 -t:Run`
Using `adb` (path: `C:\Users\Lucas Noc\AppData\Local\Android\Sdk\platform-tools\adb.exe`), screenshot the staff Home screen (`adb shell screencap -p /sdcard/home_check.png` then `adb pull`) and confirm: no "Acesso rápido" section, header shows the condo name as the bold title and "Olá, {nome}" as the subtitle, a blue "+" floating button sits above the bottom nav bar. Pull the screenshot, view it, then delete both the on-device copy (`adb shell rm /sdcard/home_check.png`) and the local temp copy — don't leave screenshot files in the repo.

- [ ] **Step 3: Confirm "Mais" has Reservas (staff)**

Since this device likely blocks `adb shell input` (synthetic touch), this step may need to be done by the human directly: open "Mais" from the bottom nav and confirm a "Reservas" row now appears, linking to `/bookings`. If touch injection works in this environment, screenshot it directly instead.

- [ ] **Step 4: Fix anything the screenshots reveal**

If the FAB overlaps content awkwardly, the header looks wrong, or anything else doesn't match the mockup, fix the specific CSS/markup, rebuild, reinstall, and re-screenshot before moving on.
