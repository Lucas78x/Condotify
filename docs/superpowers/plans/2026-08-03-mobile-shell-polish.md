# Polish do "shell" do app mobile Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the unstyled white "Carregando..." boot screen, add a fade transition between navigations (including login→home), and refine the shared components used by all 15 authenticated pages (`PageHeader`, `PageState`'s skeleton, metric cards, content panels, action tiles) to match the minimalist "soft corners, breathing room, light shadow" language established by the login redesign.

**Architecture:** All four changes are additive CSS (plus two tiny markup edits) in the existing `Condotify.Mobile` project — no new components, no new dependencies. The boot screen is plain HTML/CSS in `wwwroot/index.html` (replaced the instant Blazor mounts, so it never touches Razor). The page transition is a single wrapper `<div>` with a Blazor `@key` tied to the current URL, placed inside `MainLayout.razor` around `@Body` (not around the whole layout, so the app bar/rail/bottom nav never remount — only the routed page content re-animates). The shared component refinement only touches CSS class definitions in `app.css`; no Razor markup changes to `PageHeader.razor`, `PageState.razor`, or `Home.razor`.

**Tech Stack:** .NET MAUI Blazor Hybrid, MudBlazor 9.7.0, plain CSS (no JS interop needed for any of this).

**Reference spec:** `docs/superpowers/specs/2026-08-03-mobile-shell-polish-design.md`

## Global Constraints

- Only `Condotify.Mobile` is touched. Do not modify `Condotify.UI/CondotifyTheme.cs` (shared with the web app) or any page-specific CSS (`.directory-*`, `.person-*`, `.detail-grid`, `.section-tabs`, etc.).
- No new Razor components, no JS interop. Animations are CSS-only, each gated behind `@media (prefers-reduced-motion: no-preference)`, matching the existing pattern (`login-enter`, the old `skeleton-pulse`).
- Boot screen background: `linear-gradient(160deg, #3156d3, #1c2f7a)` — identical to the login hero and the native splash's base color (`#3156D3`), so there is no visible color jump between OS splash → boot placeholder → first Blazor frame.
- Corner radius for the refined cards/panels/tiles/skeletons: `14px` (up from the old `7px`), consistently, so the skeleton shape matches the real content it's replacing.
- No automated UI tests exist for this project (same as the login work) — verification is `dotnet build` for both `net9.0-android` and `net9.0-windows10.0.19041.0`, plus an actual install + screenshot on the connected Android device, compared against the approved mockup.

---

## File Structure

- Modify `Condotify.Mobile/wwwroot/index.html` — replace the unstyled `Carregando...` placeholder with a branded boot screen.
- Modify `Condotify.Mobile/Components/Layout/MainLayout.razor` — wrap `@Body` in a keyed transition div.
- Modify `Condotify.Mobile/wwwroot/css/app.css` — add boot-screen styles, page-transition animation, refined shared card/panel/tile styles, and the skeleton shimmer animation (replacing the old pulse).

---

### Task 1: Boot loading screen

**Files:**
- Modify: `Condotify.Mobile/wwwroot/index.html`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:** None consumed or produced — fully self-contained (plain HTML/CSS, replaced entirely once Blazor's root component mounts into `#app`).

- [ ] **Step 1: Replace the boot placeholder markup**

Edit `Condotify.Mobile/wwwroot/index.html`. Find:

```html
    <div class="status-bar-safe-area"></div>
    <div id="app">Carregando...</div>
```

Replace with:

```html
    <div class="status-bar-safe-area"></div>
    <div id="app">
        <div class="boot-screen">
            <div class="boot-mark" aria-hidden="true">C</div>
            <div class="boot-spinner" role="status" aria-label="Carregando"></div>
        </div>
    </div>
```

- [ ] **Step 2: Add the boot screen CSS**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
button, a, input, select { -webkit-tap-highlight-color: transparent; }

.public-shell { min-height: 100vh; background: #f6f8fb; }
```

Replace with:

```css
button, a, input, select { -webkit-tap-highlight-color: transparent; }

.boot-screen { position: fixed; inset: 0; z-index: 9999; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 22px; background: linear-gradient(160deg, #3156d3, #1c2f7a); }
.boot-mark { width: 56px; height: 56px; display: grid; place-items: center; border: 1px solid rgba(255, 255, 255, .32); border-radius: 16px; background: rgba(255, 255, 255, .14); color: #ffffff; font-size: 1.4rem; font-weight: 800; }
.boot-spinner { width: 26px; height: 26px; border: 2.5px solid rgba(255, 255, 255, .28); border-top-color: #ffffff; border-radius: 50%; }
@media (prefers-reduced-motion: no-preference) {
    .boot-spinner { animation: boot-spin 800ms linear infinite; }
    @keyframes boot-spin {
        to { transform: rotate(360deg); }
    }
}

.public-shell { min-height: 100vh; background: #f6f8fb; }
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors. (index.html/CSS changes don't affect compilation, but this confirms nothing else broke.)

- [ ] **Step 4: Commit**

```bash
git add Condotify.Mobile/wwwroot/index.html Condotify.Mobile/wwwroot/css/app.css
git commit -m "feat: replace unstyled boot placeholder with a branded loading screen"
```

---

### Task 2: Page transition on navigation

**Files:**
- Modify: `Condotify.Mobile/Components/Layout/MainLayout.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:** None — self-contained. `MainLayout.razor` already injects `NavigationManager Navigation` (used elsewhere in the file for `OpenLicenses`/`OpenNotifications`/etc.), so no new dependency is added.

- [ ] **Step 1: Wrap `@Body` in a keyed transition div**

Edit `Condotify.Mobile/Components/Layout/MainLayout.razor`. Find:

```razor
        <MudMainContent Class="app-content">
            <div class="page-frame">@Body</div>
        </MudMainContent>
```

Replace with:

```razor
        <MudMainContent Class="app-content">
            <div class="page-frame">
                <div class="page-transition" @key="@Navigation.Uri">
                    @Body
                </div>
            </div>
        </MudMainContent>
```

This only re-keys the inner content — `MudAppBar`, `MudDrawer`, and the bottom nav stay outside the keyed div and are never recreated on navigation, so only the routed page fades, not the whole shell. (The login→home transition is handled separately: it's a natural mount when `Session.IsAuthenticated` flips this component's own top-level `@if`/`else` branch, so the newly-created `.page-transition` div picks up the same CSS animation automatically — no extra code needed for that case.)

- [ ] **Step 2: Add the page-transition CSS**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.mobile-bottom-nav { display: none; }
```

Replace with:

```css
.mobile-bottom-nav { display: none; }
@media (prefers-reduced-motion: no-preference) {
    .page-transition { animation: page-fade-in 220ms ease-out both; }
    @keyframes page-fade-in {
        from { opacity: 0; transform: translateY(6px); }
        to { opacity: 1; transform: translateY(0); }
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add Condotify.Mobile/Components/Layout/MainLayout.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "feat: fade page content on navigation, including login to home"
```

---

### Task 3: Refine shared cards, panels, and tiles

**Files:**
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:** None — pure CSS refinement of classes already consumed by `PageHeader.razor` (`.page-header`), `Home.razor` and other pages (`.metric-grid`/`.metric`, `.content-panel`/`.panel-heading`, `.action-grid`/`.action-tile`). No Razor markup changes anywhere.

- [ ] **Step 1: Refine `.page-header`**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.page-header { display: flex; align-items: center; justify-content: space-between; gap: 18px; margin-bottom: 24px; }
.page-header > div:first-child { min-width: 0; }
.page-header h1 { overflow-wrap: anywhere; font-size: 1.65rem; }
```

Replace with:

```css
.page-header { display: flex; align-items: center; justify-content: space-between; gap: 18px; margin-bottom: 28px; }
.page-header > div:first-child { min-width: 0; }
.page-header h1 { overflow-wrap: anywhere; font-size: 1.65rem; letter-spacing: -.3px; }
```

- [ ] **Step 2: Refine `.metric-grid`/`.metric`**

Find:

```css
.metric-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); border: 1px solid var(--mud-palette-lines-default); border-radius: 7px; overflow: hidden; background: var(--mud-palette-surface); margin-bottom: 20px; }
.metric { position: relative; min-width: 0; min-height: 88px; display: grid; align-content: center; gap: 3px; padding: 17px 20px; border-right: 1px solid var(--mud-palette-lines-default); }
.metric:last-child { border-right: 0; }
```

Replace with:

```css
.metric-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; margin-bottom: 22px; }
.metric { position: relative; min-width: 0; min-height: 88px; display: grid; align-content: center; gap: 3px; padding: 17px 20px; border-radius: 14px; background: #f6f8fb; }
```

(The old version was one bordered strip with internal dividers; the new version is individual rounded cards with even spacing between them, matching the mockup.)

- [ ] **Step 3: Refine `.content-panel`**

Find:

```css
.content-panel { background: var(--mud-palette-surface); border: 1px solid var(--mud-palette-lines-default); border-radius: 7px; overflow: hidden; }
```

Replace with:

```css
.content-panel { background: var(--mud-palette-surface); border: 1px solid #eef1f5; border-radius: 14px; overflow: hidden; box-shadow: 0 4px 14px rgba(31, 48, 78, .05); }
```

- [ ] **Step 4: Refine `.action-tile`**

Find:

```css
.action-tile { min-width: 0; min-height: 88px; display: flex; flex-direction: column; align-items: flex-start; justify-content: center; gap: 9px; padding: 13px 14px; color: inherit; text-decoration: none; background: var(--mud-palette-surface); border: 1px solid var(--mud-palette-lines-default); border-radius: 7px; transition: border-color 140ms ease, transform 140ms ease, box-shadow 140ms ease; }
```

Replace with:

```css
.action-tile { min-width: 0; min-height: 88px; display: flex; flex-direction: column; align-items: flex-start; justify-content: center; gap: 9px; padding: 13px 14px; color: inherit; text-decoration: none; background: var(--mud-palette-surface); border: 1px solid #eef1f5; border-radius: 14px; box-shadow: 0 4px 14px rgba(31, 48, 78, .05); transition: border-color 140ms ease, transform 140ms ease, box-shadow 140ms ease; }
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 6: Commit**

```bash
git add Condotify.Mobile/wwwroot/css/app.css
git commit -m "refactor: refine shared card, panel, and tile styles to match the login redesign"
```

---

### Task 4: Skeleton shimmer

**Files:**
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:** None — pure CSS. No changes to `PageState.razor`'s markup (it already renders `.skeleton-metrics`, `.skeleton-panel`, `.skeleton-heading`, `.skeleton-row` exactly as needed).

- [ ] **Step 1: Replace the skeleton shapes with shimmer-ready styles**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.page-skeleton { display: grid; gap: 18px; }
.skeleton-metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); overflow: hidden; border: 1px solid var(--mud-palette-lines-default); border-radius: 7px; background: var(--mud-palette-surface); }
.skeleton-metrics span { min-height: 86px; border-right: 1px solid var(--mud-palette-lines-default); background: #f0f3f7; }
.skeleton-metrics span:last-child { border-right: 0; }
.skeleton-panel { min-height: 268px; display: grid; align-content: start; gap: 0; overflow: hidden; border: 1px solid var(--mud-palette-lines-default); border-radius: 7px; background: var(--mud-palette-surface); }
.skeleton-heading, .skeleton-row { display: block; background: #edf1f6; }
.skeleton-heading { width: 180px; height: 18px; margin: 20px; border-radius: 4px; }
.skeleton-row { height: 68px; border-top: 1px solid var(--mud-palette-lines-default); border-radius: 0; }
```

Replace with:

```css
.page-skeleton { display: grid; gap: 18px; }
.skeleton-metrics { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 10px; }
.skeleton-metrics span { min-height: 86px; border-radius: 14px; }
.skeleton-panel { min-height: 268px; display: grid; align-content: start; gap: 10px; padding: 20px; border-radius: 14px; background: #ffffff; border: 1px solid #eef1f5; overflow: hidden; }
.skeleton-heading, .skeleton-row { display: block; border-radius: 8px; }
.skeleton-heading { width: 180px; height: 18px; }
.skeleton-row { height: 56px; }
.skeleton-metrics span, .skeleton-heading, .skeleton-row { background: linear-gradient(100deg, #eef1f5 30%, #f8f9fb 50%, #eef1f5 70%); background-size: 200% 100%; }
@media (prefers-reduced-motion: no-preference) {
    .skeleton-metrics span, .skeleton-heading, .skeleton-row { animation: skeleton-shimmer 1400ms ease-in-out infinite; }
    @keyframes skeleton-shimmer {
        to { background-position: -200% 0; }
    }
}
```

- [ ] **Step 2: Remove the old pulse animation from the shared reduced-motion block**

The old skeleton animation lived in a different, pre-existing `@media (prefers-reduced-motion: no-preference)` block (shared with the login card's entrance animation). Find:

```css
@media (prefers-reduced-motion: no-preference) {
    .skeleton-metrics span, .skeleton-heading, .skeleton-row { animation: skeleton-pulse 1100ms ease-in-out infinite alternate; }
    .login-card { animation: login-enter 240ms ease-out both; }
    @keyframes login-enter {
        from { opacity: 0; transform: translateY(8px); }
        to { opacity: 1; transform: translateY(0); }
    }
    @keyframes skeleton-pulse {
        from { opacity: .52; }
        to { opacity: 1; }
    }
}
```

Replace with:

```css
@media (prefers-reduced-motion: no-preference) {
    .login-card { animation: login-enter 240ms ease-out both; }
    @keyframes login-enter {
        from { opacity: 0; transform: translateY(8px); }
        to { opacity: 1; transform: translateY(0); }
    }
}
```

(The shimmer rule and its own keyframe now live next to the rest of the skeleton CSS from Step 1, so this block only needs to drop the now-unused pulse rule and keyframe — nothing else in it changes.)

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 4: Commit**

```bash
git add Condotify.Mobile/wwwroot/css/app.css
git commit -m "refactor: replace the pulsing loading skeleton with a shimmer animation"
```

---

### Task 5: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Build both targets**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android -p:CondotifyApiBaseUrl=http://172.30.2.163:5093`
Expected: both succeed, 0 errors.

- [ ] **Step 2: Install and screenshot on the connected Android device**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android -p:CondotifyApiBaseUrl=http://172.30.2.163:5093 -t:Run`
Then, using `adb` (path: `C:\Users\Lucas Noc\AppData\Local\Android\Sdk\platform-tools\adb.exe`):
- `adb shell screencap -p /sdcard/boot_check.png` immediately after a fresh launch (kill and relaunch the app via `adb shell am force-stop br.com.condotify.app` then `adb shell am start -n br.com.condotify.app/crc64105f34807b2bcc79.MainActivity` right before capturing, to catch the boot screen before it's replaced) — expect the blue boot screen with the "C" mark and spinner, not a white/gray blank screen.
- Log in, and screenshot immediately after tapping "Entrar" — expect a brief fade rather than an instant hard cut to the Início screen.
- Navigate to at least one other page (e.g. tap a bottom-nav/rail item) and confirm the app bar/rail do NOT flicker or remount — only the page content fades.
- Screenshot the Início screen fully loaded — compare metric cards, content panel, and page header spacing against the approved mockup (rounded 14px corners, soft shadow, no internal grid dividers).
- If reachable at that moment, screenshot the loading (skeleton) state of a page — confirm the shimmer sweep instead of the old fade-in-out pulse.
- Pull each screenshot with `adb pull /sdcard/<name>.png <local temp path>`, view it, then delete both the on-device copy (`adb shell rm /sdcard/<name>.png`) and the local temp copy once reviewed — don't leave screenshot files in the repo.

- [ ] **Step 3: Fix anything the screenshots reveal**

If a screenshot doesn't match the mockup (wrong corner radius, animation not firing, shell remounting on navigation), fix the specific CSS/markup, rebuild, reinstall, and re-screenshot before moving on — don't leave a known visual mismatch uncommitted as "done."
