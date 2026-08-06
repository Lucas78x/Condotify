# Morador emitir/revogar o passe digital pelo app — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a resident issue/revoke their own visit's Google/Apple Wallet digital pass from the mobile app, gated by a per-license toggle.

**Architecture:** Extract the existing staff-only pass issue/revoke logic (today only in `DigitalPassesController`) into a shared `DigitalPassIssuanceService`. Add a resident-scoped `POST`/`DELETE api/resident/visits/{visitId}/pass` pair to `ResidentProfileController` that calls the same service, authorized by visit ownership (`HostResidentId`) instead of a staff permission, gated by a new `AllowResidentDigitalPass` license policy flag. Wire the mobile `VisitorPassDialog.razor` to call it.

**Tech Stack:** ASP.NET Core 8 / EF Core (Postgres) API; Blazor Server portal; MAUI Blazor Hybrid mobile app; xUnit for .NET tests (no DB test provider available — DB-touching code is guarded by reflection/attribute tests and a manual verification harness note, matching this repo's existing pattern in `ResidentProfileControllerTests.cs`).

## Global Constraints

- Reuse `DigitalPassProviderService.Build`/`AppleWalletPassService` as-is — do not duplicate wallet JWT/pkpass logic.
- No new database table for the license flag — reuse the existing per-license `LicenseCredentialPolicies` table (see design spec's explicit decision to defer the general feature-toggle system).
- The staff portal flow (`DigitalPassesController`, `ConciergeVisitDetailDialog.razor`) must keep behaving exactly as it does today — this plan only adds a second caller, it does not change staff permissions or UI.
- Migrations: `dotnet ef migrations add <Nome> --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`. **Read the whole generated migration before applying** — the dev database has real data.
- `CondotifyAPI.Tests` has no EF InMemory/Sqlite provider (Npgsql only) — DB-touching controller/service code is not unit-testable in this repo today. Follow the established pattern: pure logic gets xUnit tests; DB-coupled endpoints get reflection-based attribute/route tests (see `ResidentProfileControllerTests.cs`) plus a "Manual harness note" instructing verification against the running dev API.

---

### Task 1: Regression test for the RSA-disposed-key bug

**Files:**
- Modify: `CondotifyAPI.Tests/DigitalPassProviderServiceTests.cs`

**Interfaces:**
- Consumes: `DigitalPassProviderService.Build(DigitalPassDTO, string token, string publicUrl)` (existing, unchanged signature).
- Produces: nothing new — this is a regression guard for the `CryptoProviderFactory.CacheSignatureProviders = false` fix already shipped in `DigitalPassProviderService.BuildGoogleWalletUrl`.

Earlier this session, calling `Build` twice in the same process with Google Wallet configured threw `ObjectDisposedException` (Microsoft.IdentityModel caches signature providers by key material; the `using var rsa` from the first call got disposed, and the second call's cached provider tried to sign with it). It's fixed in code but has no test guarding it — add one now, before building more callers on top of this service.

- [ ] **Step 1: Write the failing-if-unfixed test**

Add to `CondotifyAPI.Tests/DigitalPassProviderServiceTests.cs`, right after `Build_ShouldCreateGoogleSaveUrlOnlyWhenSigningConfigurationIsComplete`:

```csharp
    [Fact]
    public void Build_ShouldWorkTwiceInARowWithTheSameConfiguredKey()
    {
        using var rsa = RSA.Create(2048);
        var settings = new Dictionary<string, string?>
        {
            ["DigitalPass:GoogleWallet:IssuerId"] = "3388000000022000000",
            ["DigitalPass:GoogleWallet:ServiceAccountEmail"] = "wallet@test.iam.gserviceaccount.com",
            ["DigitalPass:GoogleWallet:PrivateKey"] = rsa.ExportRSAPrivateKeyPem(),
            ["DigitalPass:GoogleWallet:ClassSuffix"] = "condotify_access"
        };
        var service = new DigitalPassProviderService(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        var first = service.Build(Pass(), "token-one", "https://app.condotify.test/passe/token-one");
        var second = service.Build(Pass(), "token-two", "https://app.condotify.test/passe/token-two");

        Assert.True(first.GoogleWalletConfigured);
        Assert.True(second.GoogleWalletConfigured);
        Assert.StartsWith("https://pay.google.com/gp/v/save/", second.GoogleWalletUrl, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run it**

Run: `dotnet test CondotifyAPI.Tests --filter Build_ShouldWorkTwiceInARowWithTheSameConfiguredKey`
Expected: PASS (the fix already shipped earlier this session — this step confirms it, it should not fail).

- [ ] **Step 3: Commit**

```bash
git add CondotifyAPI.Tests/DigitalPassProviderServiceTests.cs
git commit -m "test: guard against RSA signature-provider cache disposal regression"
```

---

### Task 2: Fix the missing `LicenseId` on resident visit output

**Files:**
- Modify: `CondotifyAPI/Controllers/ResidentProfileController.cs:104-135` (inline `GET /visits` mapping), `:575-614` (`ToVisitOut` private methods)
- Test: `CondotifyAPI.Tests/ResidentProfileControllerTests.cs`

**Interfaces:**
- Consumes: `AccessVisitDTO.LicenseId` (existing field, already used to filter the query at line 100).
- Produces: `ConciergeVisitOut.LicenseId` now populated — Task 5's mobile UI depends on this to call `IssueResidentDigitalPassAsync`/pass endpoints scoped correctly (the resident endpoints derive `licenseId` from the grant server-side, not from this field, but the mobile `VisitorPassDialog` still reads `Visit.LicenseId` for display/consistency with the existing `ConciergeVisitViewModel` contract).

- [ ] **Step 1: Fix both `ToVisitOut` overloads**

In `CondotifyAPI/Controllers/ResidentProfileController.cs`, in the second `ToVisitOut` overload (around line 578), add `LicenseId` to the object initializer:

```csharp
    private static ConciergeVisitOut ToVisitOut(
        AccessVisitDTO visit,
        string hostName,
        string blockName,
        string unitNumber,
        ResidentAccessCredentialDTO credential) => new()
    {
        Id = visit.Id,
        LicenseId = visit.LicenseId,
        HostResidentId = visit.HostResidentId,
        HostName = hostName,
```

(Everything else in the initializer stays exactly as-is — only the new `LicenseId = visit.LicenseId,` line is added.)

- [ ] **Step 2: Replace the GET /visits inline mapping with `ToVisitOut`**

The `GET api/resident/visits` action (lines 104-135) currently duplicates the same mapping inline instead of calling `ToVisitOut(x)`. Replace the `return Ok(rows.Select(x => new ConciergeVisitOut { ... }))` block with:

```csharp
        return Ok(rows.Select(ToVisitOut));
```

This removes the second, now-fixed-in-only-one-place copy of the bug and matches how `CreateVisit` (lines 166, 290) already calls `ToVisitOut`.

- [ ] **Step 3: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj`
Expected: 0 errors (the `rows` query already `.Include()`s everything `ToVisitOut(AccessVisitDTO)` needs — `HostResident`, `GuestResident.Unit.Block`, `Credential`).

- [ ] **Step 4: Manual harness note**

No automated DB test covers this fix — `ResidentProfileControllerTests.cs`'s existing tests only assert on attributes/routes via reflection (see `Controller_RequiresTheResidentPolicy` etc.), not method bodies or query results, matching this repo's no-DB-test-provider constraint (see Global Constraints). Verify manually once the dev API is running: log in as a resident, call `GET api/resident/visits`, and confirm the JSON response's `licenseId` field is a real GUID, not `00000000-0000-0000-0000-000000000000`.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Controllers/ResidentProfileController.cs
git commit -m "fix: populate LicenseId on resident visit output (was always Guid.Empty)"
```

---

### Task 3: `AllowResidentDigitalPass` license policy flag

**Files:**
- Modify: `CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs`
- Modify: `CondotifyAPI/Data/Administration/LicenseAdministrationDtos.cs`
- Modify: `CondotifyAPI/Controllers/LicenseAdministrationController.cs:111-128,142` (`UpdatePolicy`, `ToPolicy`)
- Modify: `Condotify/Components/LicenseModules/AdministrationModule.razor:31`
- Create: `CondotifyAPI.Infrastructure/Migrations/<generated-timestamp>_AddResidentDigitalPassPolicy.cs` (generated, not hand-written)

**Interfaces:**
- Consumes: nothing new.
- Produces: `LicenseCredentialPolicyDTO.AllowResidentDigitalPass` (bool) — Task 5's resident pass endpoints read this via `DatabaseContext.LicenseCredentialPolicies`.

- [ ] **Step 1: Add the field to the domain DTO**

In `CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs`, add after `RemoveExpiredCredentialsFromDevices`:

```csharp
    public bool AllowResidentDigitalPass { get; set; } = true;
```

- [ ] **Step 2: Add the field to the API contract DTOs**

In `CondotifyAPI/Data/Administration/LicenseAdministrationDtos.cs`, add to `CredentialPolicyOut` (line 47-58), after `RemoveExpiredCredentialsFromDevices`:

```csharp
    public bool AllowResidentDigitalPass { get; set; }
```

`UpdateCredentialPolicyIn : CredentialPolicyOut` inherits it automatically — no change needed there.

- [ ] **Step 3: Wire it through the controller**

In `CondotifyAPI/Controllers/LicenseAdministrationController.cs`, in `UpdatePolicy` (line 111-128), add after `policy.RemoveExpiredCredentialsFromDevices = input.RemoveExpiredCredentialsFromDevices;`:

```csharp
        policy.AllowResidentDigitalPass = input.AllowResidentDigitalPass;
```

In `ToPolicy` (line 142), add `AllowResidentDigitalPass = item.AllowResidentDigitalPass` to the object initializer.

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddResidentDigitalPassPolicy --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI
```

Open the generated file and confirm it contains exactly one `ALTER TABLE "LicenseCredentialPolicies" ADD "AllowResidentDigitalPass" boolean NOT NULL DEFAULT TRUE` (or EF's equivalent `AddColumn<bool>` call with `defaultValue: true`) and nothing else unexpected. If EF generated anything beyond this single column addition, stop and investigate before proceeding (the dev database has real data from earlier in this session).

- [ ] **Step 5: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj`
Expected: 0 errors.

- [ ] **Step 6: Add the portal toggle**

In `Condotify/Components/LicenseModules/AdministrationModule.razor`, add a new `policy-section` right after the "Expiração e limpeza" one (currently the last one, ending at line 31), before line 32's `@if (CanManageSettings)`:

```razor
                            <div class="policy-section"><div class="policy-section-title"><span class="overview-icon blue"><MudIcon Icon="@Icons.Material.Outlined.Wallet" /></span><div><strong>Passe digital</strong><span>Permita que o próprio morador emita o passe da Google/Apple Wallet pelo aplicativo.</span></div></div><div class="policy-switches"><MudSwitch T="bool" @bind-Value="_data.Policy.AllowResidentDigitalPass" Label="Moradores podem emitir passe digital pelo app" Color="Color.Primary" /></div></div>
```

- [ ] **Step 7: Build the portal**

Run: `dotnet build Condotify/Condotify.csproj`
Expected: 0 errors.

- [ ] **Step 8: Manual harness note**

Start the API + Postgres against the dev database, run the migration (it applies automatically on API startup in Development per `README.md`), open the portal's Administration module for a test license, confirm the new "Passe digital" switch appears alongside the other credential-policy switches, toggle it, save, and confirm `GET` on the policy (reload the page) reflects the saved value.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs CondotifyAPI/Data/Administration/LicenseAdministrationDtos.cs CondotifyAPI/Controllers/LicenseAdministrationController.cs Condotify/Components/LicenseModules/AdministrationModule.razor CondotifyAPI.Infrastructure/Migrations/
git commit -m "feat: add per-license toggle for resident-issued digital passes"
```

---

### Task 4: Extract `DigitalPassIssuanceService`

**Files:**
- Create: `CondotifyAPI/Services/Operations/DigitalPassIssuanceService.cs`
- Modify: `CondotifyAPI/Services/Operations/DigitalPassProviderService.cs` (make the URL-resolution helper reusable)
- Modify: `CondotifyAPI/Controllers/DigitalPassesController.cs` (refactor `Issue`/`Revoke`/`Public` to use the new service; remove now-dead private helpers)
- Modify: `CondotifyAPI/Program.cs` (DI registration)

**Interfaces:**
- Consumes: `IDigitalPassProviderService.Build` (existing), `IAppleWalletPassService.IsConfigured` (existing), `DatabaseContext` (existing).
- Produces (for Task 5): `IDigitalPassIssuanceService.IssueAsync(Guid licenseId, Guid visitId, string requestHostRoot, Guid? actorUserId, string actorName, CancellationToken)` returning `DigitalPassIssueResult(DigitalPassIssueOutcome Outcome, DigitalPassViewModel? Pass, string? Error)` where `Outcome` is one of `Success | VisitNotFound | VisitNotEligible | MissingCredential`; and `IDigitalPassIssuanceService.RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken)` returning `DigitalPassRevokeResult(DigitalPassRevokeOutcome Outcome)` where `Outcome` is `Success | NotFound`.

- [ ] **Step 1: Make the public-URL resolution reusable**

In `CondotifyAPI/Services/Operations/DigitalPassProviderService.cs`, change `FirstNonBlank` from `private` to `internal` (it already sits at the bottom of the class, after `Localized`):

```csharp
    internal static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
```

Add a new `internal static` method right below it, in the same class:

```csharp
    internal static string ResolvePublicUrl(IConfiguration configuration, string requestHostRoot, string token)
    {
        var root = FirstNonBlank(
            configuration["DigitalPass:PublicAppUrl"],
            Environment.GetEnvironmentVariable("CONDOTIFY_PUBLIC_APP_URL"),
            requestHostRoot);
        return $"{root!.TrimEnd('/')}/passe/{Uri.EscapeDataString(token)}";
    }
```

- [ ] **Step 2: Create the result types and service**

Create `CondotifyAPI/Services/Operations/DigitalPassIssuanceService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Operations;
using CondotifyAPI.Domain.Enums.Invitation;
using CondotifyAPI.Infrastructure;
using Condotify.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Operations;

public enum DigitalPassIssueOutcome { Success, VisitNotFound, VisitNotEligible, MissingCredential }
public sealed record DigitalPassIssueResult(DigitalPassIssueOutcome Outcome, DigitalPassViewModel? Pass, string? Error);

public enum DigitalPassRevokeOutcome { Success, NotFound }
public sealed record DigitalPassRevokeResult(DigitalPassRevokeOutcome Outcome);

public interface IDigitalPassIssuanceService
{
    Task<DigitalPassIssueResult> IssueAsync(Guid licenseId, Guid visitId, string requestHostRoot, Guid? actorUserId, string actorName, CancellationToken cancellationToken);
    Task<DigitalPassRevokeResult> RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken cancellationToken);
}

/// <summary>
/// Shared by the staff portal (DigitalPassesController) and the resident
/// mobile app (ResidentProfileController) so a bug fixed here - like the two
/// found while building the first caller - only needs fixing once.
/// </summary>
public sealed class DigitalPassIssuanceService(
    DatabaseContext context,
    IDigitalPassProviderService providers,
    IAppleWalletPassService appleWallet,
    IConfiguration configuration) : IDigitalPassIssuanceService
{
    public async Task<DigitalPassIssueResult> IssueAsync(Guid licenseId, Guid visitId, string requestHostRoot, Guid? actorUserId, string actorName, CancellationToken cancellationToken)
    {
        var visit = await context.AccessVisits
            .Include(x => x.License).Include(x => x.Credential)
            .Include(x => x.HostResident).ThenInclude(x => x.Unit).ThenInclude(x => x.Block)
            .FirstOrDefaultAsync(x => x.Id == visitId && x.LicenseId == licenseId, cancellationToken);
        if (visit is null) return new DigitalPassIssueResult(DigitalPassIssueOutcome.VisitNotFound, null, null);
        if (visit.ValidTo <= DateTime.UtcNow || visit.Status is not (AccessVisitStatusEnum.Scheduled or AccessVisitStatusEnum.CheckedIn))
            return new DigitalPassIssueResult(DigitalPassIssueOutcome.VisitNotEligible, null, "A visita nao esta valida para emissao de passe.");
        if (string.IsNullOrWhiteSpace(visit.Credential.Identifier))
            return new DigitalPassIssueResult(DigitalPassIssueOutcome.MissingCredential, null, "A visita ainda nao possui uma credencial de acesso.");

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId, cancellationToken);
        if (pass is null)
        {
            pass = new DigitalPassDTO { Id = Guid.NewGuid(), LicenseId = licenseId, VisitId = visitId, CreatedAt = now };
            context.DigitalPasses.Add(pass);
        }
        pass.TokenHash = Hash(token); pass.Status = DigitalPassStatusEnum.Active; pass.IssuedAt = now;
        pass.ExpiresAt = visit.ValidTo; pass.RevokedAt = null; pass.UpdatedAt = now;
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Issued", Status = "Success", Summary = $"Passe digital emitido para {visit.VisitorName}.",
            DetailsJson = JsonSerializer.Serialize(new { visitId, visit.ValidFrom, visit.ValidTo }),
            UserId = actorUserId, UserName = actorName, CreatedAt = now
        });
        await context.SaveChangesAsync(cancellationToken);
        pass.Visit = visit; pass.License = visit.License;

        var publicUrl = DigitalPassProviderService.ResolvePublicUrl(configuration, requestHostRoot, token);
        var output = providers.Build(pass, token, publicUrl);
        if (appleWallet.IsConfigured)
        {
            output.AppleWalletUrl = $"{requestHostRoot}/api/public/passes/{Uri.EscapeDataString(token)}/apple";
            output.AppleWalletConfigured = true;
        }
        return new DigitalPassIssueResult(DigitalPassIssueOutcome.Success, output, null);
    }

    public async Task<DigitalPassRevokeResult> RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken cancellationToken)
    {
        var pass = await context.DigitalPasses.FirstOrDefaultAsync(x => x.VisitId == visitId && x.LicenseId == licenseId, cancellationToken);
        if (pass is null) return new DigitalPassRevokeResult(DigitalPassRevokeOutcome.NotFound);
        pass.Status = DigitalPassStatusEnum.Revoked; pass.RevokedAt = DateTime.UtcNow; pass.UpdatedAt = DateTime.UtcNow;
        pass.TokenHash = Hash($"revoked:{pass.Id:N}:{RandomNumberGenerator.GetHexString(16)}");
        context.AccessOperationAudits.Add(new AccessOperationAuditDTO
        {
            Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "DigitalPass", EntityId = pass.Id,
            Action = "Revoked", Status = "Success", Summary = "Passe digital revogado.", DetailsJson = "{}",
            UserId = actorUserId, UserName = actorName, CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync(cancellationToken);
        return new DigitalPassRevokeResult(DigitalPassRevokeOutcome.Success);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
```

- [ ] **Step 3: Register it in DI**

In `CondotifyAPI/Program.cs`, add right after the existing line `builder.Services.AddScoped<IAppleWalletPassService, AppleWalletPassService>();` (line 214):

```csharp
builder.Services.AddScoped<IDigitalPassIssuanceService, DigitalPassIssuanceService>();
```

- [ ] **Step 4: Refactor `DigitalPassesController` to use the service**

In `CondotifyAPI/Controllers/DigitalPassesController.cs`:

1. Add `issuance` to the primary constructor parameter list (after `providers`):

```csharp
public sealed class DigitalPassesController(
    DatabaseContext context,
    ILicenseAuthorizationService authorization,
    IDigitalPassProviderService providers,
    IDigitalPassIssuanceService issuance,
    IAppleWalletPassService appleWallet,
    IConfiguration configuration) : ControllerBase
```

2. Replace the entire body of `Issue` (currently lines 26-60) with:

```csharp
    [Authorize]
    [HttpPost("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Issue(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var result = await issuance.IssueAsync(licenseId, visitId, $"{Request.Scheme}://{Request.Host}", CurrentUserId(), CurrentActor(), HttpContext.RequestAborted);
        return result.Outcome switch
        {
            DigitalPassIssueOutcome.VisitNotFound => NotFound(),
            DigitalPassIssueOutcome.Success => Ok(result.Pass),
            _ => Conflict(new { Errors = result.Error })
        };
    }
```

3. Replace the entire body of `Revoke` (currently lines 63-79) with:

```csharp
    [Authorize]
    [HttpDelete("api/access/licenses/{licenseId:guid}/visits/{visitId:guid}/pass")]
    public async Task<IActionResult> Revoke(Guid licenseId, Guid visitId)
    {
        if (!await authorization.HasPermissionAsync(User, licenseId, LicensePermissionEnum.ManagePeople, HttpContext.RequestAborted)) return Forbid();
        var result = await issuance.RevokeAsync(licenseId, visitId, CurrentUserId(), CurrentActor(), HttpContext.RequestAborted);
        return result.Outcome == DigitalPassRevokeOutcome.NotFound ? NotFound() : NoContent();
    }
```

4. In the `Public` action (the `[AllowAnonymous] [HttpGet("api/public/passes/{token}")]` one, currently around line 82-98), replace its call to the private `PublicUrl(token)` helper with the shared static one:

```csharp
        return Ok(ToOutput(pass, token, DigitalPassProviderService.ResolvePublicUrl(configuration, $"{Request.Scheme}://{Request.Host}", token)));
```

(Applies to both places `PublicUrl(token)` is called inside `Public` — there is only one, at the `return Ok(...)` line.)

5. Delete the now-unused private `PublicUrl(string token)` method (lines 128-133) and the private `Hash(string value)` method (was at line 144) — both are fully replaced by the shared service/static helper. Leave `ToOutput` in place (still used by `Public`).

- [ ] **Step 5: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj`
Expected: 0 errors. If `Hash` or `PublicUrl` are still referenced anywhere in the file, the build will fail with an unused-method warning is fine but a missing-reference error means Step 4.5 removed something still in use — check the `Public` action didn't call the old `Hash`.

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test CondotifyAPI.Tests`
Expected: all pass, including the new Task 1 regression test.

- [ ] **Step 7: Manual harness note**

With the dev API running, log in as staff (`teste@condotify.local` / `Teste@123`), call `POST api/access/licenses/{licenseId}/visits/{visitId}/pass` twice in a row against a real visit (same reproduction used earlier this session), confirm both return `200` with a populated `googleWalletUrl` and no `500`. This is the same check already done manually earlier in the session — now codified as a required step so it's not skipped after this refactor.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI/Services/Operations/DigitalPassIssuanceService.cs CondotifyAPI/Services/Operations/DigitalPassProviderService.cs CondotifyAPI/Controllers/DigitalPassesController.cs CondotifyAPI/Program.cs
git commit -m "refactor: extract DigitalPassIssuanceService shared by staff and resident callers"
```

---

### Task 5: Resident-scoped issue/revoke endpoints

**Files:**
- Modify: `CondotifyAPI/Controllers/ResidentProfileController.cs`
- Test: `CondotifyAPI.Tests/ResidentProfileControllerTests.cs`

**Interfaces:**
- Consumes: `IDigitalPassIssuanceService` (Task 4), `IResidentAuthorizationService.GetGrantAsync` (existing, returns `ResidentAccessGrant(Guid ResidentId, Guid LicenseId, IReadOnlyCollection<Guid> UnitIds, ResidentAccessTypeEnum AccessType, bool IsResponsible)`), `DatabaseContext.LicenseCredentialPolicies` (Task 3's new column).
- Produces (for Task 6): `POST api/resident/visits/{visitId:guid}/pass` returning `DigitalPassViewModel` (200), `DELETE api/resident/visits/{visitId:guid}/pass` returning 204.

- [ ] **Step 1: Add `IDigitalPassIssuanceService` to the controller's dependencies**

`ResidentProfileController` uses an old-style constructor (not primary-constructor). In `CondotifyAPI/Controllers/ResidentProfileController.cs`, add a field and constructor parameter:

```csharp
    private readonly DatabaseContext _context;
    private readonly IResidentAuthorizationService _authorization;
    private readonly IDigitalPassIssuanceService _issuance;

    public ResidentProfileController(DatabaseContext context, IResidentAuthorizationService authorization, IDigitalPassIssuanceService issuance)
    {
        _context = context;
        _authorization = authorization;
        _issuance = issuance;
    }
```

Add `using CondotifyAPI.Services.Operations;` to the top of the file if not already present (check the existing `using` block first — it is not currently imported).

- [ ] **Step 2: Add the Issue action**

Add near the end of the visits-related actions (right after `Visits`, before the next unrelated action):

```csharp
    [HttpPost("visits/{visitId:guid}/pass")]
    public async Task<IActionResult> IssuePass(Guid visitId, CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var visit = await _context.AccessVisits.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == visitId && x.LicenseId == grant.LicenseId, cancellationToken);
        if (visit is null) return NotFound();
        if (visit.HostResidentId != grant.ResidentId) return Forbid();

        var policy = await _context.LicenseCredentialPolicies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.LicenseId == grant.LicenseId, cancellationToken);
        if (policy is not null && !policy.AllowResidentDigitalPass) return Forbid();

        var resident = await _context.Residents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == grant.ResidentId, cancellationToken);
        var result = await _issuance.IssueAsync(grant.LicenseId, visitId, $"{Request.Scheme}://{Request.Host}", null, resident?.Name ?? "Morador", cancellationToken);
        return result.Outcome switch
        {
            DigitalPassIssueOutcome.VisitNotFound => NotFound(),
            DigitalPassIssueOutcome.Success => Ok(result.Pass),
            _ => Conflict(new { Errors = result.Error })
        };
    }
```

Note the missing `policy` row is treated as "allowed" (`AllowResidentDigitalPass` defaults to `true` for licenses that never touched the Administration module's credential-policy form) — matches `LicenseAdministrationController.GetPolicyAsync`'s own lazy-creation-with-defaults behavior.

- [ ] **Step 3: Add the Revoke action**

```csharp
    [HttpDelete("visits/{visitId:guid}/pass")]
    public async Task<IActionResult> RevokePass(Guid visitId, CancellationToken cancellationToken)
    {
        var grant = await _authorization.GetGrantAsync(User, cancellationToken);
        if (grant is null) return Forbid();

        var visit = await _context.AccessVisits.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == visitId && x.LicenseId == grant.LicenseId, cancellationToken);
        if (visit is null) return NotFound();
        if (visit.HostResidentId != grant.ResidentId) return Forbid();

        var resident = await _context.Residents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == grant.ResidentId, cancellationToken);
        var result = await _issuance.RevokeAsync(grant.LicenseId, visitId, null, resident?.Name ?? "Morador", cancellationToken);
        return result.Outcome == DigitalPassRevokeOutcome.NotFound ? NotFound() : NoContent();
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj`
Expected: 0 errors.

- [ ] **Step 5: Write the route/authorization regression tests**

Add to `CondotifyAPI.Tests/ResidentProfileControllerTests.cs`, following the exact style of `ResidentCommandRoutes_UseTheExpectedHttpVerbs`:

```csharp
    [Theory]
    [InlineData(nameof(ResidentProfileController.IssuePass))]
    [InlineData(nameof(ResidentProfileController.RevokePass))]
    public void DigitalPassCommands_DoNotOverrideTheResidentPolicy(string actionName)
    {
        var action = typeof(ResidentProfileController).GetMethods()
            .Single(x => x.Name == actionName);

        Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        Assert.Empty(action.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
    }

    [Fact]
    public void DigitalPassRoutes_UseExpectedVerbsAndTemplate()
    {
        var issue = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.IssuePass));
        var revoke = typeof(ResidentProfileController).GetMethod(nameof(ResidentProfileController.RevokePass));

        Assert.Equal("visits/{visitId:guid}/pass", Assert.Single(issue!.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>()).Template);
        Assert.Equal("visits/{visitId:guid}/pass", Assert.Single(revoke!.GetCustomAttributes(typeof(HttpDeleteAttribute), false).Cast<HttpDeleteAttribute>()).Template);
    }
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test CondotifyAPI.Tests --filter "ResidentProfileControllerTests"`
Expected: all pass, including the two new ones.

- [ ] **Step 7: Manual harness note**

With the dev API running: log in as a resident (mobile app or a direct `POST api/resident/auth/login`-equivalent — check `ResidentLoginTests.cs` for the exact resident login route if unsure), find a visit that resident hosts, call `POST api/resident/visits/{visitId}/pass` and confirm `200` with `googleWalletUrl` populated; call it again and confirm still `200` (idempotent re-issue); call `DELETE` and confirm `204`; call `POST` for a visit **not** hosted by that resident and confirm `403`.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI/Controllers/ResidentProfileController.cs CondotifyAPI.Tests/ResidentProfileControllerTests.cs
git commit -m "feat: let residents issue/revoke their own visit's digital pass"
```

---

### Task 6: Mobile API client methods

**Files:**
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`

**Interfaces:**
- Consumes: `api/resident/visits/{visitId}/pass` (Task 5).
- Produces (for Task 7): `IssueResidentDigitalPassAsync(Guid visitId, CancellationToken)`, `RevokeResidentDigitalPassAsync(Guid visitId, CancellationToken)`.

- [ ] **Step 1: Add the two client methods**

In `Condotify.ApiClient/CondotifyApiClient.cs`, add right after the existing `RevokeDigitalPassAsync` (line 1209):

```csharp
    public Task<ApiResult<DigitalPassViewModel>> IssueResidentDigitalPassAsync(Guid visitId, CancellationToken cancellationToken = default) =>
        SendForAsync<DigitalPassViewModel>(HttpMethod.Post, $"api/resident/visits/{visitId}/pass", new { }, cancellationToken);

    public Task<ApiResult<bool>> RevokeResidentDigitalPassAsync(Guid visitId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/resident/visits/{visitId}/pass", cancellationToken);
```

- [ ] **Step 2: Build**

Run: `dotnet build Condotify.ApiClient/Condotify.ApiClient.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add Condotify.ApiClient/CondotifyApiClient.cs
git commit -m "feat: add ApiClient methods for resident digital pass issue/revoke"
```

---

### Task 7: Mobile UI — `VisitorPassDialog.razor`

**Files:**
- Modify: `Condotify.Mobile/Components/Dialogs/VisitorPassDialog.razor`
- Modify: `Condotify.Mobile/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `CondotifyApiClient.IssueResidentDigitalPassAsync`/`RevokeResidentDigitalPassAsync` (Task 6).

- [ ] **Step 1: Add the wallet section to the dialog**

Replace the full content of `Condotify.Mobile/Components/Dialogs/VisitorPassDialog.razor` with:

```razor
@inject MobileDeviceActions DeviceActions
@inject CondotifyApiClient Api
@inject ISnackbar Snackbar

<MudDialog Class="mobile-dialog visitor-pass-dialog">
    <TitleContent>
        <div class="mobile-dialog-title">
            <MudIcon Icon="@Icons.Material.Outlined.ConfirmationNumber" Color="Color.Primary" />
            <div><MudText Typo="Typo.h6">Convite de acesso</MudText><MudText Typo="Typo.caption">Apresente este QR Code na portaria.</MudText></div>
        </div>
    </TitleContent>
    <DialogContent>
        <div class="visitor-pass-hero">
            <span class="visitor-pass-brand"><MudIcon Icon="@Icons.Material.Outlined.Apartment" /> Condotify</span>
            <div class="visitor-pass-person"><small>VISITANTE</small><strong>@Visit.VisitorName</strong><span>@Visit.BlockName @(!string.IsNullOrWhiteSpace(Visit.UnitNumber) ? $"· Unidade {Visit.UnitNumber}" : string.Empty)</span></div>
            @if (!string.IsNullOrWhiteSpace(QrDataUri))
            {
                <div class="visitor-pass-qr"><img src="@QrDataUri" alt="QR Code do convite de @Visit.VisitorName" /></div>
            }
            else
            {
                <MudAlert Severity="Severity.Warning" Variant="Variant.Outlined">O QR Code ainda não foi disponibilizado.</MudAlert>
            }
            <div class="visitor-pass-validity"><span><small>ENTRADA A PARTIR DE</small><strong>@Visit.ValidFrom.ToLocalTime().ToString("dd/MM · HH:mm")</strong></span><span><small>VÁLIDO ATÉ</small><strong>@Visit.ValidTo.ToLocalTime().ToString("dd/MM · HH:mm")</strong></span></div>
            <code>@Visit.CredentialCode</code>
        </div>
        <MudAlert Severity="Severity.Info" Variant="Variant.Text" Dense="true">O convite é pessoal. A portaria valida horário, situação e limite de utilizações.</MudAlert>

        <section class="visitor-pass-wallet">
            <div class="visitor-pass-wallet-heading"><MudIcon Icon="@Icons.Material.Outlined.Wallet" /><div><strong>Passe digital Condotify</strong><small>Adicione esta autorização a uma carteira compatível no seu celular.</small></div></div>
            @if (_pass is null)
            {
                <MudButton Variant="Variant.Filled" Color="Color.Primary" FullWidth="true" StartIcon="@Icons.Material.Outlined.AddCard" Disabled="_issuing" OnClick="IssuePassAsync">@(_issuing ? "Emitindo..." : "Adicionar à carteira")</MudButton>
            }
            else
            {
                <div class="visitor-pass-wallet-buttons">
                    @if (_pass.GoogleWalletConfigured) { <MudButton Href="@_pass.GoogleWalletUrl" Target="_blank" Variant="Variant.Filled" Color="Color.Dark" StartIcon="@Icons.Material.Outlined.Wallet">Google Wallet</MudButton> }
                    @if (_pass.AppleWalletConfigured) { <MudButton Href="@_pass.AppleWalletUrl" Target="_blank" Variant="Variant.Filled" Color="Color.Dark" StartIcon="@Icons.Material.Outlined.PhoneIphone">Apple Wallet</MudButton> }
                </div>
                <MudButton Variant="Variant.Text" Color="Color.Error" Size="Size.Small" StartIcon="@Icons.Material.Outlined.LinkOff" Disabled="_revoking" OnClick="RevokePassAsync">@(_revoking ? "Revogando..." : "Revogar passe")</MudButton>
            }
        </section>
    </DialogContent>
    <DialogActions>
        <MudButton Variant="Variant.Text" OnClick="Close">Fechar</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Share" Disabled="@string.IsNullOrWhiteSpace(Visit.CredentialCode)" OnClick="ShareAsync">Compartilhar convite</MudButton>
    </DialogActions>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public ConciergeVisitViewModel Visit { get; set; } = null!;
    private string QrDataUri => QrCodeRenderer.ToPngDataUri(Visit.CredentialCode);
    private DigitalPassViewModel? _pass;
    private bool _issuing;
    private bool _revoking;

    private Task ShareAsync() => DeviceActions.ShareTextAsync($"Convite para {Visit.VisitorName}",
        $"Convite Condotify\nVisitante: {Visit.VisitorName}\nLocal: {Visit.BlockName} / {Visit.UnitNumber}\nVálido de {Visit.ValidFrom.ToLocalTime():dd/MM HH:mm} até {Visit.ValidTo.ToLocalTime():dd/MM HH:mm}\nCódigo: {Visit.CredentialCode}");
    private void Close() => Dialog.Close();

    private async Task IssuePassAsync()
    {
        _issuing = true;
        var result = await Api.IssueResidentDigitalPassAsync(Visit.Id);
        _issuing = false;
        if (!result.Success) { Snackbar.Add(result.Error ?? "Não foi possível emitir o passe.", Severity.Error); return; }
        _pass = result.Value;
        Snackbar.Add("Passe digital emitido.", Severity.Success);
    }

    private async Task RevokePassAsync()
    {
        _revoking = true;
        var result = await Api.RevokeResidentDigitalPassAsync(Visit.Id);
        _revoking = false;
        if (!result.Success) { Snackbar.Add(result.Error ?? "Não foi possível revogar.", Severity.Error); return; }
        _pass = null;
        Snackbar.Add("Passe revogado.", Severity.Success);
    }
}
```

(Changes from the original: adds `@inject CondotifyApiClient Api` and `@inject ISnackbar Snackbar`; adds the new `<section class="visitor-pass-wallet">` block; adds `_pass`/`_issuing`/`_revoking` fields and `IssuePassAsync`/`RevokePassAsync` methods. Everything else — header, QR hero, share button/`ShareAsync` — is unchanged from today.)

- [ ] **Step 2: Add the CSS**

In `Condotify.Mobile/wwwroot/css/app.css`, add right after the existing `.visitor-pass-actions { flex-wrap: nowrap; }` rule (around line 906):

```css
.visitor-pass-wallet { display: grid; gap: 12px; margin-top: 16px; padding: 16px; border-radius: 20px; background: var(--surface); box-shadow: 0 8px 22px rgba(15, 37, 78, .08); }
.visitor-pass-wallet-heading { display: flex; align-items: flex-start; gap: 10px; }
.visitor-pass-wallet-heading strong { display: block; font-size: .92rem; }
.visitor-pass-wallet-heading small { color: var(--text-muted); font-size: .74rem; }
.visitor-pass-wallet-buttons { display: grid; gap: 8px; }
```

- [ ] **Step 3: Build**

Run: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android35.0`
Expected: 0 errors.

- [ ] **Step 4: Manual harness note**

Publish and install the APK on a physical device the way it was done earlier this session (`dotnet publish Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android35.0 -c Debug -p:AndroidPackageFormat=apk -p:CondotifyApiBaseUrl=http://<lan-ip>:5093 -p:EmbedAssembliesIntoApk=true`, then `adb install -r`). Log in as a resident, open a hosted visit's pass dialog, tap "Adicionar à carteira", confirm the Google/Apple Wallet buttons appear and open a working link, tap "Revogar passe", confirm the buttons disappear and the button reverts to "Adicionar à carteira".

- [ ] **Step 5: Commit**

```bash
git add Condotify.Mobile/Components/Dialogs/VisitorPassDialog.razor Condotify.Mobile/wwwroot/css/app.css
git commit -m "feat: let residents add/revoke their visit's digital wallet pass from the app"
```

---

### Task 8: Full solution verification

**Files:** none (verification only)

- [ ] **Step 1: Full build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors (the pre-existing unrelated iOS `MSB3030` long-path issue on this machine, if it resurfaces, is not a regression from this plan — see earlier session notes).

- [ ] **Step 2: Full test suite**

Run: `dotnet test CondotifyAPI.Tests`
Expected: all pass, including every test added in Tasks 1 and 5.

- [ ] **Step 3: Confirm no regression in the staff flow**

Run the Task 4 Step 7 manual check again (staff issues a pass twice via the portal) — this is the one existing flow every task in this plan touches indirectly (Task 4 refactors it, Task 3 adds a column near it). Confirm it still behaves exactly as before this plan started.

- [ ] **Step 4: Commit** (only if any of the above steps required fixes not already committed)
