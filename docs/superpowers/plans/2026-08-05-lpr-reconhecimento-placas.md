# LPR (Reconhecimento de Placas na Cancela) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a `AccessControlDevice` (cancela) recognize the plate of an approaching vehicle from a snapshot of an existing `CFTVDevice` camera, match it against a registered `VehicleDTO`, and either auto-open the gate or raise an alert for the porter — reusing every piece of infrastructure that already exists (`CftvSnapshotService`, `AcessControlService.OpenDoorAsync`, `RequireLicensePermission`) and adding only what is genuinely missing (vehicle CRUD, a camera↔gate link, a self-hosted OCR service, and the orchestration between them).

**Architecture:** A new stateless Python/FastAPI microservice (`lpr-ocr`) does the actual plate recognition from a JPEG and is never trusted with business logic. A new `.NET` `BackgroundService` (`LprPollingService`) polls each LPR-enabled device, pulls a snapshot via the existing `CftvSnapshotService`, calls the OCR service over HTTP, and hands the result to a pure decision function (`LprDecisionEngine`) that decides whether to open the gate, log a detection, or raise an alert — writing everything to a new `VehicleAccessAudit` table.

**Tech Stack:** ASP.NET Core 8 / EF Core (Postgres) for the orchestration side; Python 3.11 / FastAPI / `fast-alpr` for the OCR microservice; xUnit for .NET tests, `pytest` for the Python tests.

## Global Constraints

- Reuse `CftvSnapshotService.FetchAsync` and `AcessControlService.OpenDoorAsync` as-is — do not duplicate or fork them.
- The OCR engine is self-hosted (no paid cloud API) and never persists images — it receives bytes, returns `{plate, confidence}`, forgets.
- The gate never opens on a low-confidence or failed read — any failure (camera, OCR service, no match) falls back to "closed, existing QR/tag/card credential still works."
- The LPR mode (`DetectionOnly` / `AutoOpen`) is configured **per device**, not per license.
- Pure/branchy logic (plate normalization, open/alert/log decision) lives in `internal static` classes in `CondotifyAPI` and is unit-tested via the existing `InternalsVisibleTo("CondotifyAPI.Tests")` (`CondotifyAPI/Properties/AssemblyInfo.cs:3`) — do not introduce a new test-visibility mechanism.
- This codebase's test project (`CondotifyAPI.Tests`) has no EF InMemory provider and no `WebApplicationFactory` harness — DB-touching code (controllers, repository-style services) is verified by `dotnet build` + the manual harness note in each task, not by unit test. Do not invent a testing mechanism the project doesn't have.
- Migrations: `dotnet ef migrations add <Nome> --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`.
- Full solution build check: `dotnet build Condotify.sln`.
- Spec of record: `docs/superpowers/specs/2026-08-05-lpr-reconhecimento-placas-design.md`.

---

### Task 1: Schema — permissions, LPR device fields, vehicle audit table

**Files:**
- Modify: `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`
- Create: `CondotifyAPI.Domain/Enums/Equipments/LprModeEnum.cs`
- Modify: `CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs`
- Modify: `CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/AccessControlDeviceConfiguration.cs`
- Create: `CondotifyAPI.Domain/DTO/Vehicle/VehicleAccessAuditDTO.cs`
- Create: `CondotifyAPI.Infrastructure/ContextConfiguration/Vehicle/VehicleAccessAuditConfiguration.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/Resident/DatabaseContext.Resident.cs`
- Create (generated): `CondotifyAPI.Infrastructure/Migrations/<timestamp>_AddLprSupport.cs`

**Interfaces:**
- Produces: `LicensePermissionEnum.ViewVehicles`, `LicensePermissionEnum.ManageVehicles` (used by Task 2); `LprModeEnum { DetectionOnly = 0, AutoOpen = 1 }` (used by Tasks 3, 5, 8); `AccessControlDeviceDTO.LprCameraId (Guid?)`, `.LprCameraChannel (int?)`, `.LprMode (LprModeEnum?)` (used by Tasks 3, 8); `VehicleAccessAuditDTO` with `VehicleAccessAuditAction { NoRead, Opened, DetectedOnly, AlertRaised }` and `DatabaseContext.VehicleAccessAudits` (used by Task 8).

- [ ] **Step 1: Add the two new permission bits**

In `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`, insert before `All` and update the mask (two new bits pushes the top of the mask from 29 to 31):

```csharp
    ViewEmergency = 1L << 27,
    ManageEmergency = 1L << 28,
    ViewVehicles = 1L << 29,
    ManageVehicles = 1L << 30,
    All = (1L << 31) - 1
```

- [ ] **Step 2: Add `LprModeEnum`**

Create `CondotifyAPI.Domain/Enums/Equipments/LprModeEnum.cs`:

```csharp
namespace CondotifyAPI.Domain.Enums.Equipments;

public enum LprModeEnum
{
    DetectionOnly = 0,
    AutoOpen = 1
}
```

- [ ] **Step 3: Add LPR fields to `AccessControlDeviceDTO`**

In `CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs`, add `using CondotifyAPI.Domain.Enums.Equipments;` at the top and, after `DiscoveredPortalsJson`:

```csharp
        public Guid? LprCameraId { get; set; }
        public int? LprCameraChannel { get; set; }
        public LprModeEnum? LprMode { get; set; }
```

- [ ] **Step 4: Configure the new columns and the camera FK**

In `CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/AccessControlDeviceConfiguration.cs`, after the `DiscoveredPortalsJson` line, add:

```csharp
            builder.Property(d => d.LprMode);

            builder.HasOne<CFTVDeviceDTO>()
                .WithMany()
                .HasForeignKey(d => d.LprCameraId)
                .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 5: Create `VehicleAccessAuditDTO`**

Create `CondotifyAPI.Domain/DTO/Vehicle/VehicleAccessAuditDTO.cs`:

```csharp
using CondotifyAPI.Domain.DTO.Equipments;

namespace CondotifyAPI.Domain.DTO.Vehicle;

public enum VehicleAccessAuditAction
{
    NoRead = 0,
    Opened = 1,
    DetectedOnly = 2,
    AlertRaised = 3
}

public class VehicleAccessAuditDTO
{
    public Guid Id { get; set; }
    public Guid AccessControlDeviceId { get; set; }
    public AccessControlDeviceDTO Device { get; set; } = null!;
    public string? PlateRead { get; set; }
    public double Confidence { get; set; }
    public Guid? MatchedVehicleId { get; set; }
    public VehicleAccessAuditAction Action { get; set; }
    public string? SnapshotReference { get; set; }
    public DateTime Timestamp { get; set; }
}
```

- [ ] **Step 6: Configure `VehicleAccessAuditDTO`**

Create `CondotifyAPI.Infrastructure/ContextConfiguration/Vehicle/VehicleAccessAuditConfiguration.cs`:

```csharp
using CondotifyAPI.Domain.DTO.Vehicle;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Vehicle;

public sealed class VehicleAccessAuditConfiguration : IEntityTypeConfiguration<VehicleAccessAuditDTO>
{
    public void Configure(EntityTypeBuilder<VehicleAccessAuditDTO> builder)
    {
        builder.ToTable("VehicleAccessAudits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlateRead).HasMaxLength(10);
        builder.Property(x => x.SnapshotReference).HasMaxLength(300);
        builder.Property(x => x.Timestamp).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasOne(x => x.Device)
            .WithMany()
            .HasForeignKey(x => x.AccessControlDeviceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.AccessControlDeviceId, x.Timestamp });
        builder.HasIndex(x => x.MatchedVehicleId);
    }
}
```

- [ ] **Step 7: Register the DbSet and the configuration**

In `CondotifyAPI.Infrastructure/DatabaseContext/Resident/DatabaseContext.Resident.cs`, add the DbSet next to `Vehicles` and apply the configuration next to `VehicleConfiguration`:

```csharp
    public DbSet<VehicleDTO> Vehicles { get; set; }
    public DbSet<VehicleAccessAuditDTO> VehicleAccessAudits { get; set; }
```

```csharp
        builder.ApplyConfiguration(new VehicleConfiguration());
        builder.ApplyConfiguration(new VehicleAccessAuditConfiguration());
```

- [ ] **Step 8: Build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors.

- [ ] **Step 9: Generate the migration**

Run: `dotnet ef migrations add AddLprSupport --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`
Expected: a new migration file. `ViewVehicles`/`ManageVehicles` are enum values only, so they produce no schema change by themselves; the migration's actual content is the three new `AccessControlDevices` columns (`LprCameraId`, `LprCameraChannel`, `LprMode`), the FK to `CFTVDevices`, and the new `VehicleAccessAudits` table. Inspect the generated `Up()` to confirm all three device columns are nullable (no data migration needed for existing rows).

- [ ] **Step 10: Commit**

```bash
git add CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs CondotifyAPI.Domain/Enums/Equipments/LprModeEnum.cs CondotifyAPI.Domain/DTO/Equipments/AccessControlDeviceDTO.cs CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/AccessControlDeviceConfiguration.cs CondotifyAPI.Domain/DTO/Vehicle/VehicleAccessAuditDTO.cs CondotifyAPI.Infrastructure/ContextConfiguration/Vehicle/VehicleAccessAuditConfiguration.cs CondotifyAPI.Infrastructure/DatabaseContext/Resident/DatabaseContext.Resident.cs CondotifyAPI.Infrastructure/Migrations
git commit -m "feat: add LPR schema (device fields, vehicle audit, permissions)"
```

---

### Task 2: Vehicle CRUD (`VehicleController`)

**Files:**
- Create: `CondotifyAPI/Data/Vehicles/VehicleDtos.cs`
- Create: `CondotifyAPI/Controllers/VehicleController.cs`

**Interfaces:**
- Consumes: `LicensePermissionEnum.ViewVehicles`/`ManageVehicles` (Task 1), `VehicleDTO` (existing, `CondotifyAPI.Domain.DTO.Vehicle`), `RequireLicensePermissionAttribute` (existing, `CondotifyAPI.Services.Authorization`).
- Produces: `POST/GET api/access/licenses/{licenseId}/units/{unitId}/vehicles`, `PATCH/DELETE api/access/licenses/{licenseId}/vehicles/{vehicleId}` — used later only by the mobile/web consumer (out of scope here), and by Task 8's `IVehicleLookupService` indirectly via the same `Vehicles` table.

- [ ] **Step 1: Add the request/response DTOs**

Create `CondotifyAPI/Data/Vehicles/VehicleDtos.cs`:

```csharp
namespace CondotifyAPI.Data.Vehicles;

public sealed class VehicleCreateIn
{
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = "Carro";
    public string TagIdentifier { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
}

public sealed class VehicleUpdateIn
{
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = "Carro";
    public string TagIdentifier { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class VehicleOut
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string TagIdentifier { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 2: Write the controller**

Create `CondotifyAPI/Controllers/VehicleController.cs`:

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Vehicles;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}")]
public sealed class VehicleController(DatabaseContext context) : ControllerBase
{
    [HttpGet("units/{unitId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ViewVehicles)]
    public async Task<IActionResult> ListByUnit(Guid licenseId, Guid unitId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (!await UnitBelongsAsync(licenseId, unitId)) return NotFound();

        var vehicles = await context.Vehicles
            .AsNoTracking()
            .Where(v => v.UnitId == unitId)
            .OrderBy(v => v.Plate)
            .ToListAsync();

        return Ok(vehicles.Select(ToOut));
    }

    [HttpPost("units/{unitId:guid}/vehicles")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Create(Guid licenseId, Guid unitId, [FromBody] VehicleCreateIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (!await UnitBelongsAsync(licenseId, unitId)) return NotFound();

        var plate = (input.Plate ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(plate))
            return BadRequest(new { Result = "InvalidPlate", Errors = "Informe a placa do veiculo." });

        var alreadyExists = await context.Vehicles.AsNoTracking()
            .AnyAsync(v => v.UnitId == unitId && v.Plate == plate);
        if (alreadyExists)
            return Conflict(new { Result = "DuplicatePlate", Errors = "Esta unidade ja possui um veiculo com essa placa." });

        var now = DateTime.UtcNow;
        var vehicle = new VehicleDTO
        {
            Id = Guid.NewGuid(),
            UnitId = unitId,
            ResidentId = input.ResidentId,
            Plate = plate,
            Brand = input.Brand ?? string.Empty,
            Model = input.Model ?? string.Empty,
            Color = input.Color ?? string.Empty,
            Type = string.IsNullOrWhiteSpace(input.Type) ? "Carro" : input.Type,
            TagIdentifier = input.TagIdentifier ?? string.Empty,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return Created($"api/access/licenses/{licenseId}/vehicles/{vehicle.Id}", ToOut(vehicle));
    }

    [HttpPatch("vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Update(Guid licenseId, Guid vehicleId, [FromBody] VehicleUpdateIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        vehicle.Brand = input.Brand ?? string.Empty;
        vehicle.Model = input.Model ?? string.Empty;
        vehicle.Color = input.Color ?? string.Empty;
        vehicle.Type = string.IsNullOrWhiteSpace(input.Type) ? "Carro" : input.Type;
        vehicle.TagIdentifier = input.TagIdentifier ?? string.Empty;
        vehicle.ResidentId = input.ResidentId;
        vehicle.IsActive = input.IsActive;
        vehicle.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return Ok(ToOut(vehicle));
    }

    [HttpDelete("vehicles/{vehicleId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageVehicles)]
    public async Task<IActionResult> Deactivate(Guid licenseId, Guid vehicleId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var vehicle = await context.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId && v.Unit.Block.LicenseId == licenseId);
        if (vehicle == null) return NotFound();

        vehicle.IsActive = false;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return NoContent();
    }

    private static VehicleOut ToOut(VehicleDTO v) => new()
    {
        Id = v.Id,
        UnitId = v.UnitId,
        ResidentId = v.ResidentId,
        Plate = v.Plate,
        Brand = v.Brand,
        Model = v.Model,
        Color = v.Color,
        Type = v.Type,
        TagIdentifier = v.TagIdentifier,
        IsActive = v.IsActive,
        CreatedAt = v.CreatedAt,
        UpdatedAt = v.UpdatedAt
    };

    private async Task<bool> UnitBelongsAsync(Guid licenseId, Guid unitId) =>
        await context.Units.AsNoTracking().AnyAsync(u => u.Id == unitId && u.Block.LicenseId == licenseId);

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors.

- [ ] **Step 4: Manual harness note**

This codebase has no controller-level test harness (see Global Constraints) — the existing convention is manual verification via HTTP client against a running `docker-compose up api`. Record in the task notes: `POST /api/access/licenses/{licenseId}/units/{unitId}/vehicles` with a valid JWT should return `201` with the vehicle; a duplicate plate on the same unit should return `409`.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Data/Vehicles/VehicleDtos.cs CondotifyAPI/Controllers/VehicleController.cs
git commit -m "feat: add Vehicle CRUD endpoints"
```

---

### Task 3: Per-device LPR configuration (`LprConfigurationController`)

**Files:**
- Create: `CondotifyAPI/Data/Equipments/LprConfigurationDtos.cs`
- Create: `CondotifyAPI/Controllers/LprConfigurationController.cs`

**Interfaces:**
- Consumes: `AccessControlDeviceDTO.LprCameraId/.LprCameraChannel/.LprMode` (Task 1), `LicensePermissionEnum.ViewDevices/.ManageDevices` (existing).
- Produces: `GET/PUT api/access/licenses/{licenseId}/devices/{deviceId}/lpr` — this is how an operator links a gate to a camera and turns LPR on; Task 8's `LprPollingService` reads the fields this endpoint writes.

- [ ] **Step 1: Add the DTOs**

Create `CondotifyAPI/Data/Equipments/LprConfigurationDtos.cs`:

```csharp
using CondotifyAPI.Domain.Enums.Equipments;

namespace CondotifyAPI.Data.Equipments;

public sealed class LprConfigurationIn
{
    public Guid? LprCameraId { get; set; }
    public int? LprCameraChannel { get; set; }
    public LprModeEnum? LprMode { get; set; }
}

public sealed class LprConfigurationOut
{
    public Guid? LprCameraId { get; set; }
    public int? LprCameraChannel { get; set; }
    public LprModeEnum? LprMode { get; set; }
}
```

- [ ] **Step 2: Write the controller**

Create `CondotifyAPI/Controllers/LprConfigurationController.cs`:

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/devices/{deviceId:guid}/lpr")]
public sealed class LprConfigurationController(DatabaseContext context) : ControllerBase
{
    [HttpGet]
    [RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
    public async Task<IActionResult> Get(Guid licenseId, Guid deviceId)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await context.Devices.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.LicenseId == licenseId);
        if (device == null) return NotFound();

        return Ok(ToOut(device));
    }

    [HttpPut]
    [RequireLicensePermission(LicensePermissionEnum.ManageDevices)]
    public async Task<IActionResult> Configure(Guid licenseId, Guid deviceId, [FromBody] LprConfigurationIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        var device = await context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.LicenseId == licenseId);
        if (device == null) return NotFound();

        if (input.LprMode.HasValue)
        {
            if (!input.LprCameraId.HasValue)
                return BadRequest(new { Result = "MissingCamera", Errors = "Selecione a camera que filma esta cancela antes de ativar o LPR." });

            var cameraBelongsToLicense = await context.CFTVDevices.AsNoTracking()
                .AnyAsync(c => c.Id == input.LprCameraId && c.LicenseId == licenseId);
            if (!cameraBelongsToLicense)
                return BadRequest(new { Result = "CameraNotFound", Errors = "Camera nao encontrada nesta licenca." });
        }

        device.LprCameraId = input.LprMode.HasValue ? input.LprCameraId : null;
        device.LprCameraChannel = input.LprMode.HasValue ? input.LprCameraChannel : null;
        device.LprMode = input.LprMode;
        device.LastUpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return Ok(ToOut(device));
    }

    private static LprConfigurationOut ToOut(AccessControlDeviceDTO device) => new()
    {
        LprCameraId = device.LprCameraId,
        LprCameraChannel = device.LprCameraChannel,
        LprMode = device.LprMode
    };

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        return Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
               await context.Licenses.AsNoTracking().AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors.

- [ ] **Step 4: Manual harness note**

`PUT .../lpr` with `LprMode: 1` (AutoOpen) and no `LprCameraId` should return `400`; with a valid `LprCameraId` from the same license it should return `200` with the mode persisted.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Data/Equipments/LprConfigurationDtos.cs CondotifyAPI/Controllers/LprConfigurationController.cs
git commit -m "feat: add per-device LPR configuration endpoint"
```

---

### Task 4: Plate normalization (`PlateNormalizer`)

**Files:**
- Create: `CondotifyAPI/Services/Lpr/PlateNormalizer.cs`
- Test: `CondotifyAPI.Tests/PlateNormalizerTests.cs`

**Interfaces:**
- Produces: `internal static string? PlateNormalizer.Normalize(string? rawPlate)` — returns the uppercase, punctuation-stripped plate if it matches the Brazilian old or Mercosul format, otherwise `null`. Used by Task 8.

- [ ] **Step 1: Write the failing tests**

Create `CondotifyAPI.Tests/PlateNormalizerTests.cs`:

```csharp
using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class PlateNormalizerTests
{
    [Theory]
    [InlineData("ABC1234", "ABC1234")]
    [InlineData("abc1234", "ABC1234")]
    [InlineData("ABC-1234", "ABC1234")]
    [InlineData("ABC1D23", "ABC1D23")]
    [InlineData("abc 1d23", "ABC1D23")]
    public void Normalize_AcceptsOldAndMercosulFormats(string input, string expected)
    {
        var result = PlateNormalizer.Normalize(input);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("AB1234")]
    [InlineData("ABCD1234")]
    [InlineData("ABC12345")]
    public void Normalize_RejectsInvalidInput(string input)
    {
        var result = PlateNormalizer.Normalize(input);

        Assert.Null(result);
    }

    [Fact]
    public void Normalize_RejectsNull()
    {
        Assert.Null(PlateNormalizer.Normalize(null));
    }
}
```

- [ ] **Step 2: Run and confirm it fails to compile (type doesn't exist yet)**

Run: `dotnet test CondotifyAPI.Tests --filter PlateNormalizerTests`
Expected: build error, `PlateNormalizer` not found.

- [ ] **Step 3: Implement `PlateNormalizer`**

Create `CondotifyAPI/Services/Lpr/PlateNormalizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace CondotifyAPI.Services.Lpr;

internal static partial class PlateNormalizer
{
    // 3 letters, 1 digit, 1 alphanumeric, 2 digits: covers both the old
    // format (LLLNNNN, 4th-7th chars all digits) and Mercosul (LLLNLNN).
    [GeneratedRegex("^[A-Z]{3}[0-9][A-Z0-9][0-9]{2}$")]
    private static partial Regex PlatePattern();

    internal static string? Normalize(string? rawPlate)
    {
        if (string.IsNullOrWhiteSpace(rawPlate)) return null;

        var upper = rawPlate.ToUpperInvariant();
        var alphanumeric = new string(upper.Where(char.IsLetterOrDigit).ToArray());

        return PlatePattern().IsMatch(alphanumeric) ? alphanumeric : null;
    }
}
```

- [ ] **Step 4: Run and confirm the tests pass**

Run: `dotnet test CondotifyAPI.Tests --filter PlateNormalizerTests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Services/Lpr/PlateNormalizer.cs CondotifyAPI.Tests/PlateNormalizerTests.cs
git commit -m "feat: add plate normalization for old and Mercosul formats"
```

---

### Task 5: Open/alert/log decision (`LprDecisionEngine`)

**Files:**
- Create: `CondotifyAPI/Services/Lpr/LprDecisionEngine.cs`
- Test: `CondotifyAPI.Tests/LprDecisionEngineTests.cs`

**Interfaces:**
- Consumes: `LprModeEnum` (Task 1).
- Produces: `internal enum LprAction { NoRead, Opened, DetectedOnly, AlertRaised }` and `internal static LprAction LprDecisionEngine.Decide(bool plateWasRead, double confidence, double confidenceThreshold, bool vehicleMatched, LprModeEnum mode)`. Used by Task 8.

- [ ] **Step 1: Write the failing tests**

Create `CondotifyAPI.Tests/LprDecisionEngineTests.cs`:

```csharp
using CondotifyAPI.Domain.Enums.Equipments;
using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class LprDecisionEngineTests
{
    [Fact]
    public void Decide_ReturnsNoRead_WhenConfidenceBelowThreshold()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.4, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.NoRead, action);
    }

    [Fact]
    public void Decide_ReturnsNoRead_WhenPlateWasNotRead()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: false, confidence: 0.0, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.NoRead, action);
    }

    [Fact]
    public void Decide_Opens_WhenMatchedAndAutoOpen()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.Opened, action);
    }

    [Fact]
    public void Decide_LogsOnly_WhenMatchedAndDetectionOnly()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: true, mode: LprModeEnum.DetectionOnly);

        Assert.Equal(LprAction.DetectedOnly, action);
    }

    [Fact]
    public void Decide_RaisesAlert_WhenNotMatchedAndAutoOpen()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.AutoOpen);

        Assert.Equal(LprAction.AlertRaised, action);
    }

    [Fact]
    public void Decide_LogsOnly_WhenNotMatchedAndDetectionOnly()
    {
        var action = LprDecisionEngine.Decide(plateWasRead: true, confidence: 0.95, confidenceThreshold: 0.8, vehicleMatched: false, mode: LprModeEnum.DetectionOnly);

        Assert.Equal(LprAction.DetectedOnly, action);
    }
}
```

- [ ] **Step 2: Run and confirm it fails to compile**

Run: `dotnet test CondotifyAPI.Tests --filter LprDecisionEngineTests`
Expected: build error, `LprDecisionEngine`/`LprAction` not found.

- [ ] **Step 3: Implement `LprDecisionEngine`**

Create `CondotifyAPI/Services/Lpr/LprDecisionEngine.cs`:

```csharp
using CondotifyAPI.Domain.Enums.Equipments;

namespace CondotifyAPI.Services.Lpr;

internal enum LprAction
{
    NoRead,
    Opened,
    DetectedOnly,
    AlertRaised
}

internal static class LprDecisionEngine
{
    internal static LprAction Decide(bool plateWasRead, double confidence, double confidenceThreshold, bool vehicleMatched, LprModeEnum mode)
    {
        if (!plateWasRead || confidence < confidenceThreshold) return LprAction.NoRead;

        return (vehicleMatched, mode) switch
        {
            (true, LprModeEnum.AutoOpen) => LprAction.Opened,
            (true, LprModeEnum.DetectionOnly) => LprAction.DetectedOnly,
            (false, LprModeEnum.AutoOpen) => LprAction.AlertRaised,
            (false, LprModeEnum.DetectionOnly) => LprAction.DetectedOnly,
            _ => LprAction.NoRead
        };
    }
}
```

- [ ] **Step 4: Run and confirm the tests pass**

Run: `dotnet test CondotifyAPI.Tests --filter LprDecisionEngineTests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Services/Lpr/LprDecisionEngine.cs CondotifyAPI.Tests/LprDecisionEngineTests.cs
git commit -m "feat: add LPR open/alert/log decision engine"
```

---

### Task 6: OCR service HTTP client (`ILprRecognitionClient`)

**Files:**
- Create: `CondotifyAPI/Services/Lpr/LprRecognitionClient.cs`
- Test: `CondotifyAPI.Tests/HttpLprRecognitionClientTests.cs`

**Interfaces:**
- Produces: `public sealed record PlateRecognitionResult(string? Plate, double Confidence)`; `public interface ILprRecognitionClient { Task<PlateRecognitionResult> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default); }`; `public sealed class HttpLprRecognitionClient(HttpClient httpClient) : ILprRecognitionClient`. Used by Task 8.

- [ ] **Step 1: Write the failing tests**

Create `CondotifyAPI.Tests/HttpLprRecognitionClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using CondotifyAPI.Services.Lpr;

namespace CondotifyAPI.Tests;

public class HttpLprRecognitionClientTests
{
    [Fact]
    public async Task RecognizeAsync_ParsesPlateAndConfidence()
    {
        var handler = new StubHttpMessageHandler(new StringContent("""{"plate":"ABC1D23","confidence":0.87}""", Encoding.UTF8, "application/json"));
        var client = new HttpLprRecognitionClient(new HttpClient(handler) { BaseAddress = new Uri("http://lpr-ocr") });

        var result = await client.RecognizeAsync([1, 2, 3], "image/jpeg");

        Assert.Equal("ABC1D23", result.Plate);
        Assert.Equal(0.87, result.Confidence);
    }

    [Fact]
    public async Task RecognizeAsync_ReturnsNullPlate_WhenServiceFoundNothing()
    {
        var handler = new StubHttpMessageHandler(new StringContent("""{"plate":null,"confidence":0.0}""", Encoding.UTF8, "application/json"));
        var client = new HttpLprRecognitionClient(new HttpClient(handler) { BaseAddress = new Uri("http://lpr-ocr") });

        var result = await client.RecognizeAsync([1, 2, 3], "image/jpeg");

        Assert.Null(result.Plate);
        Assert.Equal(0.0, result.Confidence);
    }

    private sealed class StubHttpMessageHandler(HttpContent responseContent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = responseContent });
    }
}
```

- [ ] **Step 2: Run and confirm it fails to compile**

Run: `dotnet test CondotifyAPI.Tests --filter HttpLprRecognitionClientTests`
Expected: build error, `HttpLprRecognitionClient` not found.

- [ ] **Step 3: Implement the client**

Create `CondotifyAPI/Services/Lpr/LprRecognitionClient.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CondotifyAPI.Services.Lpr;

public sealed record PlateRecognitionResult(string? Plate, double Confidence);

public interface ILprRecognitionClient
{
    Task<PlateRecognitionResult> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default);
}

public sealed class HttpLprRecognitionClient(HttpClient httpClient) : ILprRecognitionClient
{
    public async Task<PlateRecognitionResult> RecognizeAsync(byte[] imageBytes, string contentType, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var imageContent = new ByteArrayContent(imageBytes);
        imageContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(imageContent, "file", "snapshot.jpg");

        using var response = await httpClient.PostAsync("/recognize", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RecognizeResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Resposta vazia do servico de OCR.");

        return new PlateRecognitionResult(payload.Plate, payload.Confidence);
    }

    private sealed record RecognizeResponse(string? Plate, double Confidence);
}
```

- [ ] **Step 4: Run and confirm the tests pass**

Run: `dotnet test CondotifyAPI.Tests --filter HttpLprRecognitionClientTests`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Services/Lpr/LprRecognitionClient.cs CondotifyAPI.Tests/HttpLprRecognitionClientTests.cs
git commit -m "feat: add HTTP client for the LPR OCR service"
```

---

### Task 7: Active vehicle lookup (`IVehicleLookupService`)

**Files:**
- Create: `CondotifyAPI/Services/Lpr/VehicleLookupService.cs`

**Interfaces:**
- Consumes: `DatabaseContext.Vehicles` (existing), `VehicleDTO.Unit.Block.LicenseId` navigation (existing).
- Produces: `public interface IVehicleLookupService { Task<Guid?> FindActiveVehicleIdAsync(Guid licenseId, string normalizedPlate, CancellationToken cancellationToken = default); }` and `VehicleLookupService`. Used by Task 8.

- [ ] **Step 1: Implement the service**

Create `CondotifyAPI/Services/Lpr/VehicleLookupService.cs`:

```csharp
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public interface IVehicleLookupService
{
    Task<Guid?> FindActiveVehicleIdAsync(Guid licenseId, string normalizedPlate, CancellationToken cancellationToken = default);
}

public sealed class VehicleLookupService(DatabaseContext context) : IVehicleLookupService
{
    public async Task<Guid?> FindActiveVehicleIdAsync(Guid licenseId, string normalizedPlate, CancellationToken cancellationToken = default) =>
        await context.Vehicles
            .AsNoTracking()
            .Where(v => v.IsActive && v.Plate == normalizedPlate && v.Unit.Block.LicenseId == licenseId)
            .Select(v => (Guid?)v.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
```

This is a thin, single-purpose DB query with no branching logic — per Global Constraints, it is verified by build + the manual harness note below, not by a unit test (this project has no EF InMemory provider to fake the query against).

- [ ] **Step 2: Build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors.

- [ ] **Step 3: Manual harness note**

Once Task 2's `VehicleController` is deployed, register a vehicle with plate `ABC1D23` on a unit, then confirm `FindActiveVehicleIdAsync(licenseId, "ABC1D23")` returns that vehicle's `Id` and returns `null` for a plate that isn't registered or belongs to a different license — exercised end-to-end once Task 8 wires it into the polling loop.

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI/Services/Lpr/VehicleLookupService.cs
git commit -m "feat: add active vehicle lookup by plate and license"
```

---

### Task 8: Orchestration — debounce store, device processor, polling worker, wiring

**Files:**
- Create: `CondotifyAPI/Services/Lpr/LprDebounceStore.cs`
- Create: `CondotifyAPI/Services/Lpr/LprDeviceProcessor.cs`
- Create: `CondotifyAPI/Services/Lpr/LprPollingService.cs`
- Modify: `CondotifyAPI/Program.cs`
- Modify: `docker-compose.yml`

**Interfaces:**
- Consumes: `ICftvSnapshotService.FetchAsync` (existing, `CondotifyAPI.Services.CFTV`), `ILprRecognitionClient` (Task 6), `PlateNormalizer.Normalize` (Task 4), `LprDecisionEngine.Decide`/`LprAction` (Task 5), `IVehicleLookupService` (Task 7), `IAccessControlService.OpenDoorAsync` (existing, `CondotifyAPI.Services.AccessControl`), `VehicleAccessAuditDTO`/`OperationalAlertDTO` (existing/Task 1).
- Produces: `LprPollingService` registered as `IHostedService`; nothing downstream depends on it (it's the outermost orchestrator).

- [ ] **Step 1: Implement the in-memory debounce store**

Create `CondotifyAPI/Services/Lpr/LprDebounceStore.cs`:

```csharp
using System.Collections.Concurrent;

namespace CondotifyAPI.Services.Lpr;

public interface ILprDebounceStore
{
    bool WasRecentlyTriggered(Guid deviceId, string plate, TimeSpan window);
    void MarkTriggered(Guid deviceId, string plate);
}

// Per-instance, in-memory: correct for a single API instance. If the API
// ever scales horizontally, this needs to move to a shared store (e.g.
// Redis) so two instances don't both act on the same plate. Not needed
// today - documented here instead of built ahead of the requirement.
public sealed class InMemoryLprDebounceStore : ILprDebounceStore
{
    private readonly ConcurrentDictionary<(Guid DeviceId, string Plate), DateTime> _lastTriggeredAt = new();

    public bool WasRecentlyTriggered(Guid deviceId, string plate, TimeSpan window) =>
        _lastTriggeredAt.TryGetValue((deviceId, plate), out var lastTriggeredAt) &&
        DateTime.UtcNow - lastTriggeredAt < window;

    public void MarkTriggered(Guid deviceId, string plate) =>
        _lastTriggeredAt[(deviceId, plate)] = DateTime.UtcNow;
}
```

- [ ] **Step 2: Implement the per-device processor**

Create `CondotifyAPI/Services/Lpr/LprDeviceProcessor.cs`:

```csharp
using AutoMapper;
using CondotifyAPI.Domain.DTO.Equipments;
using CondotifyAPI.Domain.DTO.Observability;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.AccessControl;
using CondotifyAPI.Services.CFTV;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public sealed class LprDeviceProcessor(
    ICftvSnapshotService snapshotService,
    ILprRecognitionClient recognitionClient,
    IVehicleLookupService vehicleLookup,
    IAccessControlService accessControl,
    IMapper mapper,
    ILprDebounceStore debounceStore,
    IConfiguration configuration,
    ILogger<LprDeviceProcessor> logger)
{
    public async Task ProcessAsync(DatabaseContext context, AccessControlDeviceDTO device, CancellationToken cancellationToken)
    {
        if (device.LprMode is null || device.LprCameraId is null) return;

        var camera = await context.CFTVDevices.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == device.LprCameraId, cancellationToken);
        if (camera == null)
        {
            logger.LogWarning("Cancela {DeviceId} aponta para uma camera LPR inexistente {CameraId}.", device.Id, device.LprCameraId);
            return;
        }

        CftvSnapshot? snapshot;
        try
        {
            snapshot = await snapshotService.FetchAsync(camera, device.LprCameraChannel ?? 1, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Falha ao capturar snapshot da camera {CameraId} para LPR.", camera.Id);
            return;
        }

        if (snapshot == null) return;

        PlateRecognitionResult recognition;
        try
        {
            recognition = await recognitionClient.RecognizeAsync(snapshot.Content, snapshot.ContentType, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Servico de OCR indisponivel ao processar cancela {DeviceId}.", device.Id);
            return;
        }

        var confidenceThreshold = Math.Clamp(configuration.GetValue("Lpr:ConfidenceThreshold", 0.75), 0.0, 1.0);
        var normalizedPlate = PlateNormalizer.Normalize(recognition.Plate);
        var plateWasRead = normalizedPlate != null;

        if (plateWasRead)
        {
            var debounceSeconds = Math.Clamp(configuration.GetValue("Lpr:DebounceSeconds", 20), 1, 300);
            if (debounceStore.WasRecentlyTriggered(device.Id, normalizedPlate!, TimeSpan.FromSeconds(debounceSeconds)))
                return;
        }

        Guid? matchedVehicleId = plateWasRead
            ? await vehicleLookup.FindActiveVehicleIdAsync(device.LicenseId, normalizedPlate!, cancellationToken)
            : null;

        var action = LprDecisionEngine.Decide(
            plateWasRead,
            recognition.Confidence,
            confidenceThreshold,
            matchedVehicleId.HasValue,
            device.LprMode.Value);

        if (plateWasRead && action != LprAction.NoRead)
            debounceStore.MarkTriggered(device.Id, normalizedPlate!);

        context.VehicleAccessAudits.Add(new VehicleAccessAuditDTO
        {
            Id = Guid.NewGuid(),
            AccessControlDeviceId = device.Id,
            PlateRead = normalizedPlate,
            Confidence = recognition.Confidence,
            MatchedVehicleId = matchedVehicleId,
            Action = action switch
            {
                LprAction.Opened => VehicleAccessAuditAction.Opened,
                LprAction.AlertRaised => VehicleAccessAuditAction.AlertRaised,
                LprAction.DetectedOnly => VehicleAccessAuditAction.DetectedOnly,
                _ => VehicleAccessAuditAction.NoRead
            },
            Timestamp = DateTime.UtcNow
        });

        if (action == LprAction.Opened)
        {
            try
            {
                await accessControl.OpenDoorAsync(mapper.Map<AccessControlDevice>(device), device.LprCameraChannel ?? 1);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Falha ao abrir a cancela {DeviceId} apos leitura de placa por LPR.", device.Id);
            }
        }
        else if (action == LprAction.AlertRaised)
        {
            await RaiseAlertAsync(context, device, normalizedPlate, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task RaiseAlertAsync(DatabaseContext context, AccessControlDeviceDTO device, string? plate, CancellationToken cancellationToken)
    {
        var license = await context.Licenses.AsNoTracking()
            .FirstAsync(l => l.Id == device.LicenseId, cancellationToken);
        var fingerprint = $"lpr:{device.Id}:{plate}";
        var now = DateTime.UtcNow;

        var existing = await context.OperationalAlerts
            .FirstOrDefaultAsync(a => a.EnterpriseId == license.EnterpriseId && a.Fingerprint == fingerprint, cancellationToken);

        if (existing != null)
        {
            existing.OccurrenceCount++;
            existing.LastOccurredAt = now;
            existing.IsConditionActive = true;
            existing.Status = OperationalAlertStatus.Open;
            return;
        }

        context.OperationalAlerts.Add(new OperationalAlertDTO
        {
            Id = Guid.NewGuid(),
            EnterpriseId = license.EnterpriseId,
            LicenseId = license.Id,
            Fingerprint = fingerprint,
            Type = "LprPlateNotRecognized",
            Source = "Lpr",
            Severity = OperationalAlertSeverity.Warning,
            Status = OperationalAlertStatus.Open,
            Title = $"Veiculo nao identificado em {device.Name}",
            Message = plate is null
                ? $"A cancela {device.Name} nao conseguiu ler a placa do veiculo com confianca suficiente."
                : $"Placa {plate} nao possui cadastro ativo para a cancela {device.Name}.",
            ResourceType = "AccessControlDevice",
            ResourceId = device.Id,
            IsConditionActive = true,
            OccurrenceCount = 1,
            FirstOccurredAt = now,
            LastOccurredAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });
    }
}
```

- [ ] **Step 3: Implement the polling worker**

Create `CondotifyAPI/Services/Lpr/LprPollingService.cs`:

```csharp
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.Lpr;

public sealed class LprPollingService(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<LprPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Lpr:PollIntervalSeconds", 2), 1, 60));
        using var timer = new PeriodicTimer(interval);
        do
        {
            await PollAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();
            var processor = scope.ServiceProvider.GetRequiredService<LprDeviceProcessor>();

            var devices = await context.Devices
                .Where(d => d.LprMode != null && d.LprCameraId != null)
                .ToListAsync(cancellationToken);

            foreach (var device in devices)
                await processor.ProcessAsync(context, device, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falha no ciclo de reconhecimento de placas (LPR).");
        }
    }
}
```

- [ ] **Step 4: Wire everything into `Program.cs`**

Near the other `AddHttpClient<...>` typed clients (`CondotifyAPI/Program.cs:182`, right after the `IMediaGatewayClient` registration), add:

```csharp
builder.Services.AddHttpClient<ILprRecognitionClient, HttpLprRecognitionClient>(client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("CONDOTIFY_LPR_OCR_URL") ?? "http://lpr-ocr:8000");
    client.Timeout = TimeSpan.FromSeconds(10);
});
```

Near the other scoped/singleton service registrations, add:

```csharp
builder.Services.AddSingleton<ILprDebounceStore, InMemoryLprDebounceStore>();
builder.Services.AddScoped<IVehicleLookupService, VehicleLookupService>();
builder.Services.AddScoped<LprDeviceProcessor>();
```

Next to the other `AddHostedService` calls (`CondotifyAPI/Program.cs:208-219`), add:

```csharp
builder.Services.AddHostedService<LprPollingService>();
```

Add the corresponding `using CondotifyAPI.Services.Lpr;` at the top of `Program.cs` if not already covered by an existing wildcard/global using.

- [ ] **Step 5: Add the OCR service to `docker-compose.yml`**

Add a dedicated internal network and service, following the same isolation pattern as `mediamtx` (no ports published — only `api` can reach it):

```yaml
  lpr-ocr:
    build:
      context: ./lpr-ocr
    container_name: condotify-lpr-ocr
    restart: unless-stopped
    networks:
      - condotify-lpr
```

Add `condotify-lpr` to the `api` service's `networks:` list (alongside `default` and `condotify-media`), and add `CONDOTIFY_LPR_OCR_URL: http://lpr-ocr:8000` to the `api` service's `environment:`. Add the network under the top-level `networks:` key:

```yaml
  condotify-lpr:
```

- [ ] **Step 6: Build**

Run: `dotnet build Condotify.sln`
Expected: 0 errors.

- [ ] **Step 7: Run the full test suite**

Run: `dotnet test Condotify.sln`
Expected: all tests pass, including the pure-logic tests from Tasks 4-6.

- [ ] **Step 8: Manual harness note**

End-to-end verification needs Task 9 (the OCR container) running: enable LPR (`AutoOpen`) on a device linked to a camera with a known plate registered, confirm the gate opens and a `VehicleAccessAudit` row with `Action = Opened` is written; then point it at an unregistered plate and confirm the gate stays closed and an `OperationalAlert` with `Type = LprPlateNotRecognized` appears.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI/Services/Lpr/LprDebounceStore.cs CondotifyAPI/Services/Lpr/LprDeviceProcessor.cs CondotifyAPI/Services/Lpr/LprPollingService.cs CondotifyAPI/Program.cs docker-compose.yml
git commit -m "feat: orchestrate LPR polling, decisioning, gate opening and alerting"
```

---

### Task 9: Self-hosted OCR microservice (`lpr-ocr`)

**Files:**
- Create: `lpr-ocr/app/__init__.py`
- Create: `lpr-ocr/app/recognizer.py`
- Create: `lpr-ocr/app/main.py`
- Create: `lpr-ocr/tests/__init__.py`
- Create: `lpr-ocr/tests/test_main.py`
- Create: `lpr-ocr/requirements.txt`
- Create: `lpr-ocr/Dockerfile`

**Interfaces:**
- Produces: `POST /recognize` (multipart `file` field, image bytes) → `{"plate": string|null, "confidence": number}`; `GET /health` → `{"status": "ok"}`. Consumed by Task 8's `HttpLprRecognitionClient`.

- [ ] **Step 1: Write the failing tests**

Create `lpr-ocr/tests/__init__.py` (empty file) and `lpr-ocr/tests/test_main.py`:

```python
from app.main import app, get_recognizer
from app.recognizer import PlateRecognitionResult
from fastapi.testclient import TestClient


class FakeRecognizer:
    def __init__(self, result: PlateRecognitionResult) -> None:
        self._result = result

    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult:
        return self._result


def _client_with(result: PlateRecognitionResult) -> TestClient:
    app.dependency_overrides[get_recognizer] = lambda: FakeRecognizer(result)
    return TestClient(app)


def test_recognize_returns_plate_and_confidence():
    client = _client_with(PlateRecognitionResult(plate="ABC1D23", confidence=0.94))

    response = client.post(
        "/recognize",
        files={"file": ("snapshot.jpg", b"\xff\xd8\xff\xdb fake-jpeg-bytes", "image/jpeg")},
    )

    assert response.status_code == 200
    assert response.json() == {"plate": "ABC1D23", "confidence": 0.94}
    app.dependency_overrides.clear()


def test_recognize_reports_no_read_when_nothing_found():
    client = _client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.post(
        "/recognize",
        files={"file": ("snapshot.jpg", b"\xff\xd8\xff\xdb fake-jpeg-bytes", "image/jpeg")},
    )

    assert response.status_code == 200
    assert response.json() == {"plate": None, "confidence": 0.0}
    app.dependency_overrides.clear()


def test_recognize_rejects_non_image_upload():
    client = _client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.post(
        "/recognize",
        files={"file": ("notes.txt", b"hello", "text/plain")},
    )

    assert response.status_code == 400
    app.dependency_overrides.clear()


def test_health_returns_ok():
    client = _client_with(PlateRecognitionResult(plate=None, confidence=0.0))

    response = client.get("/health")

    assert response.status_code == 200
    assert response.json() == {"status": "ok"}
    app.dependency_overrides.clear()
```

- [ ] **Step 2: Run and confirm it fails (module doesn't exist yet)**

Run (from `lpr-ocr/`): `python -m pytest tests/ -v`
Expected: `ModuleNotFoundError: No module named 'app'`.

- [ ] **Step 3: Implement the recognizer abstraction**

Create `lpr-ocr/app/__init__.py` (empty) and `lpr-ocr/app/recognizer.py`:

```python
from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class PlateRecognitionResult:
    plate: str | None
    confidence: float


class PlateRecognizer(Protocol):
    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult: ...


class FastAlprRecognizer:
    """Self-hosted recognizer backed by fast-alpr (YOLO plate detector + OCR).

    The model is loaded lazily so importing this module never requires the
    (large) model weights to be present - keeps unit tests fast and offline.
    Model choice/tuning against real camera footage is a calibration task,
    not an architecture decision - swap the model names below as needed.
    """

    def __init__(
        self,
        detector_model: str = "yolo-v9-t-640-license-plate-end2end",
        ocr_model: str = "cct-xs-v1-global-model",
    ) -> None:
        self._detector_model = detector_model
        self._ocr_model = ocr_model
        self._alpr = None

    def _ensure_loaded(self):
        if self._alpr is None:
            from fast_alpr import ALPR

            self._alpr = ALPR(detector_model=self._detector_model, ocr_model=self._ocr_model)
        return self._alpr

    def recognize(self, image_bytes: bytes) -> PlateRecognitionResult:
        import cv2
        import numpy as np

        alpr = self._ensure_loaded()
        array = np.frombuffer(image_bytes, dtype=np.uint8)
        image = cv2.imdecode(array, cv2.IMREAD_COLOR)
        if image is None:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        results = alpr.predict(image)
        if not results:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        best = max(results, key=lambda r: r.ocr.text_confidence if r.ocr else 0.0)
        if best.ocr is None:
            return PlateRecognitionResult(plate=None, confidence=0.0)

        return PlateRecognitionResult(plate=best.ocr.text, confidence=float(best.ocr.text_confidence))
```

- [ ] **Step 4: Implement the FastAPI app**

Create `lpr-ocr/app/main.py`:

```python
from __future__ import annotations

from fastapi import Depends, FastAPI, HTTPException, UploadFile

from .recognizer import FastAlprRecognizer, PlateRecognitionResult, PlateRecognizer

app = FastAPI(title="Condotify LPR OCR")

_default_recognizer = FastAlprRecognizer()


def get_recognizer() -> PlateRecognizer:
    return _default_recognizer


@app.post("/recognize")
async def recognize(file: UploadFile, recognizer: PlateRecognizer = Depends(get_recognizer)) -> dict:
    content_type = file.content_type or ""
    if not content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="O arquivo enviado precisa ser uma imagem.")

    image_bytes = await file.read()
    if not image_bytes:
        raise HTTPException(status_code=400, detail="Imagem vazia.")

    result: PlateRecognitionResult = recognizer.recognize(image_bytes)
    return {"plate": result.plate, "confidence": result.confidence}


@app.get("/health")
async def health() -> dict:
    return {"status": "ok"}
```

- [ ] **Step 5: Run and confirm the tests pass**

Run (from `lpr-ocr/`): `python -m pytest tests/ -v`
Expected: all 4 tests PASS.

- [ ] **Step 6: Add `requirements.txt` and `Dockerfile`**

Create `lpr-ocr/requirements.txt`:

```
fastapi==0.115.0
uvicorn[standard]==0.32.0
python-multipart==0.0.12
fast-alpr==0.1.4
opencv-python-headless==4.10.0.84
numpy==2.1.2
pytest==8.3.3
httpx==0.27.2
```

Create `lpr-ocr/Dockerfile`:

```dockerfile
FROM python:3.11-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app ./app

EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
```

- [ ] **Step 7: Manual harness note**

`docker compose build lpr-ocr` should succeed (this downloads the `fast-alpr` model weights on first run inside the container — expect the first `docker compose up` to take longer than subsequent ones). Once up, `curl -F file=@sample.jpg http://localhost:<mapped-port>/recognize` against a real plate photo should return a plausible plate string — actual recognition accuracy against real camera footage is a calibration task flagged in the design spec, not something this task's automated tests can prove without a labeled image corpus.

- [ ] **Step 8: Commit**

```bash
git add lpr-ocr/
git commit -m "feat: add self-hosted LPR OCR microservice"
```

---

## Self-Review Notes

- **Spec coverage:** camera↔gate link (Task 1/3), self-hosted OCR (Task 9), per-device mode toggle (Task 1/3), polling trigger (Task 8), plate normalization (Task 4), debounce (Task 8), vehicle CRUD prerequisite (Task 2), decision matrix incl. gate-never-opens-blind (Task 5/8), alert integration reusing `OperationalAlerts` (Task 8), LGPD note about the OCR service never persisting images (Task 9 docstring + Global Constraints) — all covered.
- **Placeholder scan:** no TBD/TODO left; the one open item (retention policy for `VehicleAccessAudit`/snapshots) was already flagged as an explicit product-team pendency in the design spec, not silently dropped here — it's out of scope for this plan and not needed for the code to work.
- **Type consistency:** `LprModeEnum`, `PlateRecognitionResult`, `LprAction`, `VehicleAccessAuditAction` are defined once (Tasks 1, 5, 6) and referenced with the same names/signatures in every later task.
