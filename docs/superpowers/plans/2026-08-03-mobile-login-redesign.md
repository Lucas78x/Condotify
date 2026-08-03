# Redesign da tela de login do app mobile + "Esqueci minha senha" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign `Condotify.Mobile`'s login screen from a compressed desktop two-column layout into a minimalist, mobile-first "hero + sheet" design, and add the missing "Esqueci minha senha" flow (self-service reset for residents, contact-support screen for staff).

**Architecture:** Everything lives in the existing single-page component `Condotify.Mobile/Components/Pages/Login.razor`, which already toggles between "credentials" and "MFA" via one internal state field. This plan generalizes that into a `LoginStep` enum with six states (`Credentials`, `Mfa`, `ForgotEmail`, `ForgotReset`, `ForgotDone`, `StaffSupport`) instead of introducing new routes. Two new HTTP methods are added to `Condotify.Mobile.Core/MobileSessionCoordinator.cs`, following the exact pattern already used by `LoginResidentAsync`/`VerifyStaffMfaAsync`, to call the resident password-recovery endpoints that already exist in `CondotifyAPI`.

**Tech Stack:** .NET 9, MAUI Blazor Hybrid, MudBlazor 9.7.0, xUnit.

**Reference spec:** `docs/superpowers/specs/2026-08-03-mobile-login-redesign-design.md`

## Global Constraints

- Only the mobile app is touched (`Condotify.Mobile`, `Condotify.Mobile.Core`, `Condotify.Mobile.Tests`). `Condotify/Views/Login/Login.cshtml` (web staff login) is out of scope.
- No changes to `CondotifyAPI` (backend) or `Condotify.ApiClient`. Password recovery is anonymous and goes through `MobileSessionCoordinator`'s existing `CondotifyAuth` HTTP client, same as login/MFA.
- No dark mode: `MainLayout.razor` already forces `IsDarkMode="false"`; do not add dark-mode CSS.
- Visual language: gradient hero (`#3156d3` → `#1c2f7a`) on the `Credentials` step only; every other step uses a compact nav bar (back arrow + title). Card is centered with `max-width: 400px` at every window size — no two-column desktop layout.
- The resident recovery "code" is actually an opaque ~43-character token (`RefreshTokenService.GenerateTokenPair`), not a short OTP. The `ForgotReset` step must use a plain pasteable text field, not per-digit boxes.
- Support contact values are placeholders until the user supplies real ones: `suporte@condotify.com.br` / `(11) 90000-0000` (WhatsApp link target `https://wa.me/5511900000000`). Keep them as named constants so they're easy to find and swap.
- Copy is Portuguese (pt-BR), matching the rest of the app.
- No UI test infrastructure exists in this repo for Blazor/MAUI components — verification for Razor/CSS tasks is `dotnet build` + a manual walkthrough, not automated UI tests. `Condotify.Mobile.Core` logic (the coordinator) does get real unit tests.

---

## File Structure

- Modify `Condotify.Mobile.Core/MobileSession.cs` — add `MobilePasswordResetResult` record.
- Modify `Condotify.Mobile.Core/MobileSessionCoordinator.cs` — add `ForgotPasswordAsync` and `ResetPasswordAsync`.
- Modify `Condotify.Mobile.Tests/MobileSessionCoordinatorTests.cs` — add 3 tests for the methods above.
- Rewrite `Condotify.Mobile/Components/Pages/Login.razor` — new visual design (Task 2), then extended in place with the forgot-password flow (Task 3) and the staff support screen (Task 4).
- Modify `Condotify.Mobile/wwwroot/css/app.css` — replace the `.login-*`/`.access-*` block (~lines 146-281) with the new hero/sheet/navbar styling, in three surgical edits that preserve the unrelated `.skeleton-*` rules that share a media query with the old login animations.

---

### Task 1: MobileSessionCoordinator — forgot/reset password methods

**Files:**
- Modify: `Condotify.Mobile.Core/MobileSession.cs`
- Modify: `Condotify.Mobile.Core/MobileSessionCoordinator.cs`
- Test: `Condotify.Mobile.Tests/MobileSessionCoordinatorTests.cs`

**Interfaces:**
- Produces (consumed by Task 3): `public sealed record MobilePasswordResetResult(bool Success, string Error)` in `Condotify.Mobile.Core`.
- Produces (consumed by Task 3): on `MobileSessionCoordinator`:
  - `Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)`
  - `Task<MobilePasswordResetResult> ResetPasswordAsync(string token, string newPassword, CancellationToken cancellationToken = default)`

- [ ] **Step 1: Write the failing tests**

Edit `Condotify.Mobile.Tests/MobileSessionCoordinatorTests.cs`. Find this exact text (end of `LoginResident_UsesResidentEndpointAndKeepsLicenseScope`, start of the next test):

```csharp
        Assert.True(result.Success);
        Assert.Equal(MobilePrincipalKind.Resident, service.Current?.Principal);
        Assert.Equal(licenseId, service.Current?.LicenseId);
        Assert.Equal("/api/auth/resident/login", handler.Paths.Single());
    }

    [Fact]
    public async Task ConcurrentTokenReads_RotateRefreshTokenOnlyOnce()
```

Replace it with:

```csharp
        Assert.True(result.Success);
        Assert.Equal(MobilePrincipalKind.Resident, service.Current?.Principal);
        Assert.Equal(licenseId, service.Current?.LicenseId);
        Assert.Equal("/api/auth/resident/login", handler.Paths.Single());
    }

    [Fact]
    public async Task ForgotPassword_PostsToResidentForgotEndpoint()
    {
        var handler = new RecordingHandler(_ => Json(new { result = "Accepted" }, HttpStatusCode.Accepted));
        var service = Create(handler, new MemoryVault());

        await service.ForgotPasswordAsync("lucas@example.com");

        Assert.Equal("/api/auth/resident/password/forgot", handler.Paths.Single());
    }

    [Fact]
    public async Task ResetPassword_ReturnsSuccessWhenBackendAccepts()
    {
        var handler = new RecordingHandler(_ => Json(new { result = "Success" }));
        var service = Create(handler, new MemoryVault());

        var result = await service.ResetPasswordAsync("recovery-token", "NovaSenha123!");

        Assert.True(result.Success);
        Assert.Equal("/api/auth/resident/password/reset", handler.Paths.Single());
    }

    [Fact]
    public async Task ResetPassword_ReturnsBackendErrorWhenTokenInvalid()
    {
        var handler = new RecordingHandler(_ => Json(
            new { result = "InvalidToken", error = "Codigo de recuperacao invalido ou expirado." },
            HttpStatusCode.BadRequest));
        var service = Create(handler, new MemoryVault());

        var result = await service.ResetPasswordAsync("bad-token", "NovaSenha123!");

        Assert.False(result.Success);
        Assert.Equal("Codigo de recuperacao invalido ou expirado.", result.Error);
    }

    [Fact]
    public async Task ConcurrentTokenReads_RotateRefreshTokenOnlyOnce()
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Condotify.Mobile.Tests --filter "ForgotPassword_PostsToResidentForgotEndpoint|ResetPassword_ReturnsSuccessWhenBackendAccepts|ResetPassword_ReturnsBackendErrorWhenTokenInvalid"`
Expected: build error — `ForgotPasswordAsync`, `ResetPasswordAsync` and `MobilePasswordResetResult` do not exist yet. That build failure is the RED state.

- [ ] **Step 3: Add `MobilePasswordResetResult`**

Edit `Condotify.Mobile.Core/MobileSession.cs`. Find:

```csharp
public sealed record MobileLoginResult(
    bool Success,
    bool MfaRequired,
    string Error,
    string ChallengeToken = "");
```

Replace with:

```csharp
public sealed record MobileLoginResult(
    bool Success,
    bool MfaRequired,
    string Error,
    string ChallengeToken = "");

public sealed record MobilePasswordResetResult(bool Success, string Error);
```

- [ ] **Step 4: Implement the two coordinator methods**

Edit `Condotify.Mobile.Core/MobileSessionCoordinator.cs`. Find (end of `VerifyStaffMfaAsync`, start of `LogoutAsync`):

```csharp
        var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/mfa/verify",
            new { ChallengeToken = challengeToken, Code = code, DeviceLabel = deviceLabel },
            cancellationToken);
        return await CompleteLoginAsync(response, MobilePrincipalKind.Staff, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
```

Replace with:

```csharp
        var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/mfa/verify",
            new { ChallengeToken = challengeToken, Code = code, DeviceLabel = deviceLabel },
            cancellationToken);
        return await CompleteLoginAsync(response, MobilePrincipalKind.Staff, cancellationToken);
    }

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/resident/password/forgot",
            new { Email = email.Trim() },
            cancellationToken);
    }

    public async Task<MobilePasswordResetResult> ResetPasswordAsync(
        string token,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            "api/auth/resident/password/reset",
            new { Token = token.Trim(), NewPassword = newPassword },
            cancellationToken);
        using (response)
        {
            ResetPasswordResponse? payload = null;
            try { payload = await response.Content.ReadFromJsonAsync<ResetPasswordResponse>(JsonOptions, cancellationToken); }
            catch (JsonException) { }

            return response.IsSuccessStatusCode && payload?.Result == "Success"
                ? new MobilePasswordResetResult(true, string.Empty)
                : new MobilePasswordResetResult(false, payload?.Error ?? "Nao foi possivel redefinir a senha agora.");
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
```

Then find the private response DTO at the end of the file:

```csharp
    private sealed class AuthResponse
    {
        public string Result { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public long? ExpiresIn { get; set; }
        public bool MfaRequired { get; set; }
        public string? ChallengeToken { get; set; }
        public Guid? ResidentId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public Guid? LicenseId { get; set; }
        public string? LicenseName { get; set; }
    }
}
```

Replace with:

```csharp
    private sealed class AuthResponse
    {
        public string Result { get; set; } = string.Empty;
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public long? ExpiresIn { get; set; }
        public bool MfaRequired { get; set; }
        public string? ChallengeToken { get; set; }
        public Guid? ResidentId { get; set; }
        public string? Name { get; set; }
        public string? Email { get; set; }
        public Guid? LicenseId { get; set; }
        public string? LicenseName { get; set; }
    }

    private sealed class ResetPasswordResponse
    {
        public string Result { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test Condotify.Mobile.Tests`
Expected: PASS, all tests including the 3 new ones and the pre-existing ones (no regressions).

- [ ] **Step 6: Commit**

```bash
git add Condotify.Mobile.Core/MobileSession.cs Condotify.Mobile.Core/MobileSessionCoordinator.cs Condotify.Mobile.Tests/MobileSessionCoordinatorTests.cs
git commit -m "feat: add resident forgot/reset password calls to MobileSessionCoordinator"
```

---

### Task 2: Redesign Credentials + Mfa screens (hero + sheet)

**Files:**
- Modify (full rewrite): `Condotify.Mobile/Components/Pages/Login.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:**
- Consumes: nothing from Task 1 yet (this task only redesigns existing login/MFA behavior — no new feature).
- Produces (consumed by Task 3 and Task 4): the file `Login.razor` with a `private enum LoginStep { Credentials, Mfa }` field `_step`, a `GoBack()` method, and a `.login-body` markup region that Tasks 3/4 extend with more `else if` branches. Also produces the CSS class names: `.login-page`, `.login-card`, `.login-hero`, `.login-navbar`, `.login-body`, `.login-submit`, `.login-footnote`, `.mfa-field` — reused by later tasks.

This task changes the visual design only; behavior for staff/resident login and MFA stays identical to today.

- [ ] **Step 1: Rewrite `Login.razor`**

Replace the entire contents of `Condotify.Mobile/Components/Pages/Login.razor` with:

```razor
@page "/login"
@inject MobileSessionCoordinator Session
@inject MobileDeviceContext Device
@inject MobileAppState AppState
@inject MobilePushLifecycle Push
@inject NavigationManager Navigation

<PageTitle>Entrar | Condotify</PageTitle>
<div class="login-page">
    <div class="login-card">
        @if (_step == LoginStep.Credentials)
        {
            <div class="login-hero">
                <div class="login-hero-mark" aria-hidden="true">C</div>
                <MudText Typo="Typo.h5">Condotify</MudText>
                <MudText Typo="Typo.caption">Controle de acesso inteligente</MudText>
            </div>
        }
        else
        {
            <div class="login-navbar">
                <MudIconButton Icon="@Icons.Material.Outlined.ArrowBack"
                               Size="Size.Small"
                               OnClick="GoBack"
                               aria-label="Voltar" />
                <MudText Typo="Typo.subtitle1">@StepTitle</MudText>
            </div>
        }

        <div class="login-body">
            @if (_step == LoginStep.Credentials)
            {
                <div class="principal-switch" role="group" aria-label="Tipo de acesso">
                    <button type="button"
                            class="@PrincipalClass(MobilePrincipalKind.Staff)"
                            aria-pressed="@(_principal == MobilePrincipalKind.Staff)"
                            @onclick="() => ChangePrincipal(MobilePrincipalKind.Staff)">
                        <MudIcon Icon="@Icons.Material.Outlined.Badge" Size="Size.Small" />
                        <span>Equipe</span>
                    </button>
                    <button type="button"
                            class="@PrincipalClass(MobilePrincipalKind.Resident)"
                            aria-pressed="@(_principal == MobilePrincipalKind.Resident)"
                            @onclick="() => ChangePrincipal(MobilePrincipalKind.Resident)">
                        <MudIcon Icon="@Icons.Material.Outlined.Home" Size="Size.Small" />
                        <span>Morador</span>
                    </button>
                </div>

                <MudForm @ref="_form" class="login-form">
                    @if (!string.IsNullOrWhiteSpace(_error))
                    {
                        <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Icon="@Icons.Material.Outlined.ErrorOutline" Dense="true">@_error</MudAlert>
                    }
                    <MudTextField @bind-Value="_email"
                                  Label="E-mail"
                                  Placeholder="nome@empresa.com.br"
                                  InputType="InputType.Email"
                                  Required="true"
                                  RequiredError="Informe o e-mail."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.Start"
                                  AdornmentIcon="@Icons.Material.Outlined.AlternateEmail"
                                  AutoFocus="true" />
                    <MudTextField @bind-Value="_password"
                                  Label="Senha"
                                  Placeholder="Digite sua senha"
                                  InputType="@(_showPassword ? InputType.Text : InputType.Password)"
                                  Required="true"
                                  RequiredError="Informe a senha."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.End"
                                  AdornmentIcon="@(_showPassword ? Icons.Material.Outlined.VisibilityOff : Icons.Material.Outlined.Visibility)"
                                  AdornmentAriaLabel="Exibir ou ocultar senha"
                                  OnAdornmentClick="TogglePassword" />
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               EndIcon="@(_busy ? null : Icons.Material.Outlined.ArrowForward)"
                               Disabled="_busy"
                               OnClick="SubmitAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Entrar")
                    </MudButton>
                </MudForm>

                <div class="login-footnote">
                    <MudIcon Icon="@Icons.Material.Outlined.Lock" Size="Size.Small" />
                    <span>Conexão protegida</span>
                </div>
            }
            else if (_step == LoginStep.Mfa)
            {
                <div class="login-lead">Informe o código exibido no seu aplicativo autenticador.</div>
                <MudForm @ref="_form" class="login-form">
                    @if (!string.IsNullOrWhiteSpace(_error))
                    {
                        <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Icon="@Icons.Material.Outlined.ErrorOutline" Dense="true">@_error</MudAlert>
                    }
                    <MudTextField @bind-Value="_mfaCode"
                                  Label="Código de segurança"
                                  Placeholder="000000"
                                  Required="true"
                                  RequiredError="Informe o código."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.Start"
                                  AdornmentIcon="@Icons.Material.Outlined.Password"
                                  Class="mfa-field"
                                  AutoFocus="true" />
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               EndIcon="@(_busy ? null : Icons.Material.Outlined.ArrowForward)"
                               Disabled="_busy"
                               OnClick="SubmitAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Confirmar acesso")
                    </MudButton>
                </MudForm>
            }
        </div>
    </div>
</div>

@code {
    private MudForm? _form;
    private LoginStep _step = LoginStep.Credentials;
    private MobilePrincipalKind _principal = MobilePrincipalKind.Staff;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _mfaCode = string.Empty;
    private string _challenge = string.Empty;
    private string _error = string.Empty;
    private bool _showPassword;
    private bool _busy;

    private enum LoginStep { Credentials, Mfa }

    private string StepTitle => _step switch
    {
        LoginStep.Mfa => "Confirme sua identidade",
        _ => string.Empty
    };

    private string PrincipalClass(MobilePrincipalKind principal) =>
        _principal == principal ? "active" : string.Empty;

    private void ChangePrincipal(MobilePrincipalKind principal)
    {
        _principal = principal;
        _step = LoginStep.Credentials;
        _challenge = string.Empty;
        _mfaCode = string.Empty;
        _error = string.Empty;
    }

    private void TogglePassword() => _showPassword = !_showPassword;

    private void GoBack()
    {
        _error = string.Empty;
        _step = LoginStep.Credentials;
        _challenge = string.Empty;
        _mfaCode = string.Empty;
    }

    private async Task SubmitAsync()
    {
        await _form!.ValidateAsync();
        if (!_form.IsValid || _busy) return;
        _busy = true;
        _error = string.Empty;
        try
        {
            MobileLoginResult result;
            if (_step == LoginStep.Mfa)
                result = await Session.VerifyStaffMfaAsync(_challenge, _mfaCode, Device.DeviceLabel);
            else if (_principal == MobilePrincipalKind.Resident)
                result = await Session.LoginResidentAsync(_email, _password, Device.DeviceLabel);
            else
                result = await Session.LoginStaffAsync(_email, _password, Device.DeviceLabel);

            if (result.MfaRequired)
            {
                _challenge = result.ChallengeToken;
                _step = LoginStep.Mfa;
                return;
            }
            if (!result.Success)
            {
                _error = result.Error;
                return;
            }

            if (Session.Current?.Principal == MobilePrincipalKind.Staff)
                await AppState.LoadLicensesAsync();
            await Push.RegisterAsync();
            Navigation.NavigateTo("/home", replace: true);
        }
        catch (HttpRequestException)
        {
            _error = "Sem conexao com a central. Verifique sua rede e tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }
}
```

- [ ] **Step 2: Replace the login CSS block (part A — the static rules)**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find this exact block (starts at `.login-page {`, ends at `.login-footnote .mud-icon-root { ... }`):

```css
.login-page {
    --mud-palette-background-gray: #f4f7fb;
    --mud-palette-surface: #ffffff;
    --mud-palette-text-primary: #182234;
    --mud-palette-text-secondary: #647188;
    --mud-palette-lines-default: #dce3ed;
    --mud-palette-primary: #3156d3;
    --mud-palette-primary-text: #ffffff;
    --mud-palette-tertiary: #007c69;
    --mud-palette-success: #12805a;
    position: relative;
    min-height: 100vh;
    display: grid;
    place-items: center;
    overflow: hidden;
    padding: max(28px, env(safe-area-inset-top)) 24px max(28px, env(safe-area-inset-bottom));
    color: var(--mud-palette-text-primary);
    background: var(--mud-palette-background-gray);
}
.login-accent { position: fixed; z-index: 1; inset: 0 0 auto; height: 4px; background: var(--mud-palette-primary); }
.login-accent span { display: block; width: min(24%, 240px); height: 100%; margin-left: auto; background: var(--mud-palette-tertiary); }
.login-wrap { position: relative; z-index: 2; width: min(900px, 100%); }
.login-surface { min-height: 570px; display: grid; grid-template-columns: 330px minmax(0, 1fr); overflow: hidden; border: 1px solid #d9e1ec; border-radius: 8px; background: #ffffff; box-shadow: 0 22px 60px rgba(31, 48, 78, .11); }
.login-context { position: relative; min-width: 0; display: flex; flex-direction: column; padding: 34px 32px 30px; overflow: hidden; border-right: 1px solid #d8e2f2; background: #edf3ff; }
.login-brand, .login-mobile-brand { display: flex; align-items: center; gap: 12px; }
.login-brand-mark { width: 44px; height: 44px; flex: 0 0 44px; display: grid; place-items: center; border-radius: 8px; background: var(--mud-palette-primary); color: #ffffff; font-size: 1.12rem; font-weight: 800; }
.login-brand-copy { min-width: 0; display: grid; gap: 1px; }
.login-brand-copy .mud-typography-h5 { color: #182234; font-size: 1.12rem; font-weight: 750; }
.login-brand-copy .mud-typography-caption { color: #647188; }
.login-mobile-brand { display: none; }
.login-access-map { position: relative; width: 250px; height: 290px; align-self: center; margin: auto 0; }
.access-line { position: absolute; display: block; background: #b9c9ec; }
.access-line-one { top: 128px; left: 25px; width: 200px; height: 2px; }
.access-line-two { top: 42px; left: 124px; width: 2px; height: 205px; }
.access-terminal { position: absolute; z-index: 3; top: 76px; left: 76px; width: 98px; height: 112px; display: grid; place-items: center; border: 1px solid #b9c9ec; border-radius: 8px; background: #ffffff; box-shadow: 0 12px 28px rgba(49, 86, 211, .13); color: #3156d3; }
.access-terminal > .mud-icon-root { width: 46px; height: 46px; font-size: 46px; }
.access-terminal-state { position: absolute; right: 12px; bottom: 12px; width: 9px; height: 9px; border: 2px solid #ffffff; border-radius: 50%; background: #12805a; box-shadow: 0 0 0 2px #c8e7db; }
.access-node, .access-confirmed { position: absolute; z-index: 4; display: grid; place-items: center; border: 1px solid #c8d4eb; border-radius: 7px; background: #ffffff; box-shadow: 0 6px 16px rgba(31, 48, 78, .08); color: #3156d3; }
.access-node { width: 46px; height: 46px; }
.access-node-face { top: 19px; left: 102px; }
.access-node-key { top: 105px; left: 2px; color: #007c69; }
.access-node-camera { top: 105px; right: 2px; }
.access-confirmed { right: 107px; bottom: 18px; width: 36px; height: 36px; border-color: #a8d7c8; background: #e9f7f2; color: #12805a; }
.access-confirmed .mud-icon-root { width: 19px; height: 19px; font-size: 19px; }
.login-context-status { display: flex; align-items: center; gap: 8px; color: #007c69; font-size: .78rem; font-weight: 700; }
.login-panel { min-width: 0; display: flex; flex-direction: column; justify-content: center; padding: 54px 58px; background: #ffffff; }
.login-heading { position: relative; display: flex; align-items: flex-start; gap: 9px; margin-bottom: 28px; }
.login-heading > div { min-width: 0; }
.login-heading .mud-typography-h1 { margin-bottom: 7px; color: #182234; font-size: 1.75rem; line-height: 1.22; font-weight: 750; }
.login-heading .login-back { flex: 0 0 auto; margin: -5px 0 0 -8px; }
.principal-switch { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 4px; padding: 4px; margin-bottom: 24px; border: 1px solid #dce3ed; border-radius: 8px; background: #f4f7fb; }
.principal-switch button { min-width: 0; min-height: 44px; display: flex; align-items: center; justify-content: center; gap: 8px; border: 0; border-radius: 6px; background: transparent; color: var(--mud-palette-text-secondary); font: inherit; font-weight: 650; cursor: pointer; transition: color 140ms ease, background-color 140ms ease, box-shadow 140ms ease; }
.principal-switch button:hover { color: var(--mud-palette-text-primary); }
.principal-switch button:focus-visible { outline: 2px solid var(--mud-palette-primary); outline-offset: 1px; }
.principal-switch button.active { background: #ffffff; color: var(--mud-palette-primary); box-shadow: 0 1px 5px rgba(24, 35, 52, .12); }
.login-form { display: grid; gap: 18px; }
.login-form .mud-input-control { margin-top: 0; }
.login-form .mud-input-outlined-border { border-color: #bcc8d8; }
.login-form .mud-input-control:focus-within .mud-input-adornment { color: #3156d3; }
.login-form .mud-button-root { min-height: 50px; margin-top: 2px; border-radius: 6px; font-weight: 700; box-shadow: 0 7px 16px rgba(49, 86, 211, .2); }
.login-form .mud-button-label { text-transform: none; font-size: .95rem; }
.login-form .mud-button-icon-root { transition: transform 160ms ease; }
.login-form .mud-button-root:not(:disabled):hover .mud-button-icon-root { transform: translateX(3px); }
.login-form .mud-alert { border-radius: 6px; }
.mfa-field input { text-align: center; font-size: 1.12rem; font-weight: 700; letter-spacing: 0; }
.login-footnote { min-height: 24px; display: flex; align-items: center; justify-content: center; gap: 7px; margin-top: 18px; color: #647188; font-size: .75rem; text-align: center; }
.login-footnote .mud-icon-root { width: 16px; height: 16px; font-size: 16px; color: #12805a; }
```

Replace with:

```css
.login-page {
    position: relative;
    min-height: 100vh;
    display: grid;
    place-items: start center;
    overflow-x: hidden;
    padding: max(28px, env(safe-area-inset-top)) 20px max(28px, env(safe-area-inset-bottom));
    color: #182234;
    background: #f4f7fb;
}
.login-card { width: min(400px, 100%); margin-top: min(6vh, 40px); overflow: hidden; border: 1px solid #dce3ed; border-radius: 20px; background: #ffffff; box-shadow: 0 22px 50px rgba(31, 48, 78, .12); }
.login-hero { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 2px; min-height: 176px; padding: 28px 24px; background: linear-gradient(160deg, #3156d3, #1c2f7a); color: #ffffff; text-align: center; }
.login-hero-mark { width: 46px; height: 46px; display: grid; place-items: center; margin-bottom: 8px; border: 1px solid rgba(255, 255, 255, .32); border-radius: 13px; background: rgba(255, 255, 255, .14); font-size: 1.05rem; font-weight: 800; }
.login-hero .mud-typography-h5 { color: #ffffff; font-size: 1.05rem; font-weight: 750; }
.login-hero .mud-typography-caption { color: rgba(255, 255, 255, .78); }
.login-navbar { display: flex; align-items: center; gap: 6px; min-height: 56px; padding: 0 10px; border-bottom: 1px solid #dce3ed; }
.login-navbar .mud-typography-subtitle1 { color: #182234; font-size: .95rem; font-weight: 750; }
.login-body { padding: 24px 22px 26px; }
.principal-switch { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 4px; padding: 4px; margin-bottom: 20px; border: 1px solid #dce3ed; border-radius: 10px; background: #f4f7fb; }
.principal-switch button { min-width: 0; min-height: 42px; display: flex; align-items: center; justify-content: center; gap: 7px; border: 0; border-radius: 7px; background: transparent; color: #647188; font: inherit; font-weight: 650; cursor: pointer; transition: color 140ms ease, background-color 140ms ease, box-shadow 140ms ease; }
.principal-switch button:hover { color: #182234; }
.principal-switch button:focus-visible { outline: 2px solid #3156d3; outline-offset: 1px; }
.principal-switch button.active { background: #ffffff; color: #3156d3; box-shadow: 0 1px 5px rgba(24, 35, 52, .12); }
.login-form { display: grid; gap: 16px; }
.login-form .mud-input-control { margin-top: 0; }
.login-form .mud-input-outlined-border { border-color: #cdd5e2; }
.login-form .mud-input-control:focus-within .mud-input-adornment { color: #3156d3; }
.login-form .mud-alert { border-radius: 8px; }
.login-submit.mud-button-root { min-height: 48px; border-radius: 22px; font-weight: 700; box-shadow: 0 8px 16px rgba(49, 86, 211, .24); }
.login-submit .mud-button-label { text-transform: none; font-size: .92rem; }
.login-submit .mud-button-icon-root { transition: transform 160ms ease; }
.login-submit:not(:disabled):hover .mud-button-icon-root { transform: translateX(3px); }
.login-footnote { display: flex; align-items: center; justify-content: center; gap: 6px; margin-top: 16px; color: #647188; font-size: .72rem; text-align: center; }
.login-footnote .mud-icon-root { width: 14px; height: 14px; font-size: 14px; color: #12805a; }
.mfa-field input { text-align: center; font-size: 1.12rem; font-weight: 700; letter-spacing: 2px; }
```

- [ ] **Step 3: Replace the login CSS block (part B — the shared reduced-motion media query)**

In the same file, find this exact block (it mixes login animations with `.skeleton-*` rules used elsewhere in the app — keep the skeleton rules):

```css
@media (prefers-reduced-motion: no-preference) {
    .skeleton-metrics span, .skeleton-heading, .skeleton-row { animation: skeleton-pulse 1100ms ease-in-out infinite alternate; }
    .login-wrap { animation: login-enter 260ms ease-out both; }
    .access-terminal { animation: access-terminal-enter 360ms 100ms ease-out both; }
    .access-node { animation: access-node-enter 280ms ease-out both; }
    .access-node-face { animation-delay: 160ms; }
    .access-node-key { animation-delay: 220ms; }
    .access-node-camera { animation-delay: 280ms; }
    .access-confirmed { animation: access-confirmed-enter 300ms 360ms ease-out both; }
    .access-terminal-state { animation: access-status 1800ms 700ms ease-in-out infinite; }
    @keyframes login-enter {
        from { opacity: 0; transform: translateY(8px); }
        to { opacity: 1; transform: translateY(0); }
    }
    @keyframes access-terminal-enter {
        from { opacity: 0; transform: translateY(7px); }
        to { opacity: 1; transform: translateY(0); }
    }
    @keyframes access-node-enter {
        from { opacity: 0; transform: scale(.88); }
        to { opacity: 1; transform: scale(1); }
    }
    @keyframes access-confirmed-enter {
        from { opacity: 0; transform: translateY(-5px) scale(.9); }
        to { opacity: 1; transform: translateY(0) scale(1); }
    }
    @keyframes access-status {
        0%, 100% { opacity: 1; transform: scale(1); }
        50% { opacity: .62; transform: scale(.82); }
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

- [ ] **Step 4: Replace the login CSS block (part C — the responsive breakpoints)**

In the same file, find this exact block (three media queries, all login-specific):

```css
@media (max-width: 760px) {
    .login-page { place-items: start center; overflow-x: hidden; overflow-y: auto; padding-inline: 16px; }
    .login-wrap { width: min(460px, 100%); margin-top: min(7vh, 46px); }
    .login-surface { min-height: 0; display: block; }
    .login-context { min-height: 178px; height: 178px; display: flex; padding: 22px 24px; border-right: 0; border-bottom: 1px solid #d8e2f2; }
    .login-context .login-brand { position: relative; z-index: 5; align-items: flex-start; }
    .login-context .login-brand-copy { padding-top: 2px; }
    .login-context .login-brand-copy .mud-typography-caption { max-width: 170px; display: block; }
    .login-access-map { position: absolute; top: 8px; right: -25px; width: 250px; height: 290px; margin: 0; transform: scale(.56); transform-origin: top right; }
    .login-context-status { position: absolute; z-index: 5; left: 24px; bottom: 20px; }
    .login-mobile-brand { display: none; }
    .login-panel { padding: 34px 36px 38px; }
}

@media (max-width: 440px) {
    .login-page { padding: max(16px, env(safe-area-inset-top)) 12px max(16px, env(safe-area-inset-bottom)); background: #f4f7fb; }
    .login-wrap { width: 100%; margin-top: 0; }
    .login-surface { box-shadow: 0 14px 34px rgba(31, 48, 78, .1); }
    .login-context { min-height: 162px; height: 162px; padding: 20px; }
    .login-brand-mark { width: 42px; height: 42px; flex-basis: 42px; }
    .login-access-map { top: 4px; right: -36px; transform: scale(.5); }
    .login-context-status { left: 20px; bottom: 17px; }
    .login-panel { padding: 28px 22px; }
    .login-heading .mud-typography-h1 { font-size: 1.5rem; }
    .login-footnote { padding-inline: 12px; }
}

@media (max-width: 360px) {
    .login-context .login-brand-copy .mud-typography-caption { max-width: 130px; }
    .login-access-map { right: -48px; transform: scale(.46); }
    .login-panel { padding-inline: 18px; }
}
```

Replace with:

```css
@media (max-width: 440px) {
    .login-page { padding: max(16px, env(safe-area-inset-top)) 14px max(16px, env(safe-area-inset-bottom)); }
    .login-card { margin-top: 0; border-radius: 16px; }
    .login-hero { min-height: 156px; padding: 24px 20px; }
    .login-body { padding: 22px 18px 24px; }
}
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 6: Manual check**

Run the Windows target (`dotnet build -t:Run -f net9.0-windows10.0.19041.0` from `Condotify.Mobile/`, or launch from Visual Studio) and confirm:
- The login screen shows the gradient hero with "Condotify" branding, followed by the white sheet with the Equipe/Morador switch and the form.
- Switching Equipe/Morador still works.
- Submitting invalid credentials shows the error alert.
- Staff MFA still shows the compact nav-bar header with a working back button.

- [ ] **Step 7: Commit**

```bash
git add Condotify.Mobile/Components/Pages/Login.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "refactor: redesign mobile login screen as hero + sheet"
```

---

### Task 3: Resident "Esqueci minha senha" flow

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Login.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `Session.ForgotPasswordAsync(string, CancellationToken)` and `Session.ResetPasswordAsync(string, string, CancellationToken)` → `MobilePasswordResetResult` from Task 1.
- Produces (consumed by Task 4): `LoginStep` enum extended with `ForgotEmail, ForgotReset, ForgotDone`; `OpenForgotPassword()` method (Task 4 extends its body); `GoBack()` extended to branch on `ForgotReset`.

- [ ] **Step 1: Extend the `LoginStep` enum**

Edit `Condotify.Mobile/Components/Pages/Login.razor`. Find:

```csharp
    private enum LoginStep { Credentials, Mfa }
```

Replace with:

```csharp
    private enum LoginStep { Credentials, Mfa, ForgotEmail, ForgotReset, ForgotDone }
```

- [ ] **Step 2: Extend `StepTitle`**

Find:

```csharp
    private string StepTitle => _step switch
    {
        LoginStep.Mfa => "Confirme sua identidade",
        _ => string.Empty
    };
```

Replace with:

```csharp
    private string StepTitle => _step switch
    {
        LoginStep.Mfa => "Confirme sua identidade",
        LoginStep.ForgotEmail => "Recuperar senha",
        LoginStep.ForgotReset => "Redefinir senha",
        _ => string.Empty
    };
```

- [ ] **Step 3: Hide the nav bar on the `ForgotDone` confirmation screen**

Find:

```razor
        else
        {
            <div class="login-navbar">
```

Replace with:

```razor
        else if (_step != LoginStep.ForgotDone)
        {
            <div class="login-navbar">
```

- [ ] **Step 4: Make `GoBack()` return to `ForgotEmail` from `ForgotReset`**

Find:

```csharp
    private void GoBack()
    {
        _error = string.Empty;
        _step = LoginStep.Credentials;
        _challenge = string.Empty;
        _mfaCode = string.Empty;
    }
```

Replace with:

```csharp
    private void GoBack()
    {
        _error = string.Empty;
        _step = _step == LoginStep.ForgotReset ? LoginStep.ForgotEmail : LoginStep.Credentials;
        if (_step == LoginStep.Credentials)
        {
            _challenge = string.Empty;
            _mfaCode = string.Empty;
        }
    }
```

- [ ] **Step 5: Add the "Esqueci minha senha" link to the Credentials form**

Find (end of the password field and the Credentials submit button):

```razor
                                  AdornmentAriaLabel="Exibir ou ocultar senha"
                                  OnAdornmentClick="TogglePassword" />
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               EndIcon="@(_busy ? null : Icons.Material.Outlined.ArrowForward)"
                               Disabled="_busy"
                               OnClick="SubmitAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Entrar")
                    </MudButton>
                </MudForm>

                <div class="login-footnote">
```

Replace with:

```razor
                                  AdornmentAriaLabel="Exibir ou ocultar senha"
                                  OnAdornmentClick="TogglePassword" />
                    <button type="button" class="login-fp" @onclick="OpenForgotPassword">Esqueci minha senha</button>
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               EndIcon="@(_busy ? null : Icons.Material.Outlined.ArrowForward)"
                               Disabled="_busy"
                               OnClick="SubmitAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Entrar")
                    </MudButton>
                </MudForm>

                <div class="login-footnote">
```

- [ ] **Step 6: Add the three new view blocks**

Find (end of the Mfa block and the closing tags of `.login-body`/`.login-card`/`.login-page`):

```razor
                        @(_busy ? "Aguarde" : "Confirmar acesso")
                    </MudButton>
                </MudForm>
            }
        </div>
    </div>
</div>
```

Replace with:

```razor
                        @(_busy ? "Aguarde" : "Confirmar acesso")
                    </MudButton>
                </MudForm>
            }
            else if (_step == LoginStep.ForgotEmail)
            {
                <div class="login-lead">Informe o e-mail da sua conta. Se ele existir, enviaremos um código para redefinir sua senha.</div>
                <MudForm @ref="_form" class="login-form">
                    @if (!string.IsNullOrWhiteSpace(_error))
                    {
                        <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Icon="@Icons.Material.Outlined.ErrorOutline" Dense="true">@_error</MudAlert>
                    }
                    <MudTextField @bind-Value="_forgotEmail"
                                  Label="E-mail"
                                  Placeholder="nome@empresa.com.br"
                                  InputType="InputType.Email"
                                  Required="true"
                                  RequiredError="Informe o e-mail."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.Start"
                                  AdornmentIcon="@Icons.Material.Outlined.AlternateEmail"
                                  AutoFocus="true" />
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               Disabled="_busy"
                               OnClick="SubmitForgotEmailAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Enviar código")
                    </MudButton>
                </MudForm>
            }
            else if (_step == LoginStep.ForgotReset)
            {
                <div class="login-lead">Enviamos um código para <strong>@_forgotEmail</strong>, caso ele exista. Cole o código abaixo e defina sua nova senha. O código vale por 30 minutos.</div>
                <MudForm @ref="_form" class="login-form">
                    @if (!string.IsNullOrWhiteSpace(_error))
                    {
                        <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Icon="@Icons.Material.Outlined.ErrorOutline" Dense="true">@_error</MudAlert>
                    }
                    <MudTextField @bind-Value="_recoveryToken"
                                  Label="Código de recuperação"
                                  Placeholder="Cole aqui o código recebido por e-mail"
                                  Lines="2"
                                  Required="true"
                                  RequiredError="Informe o código."
                                  Variant="Variant.Outlined"
                                  Class="login-code-field" />
                    <MudTextField @bind-Value="_newPassword"
                                  Label="Nova senha"
                                  InputType="InputType.Password"
                                  Required="true"
                                  RequiredError="Informe a nova senha."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.Start"
                                  AdornmentIcon="@Icons.Material.Outlined.VpnKey" />
                    <MudTextField @bind-Value="_confirmPassword"
                                  Label="Confirmar senha"
                                  InputType="InputType.Password"
                                  Required="true"
                                  RequiredError="Confirme a nova senha."
                                  Variant="Variant.Outlined"
                                  Adornment="Adornment.Start"
                                  AdornmentIcon="@Icons.Material.Outlined.VpnKey" />
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               Disabled="_busy"
                               OnClick="SubmitForgotResetAsync">
                        @if (_busy)
                        {
                            <MudProgressCircular Indeterminate="true" Size="Size.Small" Class="mr-2" />
                        }
                        @(_busy ? "Aguarde" : "Redefinir senha")
                    </MudButton>
                </MudForm>
            }
            else if (_step == LoginStep.ForgotDone)
            {
                <div class="login-confirm">
                    <div class="login-confirm-icon" aria-hidden="true"><MudIcon Icon="@Icons.Material.Outlined.Check" /></div>
                    <MudText Typo="Typo.h1">Senha redefinida</MudText>
                    <div class="login-lead">Sua senha foi alterada com sucesso. Entre com a nova senha para continuar.</div>
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               OnClick="GoToCredentialsFromConfirmation">
                        Ir para o login
                    </MudButton>
                </div>
            }
        </div>
    </div>
</div>
```

- [ ] **Step 7: Add the new fields**

Find:

```csharp
    private string _error = string.Empty;
    private bool _showPassword;
    private bool _busy;
```

Replace with:

```csharp
    private string _error = string.Empty;
    private bool _showPassword;
    private bool _busy;
    private string _forgotEmail = string.Empty;
    private string _recoveryToken = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
```

- [ ] **Step 8: Add the new handlers**

Find the end of `SubmitAsync` and the closing brace of the `@code` block:

```csharp
        finally
        {
            _busy = false;
        }
    }
}
```

Replace with:

```csharp
        finally
        {
            _busy = false;
        }
    }

    private void OpenForgotPassword()
    {
        _error = string.Empty;
        if (_principal == MobilePrincipalKind.Resident)
            _step = LoginStep.ForgotEmail;
    }

    private async Task SubmitForgotEmailAsync()
    {
        await _form!.ValidateAsync();
        if (!_form.IsValid || _busy) return;
        _busy = true;
        _error = string.Empty;
        try
        {
            await Session.ForgotPasswordAsync(_forgotEmail);
            _recoveryToken = string.Empty;
            _newPassword = string.Empty;
            _confirmPassword = string.Empty;
            _step = LoginStep.ForgotReset;
        }
        catch (HttpRequestException)
        {
            _error = "Sem conexao com a central. Verifique sua rede e tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SubmitForgotResetAsync()
    {
        await _form!.ValidateAsync();
        if (!_form.IsValid || _busy) return;
        _error = string.Empty;
        if (_newPassword != _confirmPassword)
        {
            _error = "As senhas nao coincidem.";
            return;
        }
        _busy = true;
        try
        {
            var result = await Session.ResetPasswordAsync(_recoveryToken, _newPassword);
            if (!result.Success)
            {
                _error = result.Error;
                return;
            }
            _step = LoginStep.ForgotDone;
        }
        catch (HttpRequestException)
        {
            _error = "Sem conexao com a central. Verifique sua rede e tente novamente.";
        }
        finally
        {
            _busy = false;
        }
    }

    private void GoToCredentialsFromConfirmation()
    {
        _email = string.Empty;
        _password = string.Empty;
        _forgotEmail = string.Empty;
        _recoveryToken = string.Empty;
        _newPassword = string.Empty;
        _confirmPassword = string.Empty;
        _step = LoginStep.Credentials;
    }
}
```

- [ ] **Step 9: Add supporting CSS**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.login-footnote { display: flex; align-items: center; justify-content: center; gap: 6px; margin-top: 16px; color: #647188; font-size: .72rem; text-align: center; }
.login-footnote .mud-icon-root { width: 14px; height: 14px; font-size: 14px; color: #12805a; }
```

Replace with:

```css
.login-footnote { display: flex; align-items: center; justify-content: center; gap: 6px; margin-top: 16px; color: #647188; font-size: .72rem; text-align: center; }
.login-footnote .mud-icon-root { width: 14px; height: 14px; font-size: 14px; color: #12805a; }
.login-fp { justify-self: end; margin: -6px 0 4px; padding: 0; border: 0; background: none; color: #3156d3; font-size: .76rem; font-weight: 700; cursor: pointer; }
.login-fp:hover { text-decoration: underline; }
.login-lead { margin-bottom: 16px; color: #647188; font-size: .8rem; line-height: 1.55; }
.login-code-field textarea { font-family: Inter, monospace; overflow-wrap: anywhere; }
.login-confirm { padding: 14px 2px 4px; text-align: center; }
.login-confirm-icon { width: 52px; height: 52px; display: grid; place-items: center; margin: 0 auto 14px; border-radius: 50%; background: #e9f7f2; color: #12805a; font-size: 1.4rem; }
.login-confirm .mud-typography-h1 { margin-bottom: 8px; color: #182234; font-size: 1.25rem; font-weight: 750; }
```

- [ ] **Step 10: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 11: Manual check**

Run the app and, with "Morador" selected:
- Tap "Esqueci minha senha" → lands on the e-mail screen with a working back button (to Credentials).
- Submit an e-mail → lands on the code/new-password screen, back button returns to the e-mail screen (not Credentials).
- Submit with mismatched passwords → shows "As senhas nao coincidem." without calling the API.
- Submit with a bogus recovery code (backend not reachable/token invalid) → shows the backend's error message.
- With a real recovery token from a test resident (if an SMTP-less environment, check `CondotifyAPI` logs for the issued token), completing the reset lands on the "Senha redefinida" screen, and "Ir para o login" returns to Credentials with empty fields.

- [ ] **Step 12: Commit**

```bash
git add Condotify.Mobile/Components/Pages/Login.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "feat: add resident forgot-password flow to mobile login"
```

---

### Task 4: Staff "Esqueci minha senha" → contact support screen

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Login.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `LoginStep`, `OpenForgotPassword()`, `GoBack()` from Task 3.
- Produces: nothing further consumed by other tasks (this is the last feature task).

- [ ] **Step 1: Extend the `LoginStep` enum**

Find:

```csharp
    private enum LoginStep { Credentials, Mfa, ForgotEmail, ForgotReset, ForgotDone }
```

Replace with:

```csharp
    private enum LoginStep { Credentials, Mfa, ForgotEmail, ForgotReset, ForgotDone, StaffSupport }
```

- [ ] **Step 2: Extend `StepTitle`**

Find:

```csharp
    private string StepTitle => _step switch
    {
        LoginStep.Mfa => "Confirme sua identidade",
        LoginStep.ForgotEmail => "Recuperar senha",
        LoginStep.ForgotReset => "Redefinir senha",
        _ => string.Empty
    };
```

Replace with:

```csharp
    private string StepTitle => _step switch
    {
        LoginStep.Mfa => "Confirme sua identidade",
        LoginStep.ForgotEmail => "Recuperar senha",
        LoginStep.ForgotReset => "Redefinir senha",
        LoginStep.StaffSupport => "Esqueci minha senha",
        _ => string.Empty
    };
```

- [ ] **Step 3: Route staff to the support screen**

Find:

```csharp
    private void OpenForgotPassword()
    {
        _error = string.Empty;
        if (_principal == MobilePrincipalKind.Resident)
            _step = LoginStep.ForgotEmail;
    }
```

Replace with:

```csharp
    private void OpenForgotPassword()
    {
        _error = string.Empty;
        _step = _principal == MobilePrincipalKind.Resident ? LoginStep.ForgotEmail : LoginStep.StaffSupport;
    }
```

(`GoBack()` already sends every step other than `ForgotReset` to `Credentials`, so `StaffSupport` needs no change there.)

- [ ] **Step 4: Add the support screen markup**

Find the `ForgotDone` block and the closing tags right after it:

```razor
            else if (_step == LoginStep.ForgotDone)
            {
                <div class="login-confirm">
                    <div class="login-confirm-icon" aria-hidden="true"><MudIcon Icon="@Icons.Material.Outlined.Check" /></div>
                    <MudText Typo="Typo.h1">Senha redefinida</MudText>
                    <div class="login-lead">Sua senha foi alterada com sucesso. Entre com a nova senha para continuar.</div>
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               OnClick="GoToCredentialsFromConfirmation">
                        Ir para o login
                    </MudButton>
                </div>
            }
        </div>
    </div>
</div>
```

Replace with:

```razor
            else if (_step == LoginStep.ForgotDone)
            {
                <div class="login-confirm">
                    <div class="login-confirm-icon" aria-hidden="true"><MudIcon Icon="@Icons.Material.Outlined.Check" /></div>
                    <MudText Typo="Typo.h1">Senha redefinida</MudText>
                    <div class="login-lead">Sua senha foi alterada com sucesso. Entre com a nova senha para continuar.</div>
                    <MudButton FullWidth="true"
                               Size="Size.Large"
                               Variant="Variant.Filled"
                               Color="Color.Primary"
                               Class="login-submit"
                               OnClick="GoToCredentialsFromConfirmation">
                        Ir para o login
                    </MudButton>
                </div>
            }
            else if (_step == LoginStep.StaffSupport)
            {
                <div class="login-confirm">
                    <div class="login-support-icon" aria-hidden="true"><MudIcon Icon="@Icons.Material.Outlined.SupportAgent" /></div>
                    <MudText Typo="Typo.h1">Fale com o suporte</MudText>
                </div>
                <div class="login-lead">Por segurança, contas da equipe não redefinem a senha pelo aplicativo. Entre em contato para liberar o acesso.</div>
                <a class="login-support-row" href="mailto:@SupportEmail">
                    <MudIcon Icon="@Icons.Material.Outlined.AlternateEmail" />
                    <div>
                        <span class="login-support-label">E-mail</span>
                        <span class="login-support-value">@SupportEmail</span>
                    </div>
                </a>
                <a class="login-support-row" href="@SupportWhatsAppUrl" target="_blank" rel="noopener">
                    <MudIcon Icon="@Icons.Material.Outlined.Chat" />
                    <div>
                        <span class="login-support-label">WhatsApp</span>
                        <span class="login-support-value">@SupportPhoneDisplay</span>
                    </div>
                </a>
            }
        </div>
    </div>
</div>
```

- [ ] **Step 5: Add the support contact constants**

Find:

```csharp
    private string _forgotEmail = string.Empty;
    private string _recoveryToken = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
```

Replace with:

```csharp
    private string _forgotEmail = string.Empty;
    private string _recoveryToken = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    // Placeholder contact channel until a real support desk is wired up.
    private const string SupportEmail = "suporte@condotify.com.br";
    private const string SupportPhoneDisplay = "(11) 90000-0000";
    private const string SupportWhatsAppUrl = "https://wa.me/5511900000000";
```

- [ ] **Step 6: Add supporting CSS**

Edit `Condotify.Mobile/wwwroot/css/app.css`. Find:

```css
.login-confirm .mud-typography-h1 { margin-bottom: 8px; color: #182234; font-size: 1.25rem; font-weight: 750; }
```

Replace with:

```css
.login-confirm .mud-typography-h1 { margin-bottom: 8px; color: #182234; font-size: 1.25rem; font-weight: 750; }
.login-support-icon { width: 52px; height: 52px; display: grid; place-items: center; margin: 4px auto 14px; border-radius: 14px; background: #eef1fb; color: #3156d3; font-size: 1.3rem; }
.login-support-row { display: flex; align-items: center; gap: 12px; padding: 13px 14px; margin-bottom: 10px; border: 1px solid #dce3ed; border-radius: 12px; text-decoration: none; color: inherit; }
.login-support-row .mud-icon-root { flex: 0 0 auto; width: 32px; height: 32px; padding: 7px; border-radius: 9px; background: #eef1fb; color: #3156d3; }
.login-support-label { display: block; color: #647188; font-size: .66rem; font-weight: 650; text-transform: uppercase; letter-spacing: .02em; }
.login-support-value { display: block; color: #182234; font-size: .86rem; font-weight: 700; }
```

- [ ] **Step 7: Build to verify it compiles**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors.

- [ ] **Step 8: Manual check**

Run the app and, with "Equipe" selected:
- Tap "Esqueci minha senha" → lands on the support screen (headset icon, "Fale com o suporte", the explanatory line, and two tappable rows).
- The e-mail row opens the device's mail client with `suporte@condotify.com.br` pre-filled.
- The WhatsApp row opens `https://wa.me/5511900000000`.
- Back button returns to Credentials with "Equipe" still selected.

- [ ] **Step 9: Commit**

```bash
git add Condotify.Mobile/Components/Pages/Login.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "feat: add staff contact-support screen to mobile login"
```

---

### Task 5: Full verification pass

**Files:** none (verification only).

- [ ] **Step 1: Run the full mobile test suite**

Run: `dotnet test Condotify.Mobile.Tests`
Expected: PASS, all tests (including the 3 added in Task 1).

- [ ] **Step 2: Build the whole mobile app for Windows**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-windows10.0.19041.0`
Expected: Build succeeded, no errors, no new warnings introduced by `Login.razor`.

- [ ] **Step 3: Full manual walkthrough**

Launch the app and walk every state once in a single session: Equipe login (success), Equipe login (bad password → error), Equipe login requiring MFA (correct + incorrect code), Morador login (success), Morador forgot-password happy path end to end (email → code+password → confirmation → back to login), Morador forgot-password with mismatched passwords, Equipe "Esqueci minha senha" → support screen → back. Confirm no layout breaks at a narrow phone width and at a resized desktop window (drag the Windows app window wide — the card should stay centered at ~400px, not stretch).

- [ ] **Step 4: Confirm no other page regressed**

Since `app.css`'s shared reduced-motion media query was edited, open any page that shows a loading skeleton (e.g. the dashboard while data loads) and confirm the pulsing skeleton animation still plays.

- [ ] **Step 5: Commit (only if Step 3/4 required fixes)**

If the manual walkthrough surfaced any fix, commit it separately with a message describing what was wrong — do not fold fixes silently into earlier commits.
