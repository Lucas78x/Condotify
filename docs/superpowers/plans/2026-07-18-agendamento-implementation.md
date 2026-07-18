# Agendamento de Áreas Comuns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a condo's staff (síndico/porteiro) register and manage bookings of shared amenities (BBQ area, pool, party hall, sports court...) per condo, each with its own weekly schedule, approval policy, fee note, terms, monthly limit, and cancellation window.

**Architecture:** Backend follows the existing `AccessRoutes` pattern exactly: EF Core entities scoped by `LicenseId`, thin controllers talking directly to `DatabaseContext`, manual DTOs (no AutoMapper), authorization via `RequireLicensePermissionAttribute` + `HasLicenseAccessAsync`. Frontend follows the existing `AccessRoutesModule`/`AccessRouteFormDialog` pattern: a Blazor `LicenseModules` component with MudBlazor, a `CondotifyApiClient` method per endpoint, and dialogs for create/edit flows. This is v1 staff-only (no resident login exists in this codebase yet) — see `docs/superpowers/specs/2026-07-18-agendamento-design.md` for the full rationale.

**Tech Stack:** ASP.NET Core 8 Web API (`CondotifyAPI`), EF Core + Npgsql (`CondotifyAPI.Infrastructure`), Blazor Server + MudBlazor (`Condotify`), xUnit (`CondotifyAPI.Tests`).

## Global Constraints

- Follow the `AccessRoutes` file-per-domain layout: DTOs in `CondotifyAPI.Domain/DTO/Amenities/`, enums in `CondotifyAPI.Domain/Enums/Amenities/`, EF configs in `CondotifyAPI.Infrastructure/ContextConfiguration/Amenities/`, DbContext partial in `CondotifyAPI.Infrastructure/DatabaseContext/Amenities/`, request/response DTOs in `CondotifyAPI/Data/Amenities/`.
- All new routes are nested under `api/access/licenses/{licenseId:guid}/amenities` and require `RequireLicensePermission` + the controller's own `HasLicenseAccessAsync(licenseId)` check on every action (copy the exact method body from `AccessRoutesController.cs:528-541`).
- All new tables get `LicenseId` (directly or via the `Amenity` parent) and cascade-delete from `License`, matching every existing domain table.
- No AutoMapper, no repository layer — controllers query `DatabaseContext` directly, same as `AccessRoutesController`.
- No payment gateway integration, no resident login/auth — both explicitly out of scope per the spec.
- Every new DateTime property is a plain date/time; the existing global rule in `DatabaseContext.cs:44-54` auto-sets `timestamp with time zone` column type and a `NOW()` default for any property literally named `CreatedAt` — no manual work needed for that one field, but every other timestamp still needs an explicit `HasDefaultValueSql("CURRENT_TIMESTAMP")` where relevant, exactly like `AccessRouteConfiguration`.
- Frontend: MudBlazor components only, follow `portal.css`/`AccessRoutesModule.razor` shared CSS classes (`content-panel`, `panel-toolbar`, `mini-kpi`, `empty-state`) — do not introduce a new CSS framework.

---

### Task 1: Amenity domain enums and DTOs

**Files:**
- Create: `CondotifyAPI.Domain/Enums/Amenities/AmenityEnums.cs`
- Create: `CondotifyAPI.Domain/DTO/Amenities/AmenityDTO.cs`
- Modify: `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs:41-50` (add `Amenities` nav collection, mirroring the existing `AccessRoutes` collection on line 49)

**Interfaces:**
- Produces: `AmenityBookingStatusEnum` (`Pending=0, Confirmed=1, Rejected=2, Cancelled=3, Completed=4`), `AmenityDTO`, `AmenityScheduleSlotDTO`, `AmenityBlackoutDTO`, `AmenityBookingDTO` — consumed by Tasks 2, 6, 8, 9.

- [ ] **Step 1: Create the enum file**

```csharp
namespace CondotifyAPI.Domain.Enums.Amenities;

public enum AmenityBookingStatusEnum
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2,
    Cancelled = 3,
    Completed = 4
}
```

- [ ] **Step 2: Create the DTO file**

```csharp
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.Enums.Amenities;

namespace CondotifyAPI.Domain.DTO.Amenities;

public class AmenityDTO
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; } = true;
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; } = 60;
    public int CancellationCutoffHours { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<AmenityScheduleSlotDTO> ScheduleSlots { get; set; } = new List<AmenityScheduleSlotDTO>();
    public ICollection<AmenityBlackoutDTO> Blackouts { get; set; } = new List<AmenityBlackoutDTO>();
    public ICollection<AmenityBookingDTO> Bookings { get; set; } = new List<AmenityBookingDTO>();
}

public class AmenityScheduleSlotDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; } = true;
}

public class AmenityBlackoutDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AmenityBookingDTO
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public AmenityDTO Amenity { get; set; } = null!;
    public Guid LicenseId { get; set; }
    public LicenseDTO License { get; set; } = null!;
    public Guid UnitId { get; set; }
    public UnitDTO Unit { get; set; } = null!;
    public Guid? ResidentId { get; set; }
    public ResidentAccessDTO? Resident { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public AmenityScheduleSlotDTO Slot { get; set; } = null!;
    public AmenityBookingStatusEnum Status { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
}
```

- [ ] **Step 3: Add the `Amenities` nav collection to `LicenseDTO`**

In `CondotifyAPI.Domain/DTO/License/LicenseDTO.cs`, add the `using` and the collection next to the existing `AccessRoutes` line:

```csharp
using CondotifyAPI.Domain.DTO.Amenities;
```

```csharp
        public List<AccessRouteDTO> AccessRoutes { get; set; } = new();
        public List<AmenityDTO> Amenities { get; set; } = new();
```

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI.Domain`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI.Domain/Enums/Amenities/AmenityEnums.cs CondotifyAPI.Domain/DTO/Amenities/AmenityDTO.cs CondotifyAPI.Domain/DTO/License/LicenseDTO.cs
git commit -m "feat: add Amenity booking domain DTOs and enum"
```

---

### Task 2: EF Core configuration for the Amenity domain

**Files:**
- Create: `CondotifyAPI.Infrastructure/ContextConfiguration/Amenities/AmenityConfiguration.cs`

**Interfaces:**
- Consumes: `AmenityDTO`, `AmenityScheduleSlotDTO`, `AmenityBlackoutDTO`, `AmenityBookingDTO` (Task 1).
- Produces: `AmenityConfiguration`, `AmenityScheduleSlotConfiguration`, `AmenityBlackoutConfiguration`, `AmenityBookingConfiguration` — consumed by Task 3.

- [ ] **Step 1: Create the configuration file**

```csharp
using CondotifyAPI.Domain.DTO.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CondotifyAPI.Infrastructure.ContextConfiguration.Amenities;

public sealed class AmenityConfiguration : IEntityTypeConfiguration<AmenityDTO>
{
    public void Configure(EntityTypeBuilder<AmenityDTO> builder)
    {
        builder.ToTable("Amenities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.FeeAmount).HasColumnType("numeric(10,2)");
        builder.Property(x => x.FeeDescription).HasMaxLength(300);
        builder.Property(x => x.RequiresApproval).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.RequiresTermsAcceptance).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.TermsText).HasMaxLength(4000);
        builder.Property(x => x.MinAdvanceNoticeHours).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxAdvanceDays).IsRequired().HasDefaultValue(60);
        builder.Property(x => x.CancellationCutoffHours).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.Property(x => x.UpdatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.License)
            .WithMany(x => x.Amenities)
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.LicenseId, x.Name }).IsUnique();
    }
}

public sealed class AmenityScheduleSlotConfiguration : IEntityTypeConfiguration<AmenityScheduleSlotDTO>
{
    public void Configure(EntityTypeBuilder<AmenityScheduleSlotDTO> builder)
    {
        builder.ToTable("AmenityScheduleSlots");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DayOfWeek).IsRequired();
        builder.Property(x => x.StartTime).IsRequired();
        builder.Property(x => x.EndTime).IsRequired();
        builder.Property(x => x.Active).IsRequired().HasDefaultValue(true);

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.ScheduleSlots)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AmenityId, x.DayOfWeek });
    }
}

public sealed class AmenityBlackoutConfiguration : IEntityTypeConfiguration<AmenityBlackoutDTO>
{
    public void Configure(EntityTypeBuilder<AmenityBlackoutDTO> builder)
    {
        builder.ToTable("AmenityBlackouts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.EndDate).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(300);

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.Blackouts)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.AmenityId, x.StartDate, x.EndDate });
    }
}

public sealed class AmenityBookingConfiguration : IEntityTypeConfiguration<AmenityBookingDTO>
{
    public void Configure(EntityTypeBuilder<AmenityBookingDTO> builder)
    {
        builder.ToTable("AmenityBookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Date).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(500);
        builder.Property(x => x.CancelReason).HasMaxLength(300);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("CURRENT_TIMESTAMP");

        builder.HasOne(x => x.Amenity)
            .WithMany(x => x.Bookings)
            .HasForeignKey(x => x.AmenityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.License)
            .WithMany()
            .HasForeignKey(x => x.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Unit)
            .WithMany()
            .HasForeignKey(x => x.UnitId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Resident)
            .WithMany()
            .HasForeignKey(x => x.ResidentId)
            .OnDelete(DeleteBehavior.SetNull);

        /*
         * Restrict (not Cascade): a schedule slot must never be hard-deleted
         * while a booking still points to it, or booking history would be
         * silently destroyed. The controller only physically deletes a slot
         * once it has verified zero bookings reference it (see Task 9);
         * this FK is the DB-level safety net for that invariant.
         */
        builder.HasOne(x => x.Slot)
            .WithMany()
            .HasForeignKey(x => x.SlotId)
            .OnDelete(DeleteBehavior.Restrict);

        /*
         * Only one active (Pending or Confirmed) booking may occupy a given
         * slot on a given date. This is the concurrency safety net: two
         * simultaneous create requests race the application-level
         * availability check, but only one can win this index.
         */
        builder.HasIndex(x => new { x.AmenityId, x.SlotId, x.Date })
            .HasFilter("\"Status\" IN (0, 1)")
            .IsUnique();

        builder.HasIndex(x => new { x.LicenseId, x.UnitId, x.Date });
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI.Infrastructure`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add CondotifyAPI.Infrastructure/ContextConfiguration/Amenities/AmenityConfiguration.cs
git commit -m "feat: add EF Core configuration for Amenity booking tables"
```

---

### Task 3: Wire Amenity tables into `DatabaseContext`

**Files:**
- Create: `CondotifyAPI.Infrastructure/DatabaseContext/Amenities/DatabaseContext.Amenity.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs:27-43` (register the new entity configuration call)

**Interfaces:**
- Consumes: `AmenityConfiguration`, `AmenityScheduleSlotConfiguration`, `AmenityBlackoutConfiguration`, `AmenityBookingConfiguration` (Task 2).
- Produces: `DatabaseContext.Amenities`, `.AmenityScheduleSlots`, `.AmenityBlackouts`, `.AmenityBookings` (`DbSet<T>` properties) — consumed by Tasks 6, 8, 9, 10.

- [ ] **Step 1: Create the partial `DatabaseContext` file**

```csharp
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Infrastructure.ContextConfiguration.Amenities;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Infrastructure;

public partial class DatabaseContext
{
    public DbSet<AmenityDTO> Amenities { get; set; }
    public DbSet<AmenityScheduleSlotDTO> AmenityScheduleSlots { get; set; }
    public DbSet<AmenityBlackoutDTO> AmenityBlackouts { get; set; }
    public DbSet<AmenityBookingDTO> AmenityBookings { get; set; }

    internal static void AmenitiesEntityConfiguration(ModelBuilder builder)
    {
        builder.ApplyConfiguration(new AmenityConfiguration());
        builder.ApplyConfiguration(new AmenityScheduleSlotConfiguration());
        builder.ApplyConfiguration(new AmenityBlackoutConfiguration());
        builder.ApplyConfiguration(new AmenityBookingConfiguration());
    }
}
```

- [ ] **Step 2: Register the call in `OnModelCreating`**

In `CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs`, add the call next to `AccessRoutesEntityConfiguration`:

```csharp
        AccessRoutesEntityConfiguration(modelBuilder);
        AmenitiesEntityConfiguration(modelBuilder);
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI.Infrastructure`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI.Infrastructure/DatabaseContext/Amenities/DatabaseContext.Amenity.cs CondotifyAPI.Infrastructure/DatabaseContext/DatabaseContext.cs
git commit -m "feat: register Amenity DbSets and entity configuration"
```

---

### Task 4: Generate the EF Core migration

**Files:**
- Create: `CondotifyAPI.Infrastructure/Migrations/<timestamp>_AddAmenityBookings.cs` (and `.Designer.cs`)
- Modify: `CondotifyAPI.Infrastructure/Migrations/DatabaseContextModelSnapshot.cs` (auto-generated)

**Interfaces:**
- Consumes: the model built by Tasks 1-3.
- Produces: the `Amenities`, `AmenityScheduleSlots`, `AmenityBlackouts`, `AmenityBookings` tables in Postgres, applied automatically on next API start via `db.Database.Migrate()` (`CondotifyAPI/Program.cs:162`) — no manual `dotnet ef database update` needed.

- [ ] **Step 1: Ensure the `dotnet-ef` tool is available**

Run: `dotnet ef --version`
Expected: prints an EF Core CLI version. If it errors with "command not found", run `dotnet tool install --global dotnet-ef` first, then retry.

- [ ] **Step 2: Generate the migration**

Run:
```bash
dotnet ef migrations add AddAmenityBookings --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI
```
Expected: `Done.` and two new files appear under `CondotifyAPI.Infrastructure/Migrations/` (`<timestamp>_AddAmenityBookings.cs` and `.Designer.cs`), plus an updated `DatabaseContextModelSnapshot.cs`.

- [ ] **Step 3: Review the generated migration**

Open the new `<timestamp>_AddAmenityBookings.cs` and confirm it creates exactly four tables (`Amenities`, `AmenityScheduleSlots`, `AmenityBlackouts`, `AmenityBookings`) with the foreign keys and indexes from Task 2 (in particular the partial unique index on `AmenityBookings` filtered by `"Status" IN (0, 1)`, and `OnDelete: Restrict` on the `SlotId` foreign key). If anything is missing, it means a step in Task 2 was skipped — fix the configuration and regenerate (`dotnet ef migrations remove --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI` then repeat Step 2).

- [ ] **Step 4: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI.Infrastructure`
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI.Infrastructure/Migrations/
git commit -m "feat: add EF Core migration for Amenity booking tables"
```

---

### Task 5: Add `ViewBookings`/`ManageBookings` license permissions

**Files:**
- Modify: `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs:1-69`

**Interfaces:**
- Produces: `LicensePermissionEnum.ViewBookings`, `LicensePermissionEnum.ManageBookings` — consumed by Tasks 9, 10, 16.

- [ ] **Step 1: Write the failing test**

In `CondotifyAPI.Tests/LicenseAccessPolicyTests.cs`, add (near the existing `Administrator`/`ForRole` tests):

```csharp
    [Fact]
    public void ManageBookings_ShouldImplyViewBookings()
    {
        var normalized = LicenseAccessDefaults.Normalize(LicensePermissionEnum.ManageBookings);
        Assert.True(normalized.HasFlag(LicensePermissionEnum.ViewBookings));
    }

    [Fact]
    public void Concierge_ShouldBeAbleToManageBookings()
    {
        var permissions = LicenseAccessDefaults.ForRole(LicenseAccessRoleEnum.Concierge);
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ViewBookings));
        Assert.True(permissions.HasFlag(LicensePermissionEnum.ManageBookings));
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAccessPolicyTests"`
Expected: FAIL — `ViewBookings`/`ManageBookings` do not exist yet (compile error).

- [ ] **Step 3: Add the flags and wire them into `Normalize`/`ForRole`**

In `CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`, change:

```csharp
    ViewSettings = 1L << 15,
    ManageSettings = 1L << 16,
    All = (1L << 17) - 1
```

to:

```csharp
    ViewSettings = 1L << 15,
    ManageSettings = 1L << 16,
    ViewBookings = 1L << 17,
    ManageBookings = 1L << 18,
    All = (1L << 19) - 1
```

Then update `ForRole` — add `ViewBookings | ManageBookings` to the `Concierge` case and `ViewBookings` to the `Operator` case and the default case:

```csharp
        LicenseAccessRoleEnum.Concierge => LicensePermissionEnum.ViewDashboard |
            LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewPeople |
            LicensePermissionEnum.ManagePeople | LicensePermissionEnum.ViewCredentials |
            LicensePermissionEnum.ManageCredentials | LicensePermissionEnum.ViewDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ManageDeliveries |
            LicensePermissionEnum.ViewBookings | LicensePermissionEnum.ManageBookings,
        LicenseAccessRoleEnum.Operator => LicensePermissionEnum.ViewDashboard |
            LicensePermissionEnum.ViewStructure | LicensePermissionEnum.ViewPeople |
            LicensePermissionEnum.ViewCredentials | LicensePermissionEnum.ViewDevices |
            LicensePermissionEnum.OperateDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings,
        _ => LicensePermissionEnum.ViewDashboard | LicensePermissionEnum.ViewStructure |
            LicensePermissionEnum.ViewPeople | LicensePermissionEnum.ViewCredentials |
            LicensePermissionEnum.ViewDevices | LicensePermissionEnum.ViewEvents |
            LicensePermissionEnum.ViewDeliveries | LicensePermissionEnum.ViewBookings
```

And add to `Normalize`:

```csharp
        if (permissions.HasFlag(LicensePermissionEnum.ManageBookings)) permissions |= LicensePermissionEnum.ViewBookings;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~LicenseAccessPolicyTests"`
Expected: PASS (all tests in the file, including the two new ones).

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs CondotifyAPI.Tests/LicenseAccessPolicyTests.cs
git commit -m "feat: add ViewBookings/ManageBookings license permissions"
```

---

### Task 6: `AmenityBookingValidator` — pure booking rule logic (TDD)

This is the ordered-validation logic from the spec, written as static, dependency-free functions so they're unit-testable without a database — same style as `AccessRouteResolver`/`AccessRouteResolverTests`.

**Files:**
- Create: `CondotifyAPI/Services/Amenities/AmenityBookingValidator.cs`
- Test: `CondotifyAPI.Tests/AmenityBookingValidatorTests.cs`

**Interfaces:**
- Consumes: `AmenityDTO`, `AmenityBlackoutDTO`, `AmenityBookingStatusEnum` (Task 1).
- Produces: `AmenityBookingValidator.ValidateWindow(AmenityDTO amenity, DateTime date, DateTime nowUtc) -> string?`, `.IsDateBlacked(IEnumerable<AmenityBlackoutDTO> blackouts, DateTime date) -> bool`, `.HasReachedMonthlyLimit(int? monthlyLimit, int existingCountThisMonth) -> bool`, `.CanCancel(AmenityDTO amenity, DateTime date, TimeSpan slotStartTime, DateTime nowUtc) -> bool` — consumed by Task 9.

- [ ] **Step 1: Write the failing tests**

```csharp
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Services.Amenities;
using Xunit;

namespace CondotifyAPI.Tests;

public class AmenityBookingValidatorTests
{
    private static AmenityDTO Amenity(int minAdvanceHours = 0, int maxAdvanceDays = 60, int cancellationCutoffHours = 0) => new()
    {
        MinAdvanceNoticeHours = minAdvanceHours,
        MaxAdvanceDays = maxAdvanceDays,
        CancellationCutoffHours = cancellationCutoffHours
    };

    [Fact]
    public void ValidateWindow_ShouldReject_WhenBelowMinimumAdvanceNotice()
    {
        var amenity = Amenity(minAdvanceHours: 24);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, now);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateWindow_ShouldAccept_WhenAtOrBeyondMinimumAdvanceNotice()
    {
        var amenity = Amenity(minAdvanceHours: 24);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, now);

        Assert.Null(error);
    }

    [Fact]
    public void ValidateWindow_ShouldReject_WhenBeyondMaximumAdvanceDays()
    {
        var amenity = Amenity(maxAdvanceDays: 30);
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, now);

        Assert.NotNull(error);
    }

    [Fact]
    public void ValidateWindow_ShouldReject_WhenDateIsInThePast()
    {
        var amenity = Amenity();
        var now = new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 17, 0, 0, 0, DateTimeKind.Utc);

        var error = AmenityBookingValidator.ValidateWindow(amenity, date, now);

        Assert.NotNull(error);
    }

    [Fact]
    public void IsDateBlacked_ShouldReturnTrue_WhenDateFallsInsideARange()
    {
        var blackouts = new List<AmenityBlackoutDTO>
        {
            new() { StartDate = new DateTime(2026, 12, 24), EndDate = new DateTime(2026, 12, 26) }
        };

        Assert.True(AmenityBookingValidator.IsDateBlacked(blackouts, new DateTime(2026, 12, 25)));
        Assert.False(AmenityBookingValidator.IsDateBlacked(blackouts, new DateTime(2026, 12, 27)));
    }

    [Theory]
    [InlineData(null, 5, false)]
    [InlineData(0, 5, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 1, false)]
    public void HasReachedMonthlyLimit_ShouldCompareAgainstConfiguredLimit(int? limit, int existingCount, bool expected)
    {
        Assert.Equal(expected, AmenityBookingValidator.HasReachedMonthlyLimit(limit, existingCount));
    }

    [Fact]
    public void CanCancel_ShouldReturnFalse_WhenInsideCutoffWindow()
    {
        var amenity = Amenity(cancellationCutoffHours: 24);
        var now = new DateTime(2026, 7, 19, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var slotStart = new TimeSpan(8, 0, 0);

        Assert.False(AmenityBookingValidator.CanCancel(amenity, date, slotStart, now));
    }

    [Fact]
    public void CanCancel_ShouldReturnTrue_WhenBeforeCutoffWindow()
    {
        var amenity = Amenity(cancellationCutoffHours: 24);
        var now = new DateTime(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
        var date = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var slotStart = new TimeSpan(8, 0, 0);

        Assert.True(AmenityBookingValidator.CanCancel(amenity, date, slotStart, now));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~AmenityBookingValidatorTests"`
Expected: FAIL — `AmenityBookingValidator` does not exist yet (compile error).

- [ ] **Step 3: Implement the validator**

```csharp
using CondotifyAPI.Domain.DTO.Amenities;

namespace CondotifyAPI.Services.Amenities;

public static class AmenityBookingValidator
{
    public static string? ValidateWindow(AmenityDTO amenity, DateTime date, DateTime nowUtc)
    {
        var earliestAllowed = nowUtc.AddHours(amenity.MinAdvanceNoticeHours);
        var endOfRequestedDay = date.Date.AddDays(1);

        if (endOfRequestedDay <= earliestAllowed)
            return $"Este local exige ao menos {amenity.MinAdvanceNoticeHours} hora(s) de antecedencia.";

        var latestAllowed = nowUtc.Date.AddDays(amenity.MaxAdvanceDays);
        if (date.Date > latestAllowed)
            return $"Este local so pode ser agendado ate {amenity.MaxAdvanceDays} dia(s) no futuro.";

        return null;
    }

    public static bool IsDateBlacked(IEnumerable<AmenityBlackoutDTO> blackouts, DateTime date)
    {
        var day = date.Date;
        return blackouts.Any(x => day >= x.StartDate.Date && day <= x.EndDate.Date);
    }

    public static bool HasReachedMonthlyLimit(int? monthlyLimit, int existingCountThisMonth)
    {
        if (monthlyLimit is null or <= 0) return false;
        return existingCountThisMonth >= monthlyLimit.Value;
    }

    public static bool CanCancel(AmenityDTO amenity, DateTime date, TimeSpan slotStartTime, DateTime nowUtc)
    {
        var slotStartsAt = date.Date.Add(slotStartTime);
        return nowUtc <= slotStartsAt.AddHours(-amenity.CancellationCutoffHours);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~AmenityBookingValidatorTests"`
Expected: PASS (all 9 tests).

- [ ] **Step 5: Commit**

```bash
git add CondotifyAPI/Services/Amenities/AmenityBookingValidator.cs CondotifyAPI.Tests/AmenityBookingValidatorTests.cs
git commit -m "feat: add AmenityBookingValidator with rule-ordering unit tests"
```

---

### Task 7: EF model tests for the Amenity schema

**Files:**
- Modify: `CondotifyAPI.Tests/DatabaseModelTests.cs` (append new `[Fact]`s near the `AccessRoutes_ShouldBeScopedAndKeepUniqueDevicePortals` test)

**Interfaces:**
- Consumes: the model from Tasks 1-3.

- [ ] **Step 1: Write the tests**

Add to `CondotifyAPI.Tests/DatabaseModelTests.cs` (needs `using CondotifyAPI.Domain.DTO.Amenities;` added to the top of the file alongside the other `using CondotifyAPI.Domain.DTO.*` lines):

```csharp
        [Fact]
        public void Amenities_ShouldBeScopedByLicenseAndUniquelyNamed()
        {
            using var context = CreateContext();
            var entity = context.Model.FindEntityType(typeof(AmenityDTO));

            Assert.NotNull(entity);
            Assert.True(HasUniqueIndex(entity!, nameof(AmenityDTO.LicenseId), nameof(AmenityDTO.Name)));
        }

        [Fact]
        public void AmenityBookings_ShouldPreventDoubleBookingTheSameSlot()
        {
            using var context = CreateContext();
            var booking = context.Model.FindEntityType(typeof(AmenityBookingDTO));

            Assert.NotNull(booking);
            var index = booking!.GetIndexes().Single(x =>
                x.IsUnique &&
                x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(AmenityBookingDTO.AmenityId), nameof(AmenityBookingDTO.SlotId), nameof(AmenityBookingDTO.Date) }));

            Assert.Equal("\"Status\" IN (0, 1)", index.GetFilter());
        }

        [Fact]
        public void AmenityBookings_ShouldRestrictSlotDeletionButCascadeAmenityDeletion()
        {
            using var context = CreateContext();
            var booking = context.Model.FindEntityType(typeof(AmenityBookingDTO));

            Assert.NotNull(booking);
            var slotForeignKey = booking!.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(AmenityScheduleSlotDTO));
            var amenityForeignKey = booking.GetForeignKeys().Single(x => x.PrincipalEntityType.ClrType == typeof(AmenityDTO));

            Assert.Equal(DeleteBehavior.Restrict, slotForeignKey.DeleteBehavior);
            Assert.Equal(DeleteBehavior.Cascade, amenityForeignKey.DeleteBehavior);
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~DatabaseModelTests"`
Expected: FAIL — new facts don't compile/pass until Tasks 1-3 are in place (if Tasks 1-3 are already done at this point in the plan, this instead verifies the configuration is correct; if any assertion fails, fix the configuration from Task 2, not the test).

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~DatabaseModelTests"`
Expected: PASS (all facts in the file, including the 3 new ones).

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI.Tests/DatabaseModelTests.cs
git commit -m "test: assert Amenity booking schema constraints"
```

---

### Task 8: Request/response DTOs for the Amenities API

**Files:**
- Create: `CondotifyAPI/Data/Amenities/AmenityManagementDtos.cs`

**Interfaces:**
- Consumes: `AmenityBookingStatusEnum` (Task 1).
- Produces: `SaveAmenityIn`, `SaveAmenityScheduleSlotIn`, `SaveAmenityBlackoutIn`, `AmenityOut`, `AmenityScheduleSlotOut`, `AmenityBlackoutOut`, `AmenitySlotAvailabilityOut`, `CreateAmenityBookingIn`, `AmenityBookingOut`, `AmenityUnitSearchOut`, `AmenityUnitSearchResidentOut`, `RejectAmenityBookingIn`, `CancelAmenityBookingIn` — consumed by Task 9.

- [ ] **Step 1: Create the file**

```csharp
using CondotifyAPI.Domain.Enums.Amenities;

namespace CondotifyAPI.Data.Amenities;

public sealed class SaveAmenityIn
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; } = true;
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; } = 60;
    public int CancellationCutoffHours { get; set; }
    public List<SaveAmenityScheduleSlotIn> ScheduleSlots { get; set; } = [];
    public List<SaveAmenityBlackoutIn> Blackouts { get; set; } = [];
}

public sealed class SaveAmenityScheduleSlotIn
{
    public Guid? Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; } = true;
}

public sealed class SaveAmenityBlackoutIn
{
    public Guid? Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenityOut
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; }
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; }
    public int CancellationCutoffHours { get; set; }
    public List<AmenityScheduleSlotOut> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutOut> Blackouts { get; set; } = [];
}

public sealed class AmenityScheduleSlotOut
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; }
}

public sealed class AmenityBlackoutOut
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenitySlotAvailabilityOut
{
    public Guid SlotId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Available { get; set; }
    public string? OccupiedByUnitNumber { get; set; }
    public AmenityBookingStatusEnum? OccupiedStatus { get; set; }
    public Guid? BookingId { get; set; }
}

public sealed class CreateAmenityBookingIn
{
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public bool TermsAccepted { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public sealed class RejectAmenityBookingIn
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class CancelAmenityBookingIn
{
    public string Reason { get; set; } = string.Empty;
}

public sealed class AmenityBookingOut
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public string AmenityName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
    public string? ResidentName { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public TimeSpan SlotStartTime { get; set; }
    public TimeSpan SlotEndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? TermsAcceptedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
}

public sealed class AmenityUnitSearchOut
{
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public List<AmenityUnitSearchResidentOut> Residents { get; set; } = [];
}

public sealed class AmenityUnitSearchResidentOut
{
    public Guid ResidentId { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add CondotifyAPI/Data/Amenities/AmenityManagementDtos.cs
git commit -m "feat: add request/response DTOs for the Amenities API"
```

---

### Task 9: `AmenitiesController` — CRUD for locais, schedule slots and blackouts

**Files:**
- Create: `CondotifyAPI/Controllers/AmenitiesController.cs`

**Interfaces:**
- Consumes: `DatabaseContext.Amenities/.AmenityScheduleSlots/.AmenityBlackouts/.AmenityBookings` (Task 3), `SaveAmenityIn`/`AmenityOut`/etc. (Task 8), `LicensePermissionEnum.ViewBookings/.ManageBookings` (Task 5).
- Produces: `GET/POST/PUT/DELETE api/access/licenses/{licenseId}/amenities[/{amenityId}]`, `GET api/access/licenses/{licenseId}/amenities/unit-search?query=` — consumed by Task 12 (frontend API client).

- [ ] **Step 1: Create the controller**

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Amenities;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/amenities")]
[RequireLicensePermission(LicensePermissionEnum.ViewBookings)]
public sealed class AmenitiesController : ControllerBase
{
    private readonly DatabaseContext _context;

    public AmenitiesController(DatabaseContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAmenities(Guid licenseId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenities = await AmenityQuery(licenseId)
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync();

        return Ok(amenities.Select(ToOut));
    }

    [HttpGet("unit-search")]
    public async Task<IActionResult> SearchUnits(Guid licenseId, [FromQuery] string query)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return Ok(Array.Empty<AmenityUnitSearchOut>());

        var normalized = query.Trim();

        var units = await _context.Units
            .AsNoTracking()
            .Include(x => x.Block)
            .Include(x => x.Residents)
            .Where(x => x.Block.LicenseId == licenseId &&
                (x.Number.Contains(normalized) ||
                 x.Residents.Any(r => r.Name.Contains(normalized))))
            .OrderBy(x => x.Block.Name)
            .ThenBy(x => x.Number)
            .Take(15)
            .ToListAsync();

        return Ok(units.Select(x => new AmenityUnitSearchOut
        {
            UnitId = x.Id,
            UnitNumber = x.Number,
            BlockName = x.Block.Name,

            Residents = x.Residents
                .Where(r => r.IsActive)
                .Select(r => new AmenityUnitSearchResidentOut { ResidentId = r.Id, Name = r.Name })
                .ToList()
        }));
    }

    [HttpPost]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> CreateAmenity(Guid licenseId, [FromBody] SaveAmenityIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = Validate(input);
        if (validation is not null)
            return BadRequest(new { Result = "InvalidAmenity", Errors = validation });

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.Amenities
            .AsNoTracking()
            .AnyAsync(x => x.LicenseId == licenseId && x.Name == normalizedName);

        if (duplicateExists)
            return Conflict(new { Result = "DuplicateAmenity", Errors = "Ja existe um local com este nome." });

        var now = DateTime.UtcNow;
        var amenity = new AmenityDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyAmenity(amenity, input);
        amenity.ScheduleSlots = input.ScheduleSlots.Select(x => NewSlot(amenity.Id, x)).ToList();
        amenity.Blackouts = input.Blackouts.Select(x => NewBlackout(amenity.Id, x)).ToList();

        _context.Amenities.Add(amenity);
        await _context.SaveChangesAsync();

        var created = await AmenityQuery(licenseId).AsNoTracking().FirstAsync(x => x.Id == amenity.Id);
        return Created(string.Empty, ToOut(created));
    }

    [HttpPut("{amenityId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> UpdateAmenity(Guid licenseId, Guid amenityId, [FromBody] SaveAmenityIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var validation = Validate(input);
        if (validation is not null)
            return BadRequest(new { Result = "InvalidAmenity", Errors = validation });

        var normalizedName = input.Name.Trim();

        var duplicateExists = await _context.Amenities
            .AsNoTracking()
            .AnyAsync(x => x.LicenseId == licenseId && x.Id != amenityId && x.Name == normalizedName);

        if (duplicateExists)
            return Conflict(new { Result = "DuplicateAmenity", Errors = "Ja existe um local com este nome." });

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        ApplyAmenity(amenity, input);
        amenity.UpdatedAt = DateTime.UtcNow;

        await ReplaceScheduleSlotsAsync(amenity, input.ScheduleSlots);
        ReplaceBlackouts(amenity, input.Blackouts);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new
            {
                Result = "SlotHasBookings",
                Errors = "Um dos horarios removidos possui agendamentos e nao pode ser excluido. Ele foi mantido como inativo."
            });
        }

        var updated = await AmenityQuery(licenseId).AsNoTracking().FirstAsync(x => x.Id == amenityId);
        return Ok(ToOut(updated));
    }

    [HttpDelete("{amenityId:guid}")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> DeleteAmenity(Guid licenseId, Guid amenityId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        _context.Amenities.Remove(amenity);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<AmenityDTO> AmenityQuery(Guid licenseId)
    {
        return _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .Where(x => x.LicenseId == licenseId);
    }

    private static string? Validate(SaveAmenityIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Informe o nome do local.";

        if (input.Name.Trim().Length > 120)
            return "O nome do local deve ter ate 120 caracteres.";

        if (input.Capacity is < 1)
            return "A capacidade deve ser maior que zero.";

        if (input.MonthlyLimitPerUnit is < 0)
            return "O limite mensal por unidade nao pode ser negativo.";

        if (input.MinAdvanceNoticeHours < 0 || input.CancellationCutoffHours < 0)
            return "Os prazos de antecedencia e cancelamento nao podem ser negativos.";

        if (input.MaxAdvanceDays < 1)
            return "A janela maxima de agendamento deve ser de ao menos 1 dia.";

        if (input.RequiresTermsAcceptance && string.IsNullOrWhiteSpace(input.TermsText))
            return "Informe o texto do termo de uso.";

        foreach (var slot in input.ScheduleSlots)
        {
            if (slot.StartTime < TimeSpan.Zero || slot.EndTime > TimeSpan.FromDays(1) || slot.StartTime >= slot.EndTime)
                return "Existe um horario invalido na grade semanal.";
        }

        foreach (var blackout in input.Blackouts)
        {
            if (blackout.EndDate.Date < blackout.StartDate.Date)
                return "Existe um bloqueio de data invalido.";
        }

        return null;
    }

    private static void ApplyAmenity(AmenityDTO amenity, SaveAmenityIn input)
    {
        amenity.Name = input.Name.Trim();
        amenity.Description = input.Description?.Trim() ?? string.Empty;
        amenity.Capacity = input.Capacity;
        amenity.Active = input.Active;
        amenity.FeeAmount = input.FeeAmount;
        amenity.FeeDescription = input.FeeDescription?.Trim() ?? string.Empty;
        amenity.RequiresApproval = input.RequiresApproval;
        amenity.RequiresTermsAcceptance = input.RequiresTermsAcceptance;
        amenity.TermsText = input.TermsText?.Trim() ?? string.Empty;
        amenity.MonthlyLimitPerUnit = input.MonthlyLimitPerUnit;
        amenity.MinAdvanceNoticeHours = input.MinAdvanceNoticeHours;
        amenity.MaxAdvanceDays = input.MaxAdvanceDays;
        amenity.CancellationCutoffHours = input.CancellationCutoffHours;
    }

    private static AmenityScheduleSlotDTO NewSlot(Guid amenityId, SaveAmenityScheduleSlotIn input) => new()
    {
        Id = Guid.NewGuid(),
        AmenityId = amenityId,
        DayOfWeek = input.DayOfWeek,
        StartTime = input.StartTime,
        EndTime = input.EndTime,
        Active = input.Active
    };

    private static AmenityBlackoutDTO NewBlackout(Guid amenityId, SaveAmenityBlackoutIn input) => new()
    {
        Id = Guid.NewGuid(),
        AmenityId = amenityId,
        StartDate = input.StartDate.Date,
        EndDate = input.EndDate.Date,
        Reason = input.Reason?.Trim() ?? string.Empty
    };

    /// <summary>
    /// Upserts schedule slots by Id. A slot removed from the payload is
    /// hard-deleted only when it has zero bookings ever; otherwise it is
    /// kept but marked inactive, so booking history never loses its
    /// referenced slot (see the Restrict FK in AmenityBookingConfiguration).
    /// </summary>
    private async Task ReplaceScheduleSlotsAsync(AmenityDTO amenity, List<SaveAmenityScheduleSlotIn> incoming)
    {
        var incomingIds = incoming.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();

        foreach (var existing in amenity.ScheduleSlots.ToList())
        {
            if (incomingIds.Contains(existing.Id))
                continue;

            var hasBookings = await _context.AmenityBookings.AsNoTracking().AnyAsync(x => x.SlotId == existing.Id);
            if (hasBookings)
                existing.Active = false;
            else
                amenity.ScheduleSlots.Remove(existing);
        }

        foreach (var input in incoming)
        {
            if (input.Id.HasValue)
            {
                var existing = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.Id.Value);
                if (existing is null) continue;
                existing.DayOfWeek = input.DayOfWeek;
                existing.StartTime = input.StartTime;
                existing.EndTime = input.EndTime;
                existing.Active = input.Active;
            }
            else
            {
                amenity.ScheduleSlots.Add(NewSlot(amenity.Id, input));
            }
        }
    }

    private static void ReplaceBlackouts(AmenityDTO amenity, List<SaveAmenityBlackoutIn> incoming)
    {
        var incomingIds = incoming.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToHashSet();

        foreach (var existing in amenity.Blackouts.Where(x => !incomingIds.Contains(x.Id)).ToList())
            amenity.Blackouts.Remove(existing);

        foreach (var input in incoming)
        {
            if (input.Id.HasValue)
            {
                var existing = amenity.Blackouts.FirstOrDefault(x => x.Id == input.Id.Value);
                if (existing is null) continue;
                existing.StartDate = input.StartDate.Date;
                existing.EndDate = input.EndDate.Date;
                existing.Reason = input.Reason?.Trim() ?? string.Empty;
            }
            else
            {
                amenity.Blackouts.Add(NewBlackout(amenity.Id, input));
            }
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static AmenityOut ToOut(AmenityDTO amenity) => new()
    {
        Id = amenity.Id,
        Name = amenity.Name,
        Description = amenity.Description,
        Capacity = amenity.Capacity,
        Active = amenity.Active,
        FeeAmount = amenity.FeeAmount,
        FeeDescription = amenity.FeeDescription,
        RequiresApproval = amenity.RequiresApproval,
        RequiresTermsAcceptance = amenity.RequiresTermsAcceptance,
        TermsText = amenity.TermsText,
        MonthlyLimitPerUnit = amenity.MonthlyLimitPerUnit,
        MinAdvanceNoticeHours = amenity.MinAdvanceNoticeHours,
        MaxAdvanceDays = amenity.MaxAdvanceDays,
        CancellationCutoffHours = amenity.CancellationCutoffHours,

        ScheduleSlots = amenity.ScheduleSlots
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new AmenityScheduleSlotOut { Id = x.Id, DayOfWeek = x.DayOfWeek, StartTime = x.StartTime, EndTime = x.EndTime, Active = x.Active })
            .ToList(),

        Blackouts = amenity.Blackouts
            .OrderBy(x => x.StartDate)
            .Select(x => new AmenityBlackoutOut { Id = x.Id, StartDate = x.StartDate, EndDate = x.EndDate, Reason = x.Reason })
            .ToList()
    };

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return false;

        return await _context.Licenses
            .AsNoTracking()
            .AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add CondotifyAPI/Controllers/AmenitiesController.cs
git commit -m "feat: add AmenitiesController for managing common-area locations"
```

---

### Task 10: `AmenityBookingsController` — availability, create, approve/reject, cancel

**Files:**
- Create: `CondotifyAPI/Controllers/AmenityBookingsController.cs`

**Interfaces:**
- Consumes: `AmenityBookingValidator` (Task 6), `DatabaseContext` DbSets (Task 3), DTOs (Task 8), `LicensePermissionEnum` (Task 5).
- Produces: `GET .../amenities/{amenityId}/bookings/availability?date=`, `POST .../amenities/{amenityId}/bookings`, `PUT .../bookings/{bookingId}/approve`, `PUT .../bookings/{bookingId}/reject`, `DELETE .../bookings/{bookingId}` — consumed by Task 12.

- [ ] **Step 1: Create the controller**

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Amenities;
using CondotifyAPI.Domain.DTO.Amenities;
using CondotifyAPI.Domain.Enums.Amenities;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Amenities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/amenities/{amenityId:guid}/bookings")]
[RequireLicensePermission(LicensePermissionEnum.ViewBookings)]
public sealed class AmenityBookingsController : ControllerBase
{
    private readonly DatabaseContext _context;

    public AmenityBookingsController(DatabaseContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetBookings(Guid licenseId, Guid amenityId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var rangeStart = (from ?? DateTime.UtcNow.Date).Date;
        var rangeEnd = (to ?? rangeStart.AddDays(30)).Date;

        var bookings = await BookingQuery(licenseId, amenityId)
            .AsNoTracking()
            .Where(x => x.Date >= rangeStart && x.Date <= rangeEnd)
            .OrderBy(x => x.Date).ThenBy(x => x.Slot.StartTime)
            .ToListAsync();

        return Ok(bookings.Select(ToOut));
    }

    [HttpGet("availability")]
    public async Task<IActionResult> GetAvailability(Guid licenseId, Guid amenityId, [FromQuery] DateTime date)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .AsNoTracking()
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        var day = date.Date;

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, day))
            return Ok(Array.Empty<AmenitySlotAvailabilityOut>());

        var slots = amenity.ScheduleSlots
            .Where(x => x.Active && x.DayOfWeek == day.DayOfWeek)
            .OrderBy(x => x.StartTime)
            .ToList();

        var activeBookings = await _context.AmenityBookings
            .AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => x.AmenityId == amenityId && x.Date == day &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed))
            .ToListAsync();

        return Ok(slots.Select(slot =>
        {
            var occupiedBy = activeBookings.FirstOrDefault(x => x.SlotId == slot.Id);
            return new AmenitySlotAvailabilityOut
            {
                SlotId = slot.Id,
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Available = occupiedBy is null,
                OccupiedByUnitNumber = occupiedBy?.Unit.Number,
                OccupiedStatus = occupiedBy?.Status,
                BookingId = occupiedBy?.Id
            };
        }));
    }

    [HttpPost]
    public async Task<IActionResult> CreateBooking(Guid licenseId, Guid amenityId, [FromBody] CreateAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var amenity = await _context.Amenities
            .Include(x => x.ScheduleSlots)
            .Include(x => x.Blackouts)
            .FirstOrDefaultAsync(x => x.Id == amenityId && x.LicenseId == licenseId);

        if (amenity is null)
            return NotFound();

        if (!amenity.Active)
            return BadRequest(new { Result = "AmenityInactive", Errors = "Este local nao esta disponivel para agendamento." });

        var unitExists = await _context.Units.AsNoTracking().AnyAsync(x => x.Id == input.UnitId && x.Block.LicenseId == licenseId);
        if (!unitExists)
            return BadRequest(new { Result = "InvalidUnit", Errors = "A unidade informada nao pertence a esta licenca." });

        var slot = amenity.ScheduleSlots.FirstOrDefault(x => x.Id == input.SlotId && x.Active);
        if (slot is null || slot.DayOfWeek != input.Date.DayOfWeek)
            return BadRequest(new { Result = "InvalidSlot", Errors = "O horario selecionado nao existe para este local nesta data." });

        var now = DateTime.UtcNow;

        var windowError = AmenityBookingValidator.ValidateWindow(amenity, input.Date, now);
        if (windowError is not null)
            return BadRequest(new { Result = "OutsideBookingWindow", Errors = windowError });

        if (AmenityBookingValidator.IsDateBlacked(amenity.Blackouts, input.Date))
            return BadRequest(new { Result = "DateBlacked", Errors = "Esta data nao esta disponivel para este local." });

        var monthStart = new DateTime(input.Date.Year, input.Date.Month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var existingThisMonth = await _context.AmenityBookings
            .AsNoTracking()
            .CountAsync(x => x.AmenityId == amenityId && x.UnitId == input.UnitId &&
                x.Date >= monthStart && x.Date < monthEnd &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));

        if (AmenityBookingValidator.HasReachedMonthlyLimit(amenity.MonthlyLimitPerUnit, existingThisMonth))
            return BadRequest(new { Result = "MonthlyLimitReached", Errors = "Esta unidade ja atingiu o limite mensal de agendamentos para este local." });

        if (amenity.RequiresTermsAcceptance && !input.TermsAccepted)
            return BadRequest(new { Result = "TermsNotAccepted", Errors = "E necessario aceitar o termo de uso para agendar este local." });

        var slotTaken = await _context.AmenityBookings
            .AsNoTracking()
            .AnyAsync(x => x.AmenityId == amenityId && x.SlotId == input.SlotId && x.Date == input.Date.Date &&
                (x.Status == AmenityBookingStatusEnum.Pending || x.Status == AmenityBookingStatusEnum.Confirmed));

        if (slotTaken)
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });

        var currentUserId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : Guid.Empty;

        var booking = new AmenityBookingDTO
        {
            Id = Guid.NewGuid(),
            AmenityId = amenityId,
            LicenseId = licenseId,
            UnitId = input.UnitId,
            ResidentId = input.ResidentId,
            Date = input.Date.Date,
            SlotId = input.SlotId,
            Status = amenity.RequiresApproval ? AmenityBookingStatusEnum.Pending : AmenityBookingStatusEnum.Confirmed,
            TermsAcceptedAt = amenity.RequiresTermsAcceptance ? now : null,
            Notes = input.Notes?.Trim() ?? string.Empty,
            CreatedByUserId = currentUserId,
            CreatedAt = now
        };

        _context.AmenityBookings.Add(booking);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Conflict(new { Result = "SlotTaken", Errors = "Este horario ja foi reservado para esta data." });
        }

        var created = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == booking.Id);
        return Created(string.Empty, ToOut(created));
    }

    [HttpPut("{bookingId:guid}/approve")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> ApproveBooking(Guid licenseId, Guid amenityId, Guid bookingId)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status != AmenityBookingStatusEnum.Pending)
            return Conflict(new { Result = "NotPending", Errors = "Apenas agendamentos pendentes podem ser aprovados." });

        booking.Status = AmenityBookingStatusEnum.Confirmed;
        await _context.SaveChangesAsync();

        var updated = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == bookingId);
        return Ok(ToOut(updated));
    }

    [HttpPut("{bookingId:guid}/reject")]
    [RequireLicensePermission(LicensePermissionEnum.ManageBookings)]
    public async Task<IActionResult> RejectBooking(Guid licenseId, Guid amenityId, Guid bookingId, [FromBody] RejectAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status != AmenityBookingStatusEnum.Pending)
            return Conflict(new { Result = "NotPending", Errors = "Apenas agendamentos pendentes podem ser recusados." });

        booking.Status = AmenityBookingStatusEnum.Rejected;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = input.Reason?.Trim() ?? string.Empty;
        await _context.SaveChangesAsync();

        var updated = await BookingQuery(licenseId, amenityId).AsNoTracking().FirstAsync(x => x.Id == bookingId);
        return Ok(ToOut(updated));
    }

    [HttpDelete("{bookingId:guid}")]
    public async Task<IActionResult> CancelBooking(Guid licenseId, Guid amenityId, Guid bookingId, [FromBody] CancelAmenityBookingIn input)
    {
        if (!await HasLicenseAccessAsync(licenseId))
            return NotFound();

        var booking = await _context.AmenityBookings
            .Include(x => x.Amenity)
            .Include(x => x.Slot)
            .FirstOrDefaultAsync(x => x.Id == bookingId && x.AmenityId == amenityId && x.LicenseId == licenseId);

        if (booking is null)
            return NotFound();

        if (booking.Status is AmenityBookingStatusEnum.Cancelled or AmenityBookingStatusEnum.Rejected or AmenityBookingStatusEnum.Completed)
            return Conflict(new { Result = "AlreadyClosed", Errors = "Este agendamento ja nao esta mais ativo." });

        if (!AmenityBookingValidator.CanCancel(booking.Amenity, booking.Date, booking.Slot.StartTime, DateTime.UtcNow))
        {
            return Conflict(new
            {
                Result = "PastCancellationCutoff",
                Errors = $"Este agendamento so pode ser cancelado ate {booking.Amenity.CancellationCutoffHours} hora(s) antes do horario."
            });
        }

        booking.Status = AmenityBookingStatusEnum.Cancelled;
        booking.CancelledAt = DateTime.UtcNow;
        booking.CancelReason = input.Reason?.Trim() ?? string.Empty;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private IQueryable<AmenityBookingDTO> BookingQuery(Guid licenseId, Guid amenityId)
    {
        return _context.AmenityBookings
            .Include(x => x.Unit).ThenInclude(x => x.Block)
            .Include(x => x.Resident)
            .Include(x => x.Slot)
            .Where(x => x.LicenseId == licenseId && x.AmenityId == amenityId);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };

    private static AmenityBookingOut ToOut(AmenityBookingDTO booking) => new()
    {
        Id = booking.Id,
        AmenityId = booking.AmenityId,
        AmenityName = booking.Amenity?.Name ?? string.Empty,
        UnitId = booking.UnitId,
        UnitNumber = booking.Unit.Number,
        BlockName = booking.Unit.Block.Name,
        ResidentId = booking.ResidentId,
        ResidentName = booking.Resident?.Name,
        Date = booking.Date,
        SlotId = booking.SlotId,
        SlotStartTime = booking.Slot.StartTime,
        SlotEndTime = booking.Slot.EndTime,
        Status = booking.Status.ToString(),
        TermsAcceptedAt = booking.TermsAcceptedAt,
        Notes = booking.Notes,
        CreatedAt = booking.CreatedAt,
        CancelledAt = booking.CancelledAt,
        CancelReason = booking.CancelReason
    };

    private async Task<bool> HasLicenseAccessAsync(Guid licenseId)
    {
        var enterpriseClaim = User.FindFirstValue("enterprise_id");
        if (!Guid.TryParse(enterpriseClaim, out var enterpriseId))
            return false;

        return await _context.Licenses
            .AsNoTracking()
            .AnyAsync(x => x.Id == licenseId && x.EnterpriseId == enterpriseId);
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build CondotifyAPI`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add CondotifyAPI/Controllers/AmenityBookingsController.cs
git commit -m "feat: add AmenityBookingsController for availability and booking lifecycle"
```

---

### Task 11: Run the full backend test suite

**Files:** none (verification task)

- [ ] **Step 1: Run all backend tests**

Run: `dotnet test CondotifyAPI.Tests`
Expected: all tests pass, including every pre-existing test (no regressions from the `LicensePermissionEnum.All` bit-width change or the new Amenity model).

- [ ] **Step 2: If anything fails, fix forward**

If `LicenseAccessPolicyTests` or `DatabaseModelTests` fail, re-check Tasks 2, 5 and 7 — do not weaken assertions to make them pass.

---

### Task 12: Frontend view models for Amenities

**Files:**
- Create: `Condotify/Models/AmenityViewModels.cs`

**Interfaces:**
- Produces: `AmenityViewModel`, `AmenityScheduleSlotViewModel`, `AmenityBlackoutViewModel`, `AmenityFormViewModel`, `AmenityScheduleSlotFormViewModel`, `AmenityBlackoutFormViewModel`, `AmenitySlotAvailabilityViewModel`, `AmenityBookingViewModel`, `AmenityBookingFormViewModel`, `AmenityUnitSearchViewModel`, `AmenityUnitSearchResidentViewModel` — consumed by Tasks 13, 14, 15, 16.

- [ ] **Step 1: Create the file**

```csharp
using System.ComponentModel.DataAnnotations;

namespace Condotify.Models;

public class AmenityViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; }
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; }
    public int CancellationCutoffHours { get; set; }
    public List<AmenityScheduleSlotViewModel> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutViewModel> Blackouts { get; set; } = [];
}

public class AmenityScheduleSlotViewModel
{
    public Guid Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Active { get; set; }
}

public class AmenityBlackoutViewModel
{
    public Guid Id { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class AmenityFormViewModel
{
    [Required] public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public bool Active { get; set; } = true;
    public decimal? FeeAmount { get; set; }
    public string FeeDescription { get; set; } = string.Empty;
    public bool RequiresApproval { get; set; }
    public bool RequiresTermsAcceptance { get; set; }
    public string TermsText { get; set; } = string.Empty;
    public int? MonthlyLimitPerUnit { get; set; }
    public int MinAdvanceNoticeHours { get; set; }
    public int MaxAdvanceDays { get; set; } = 60;
    public int CancellationCutoffHours { get; set; }
    public List<AmenityScheduleSlotFormViewModel> ScheduleSlots { get; set; } = [];
    public List<AmenityBlackoutFormViewModel> Blackouts { get; set; } = [];
}

public class AmenityScheduleSlotFormViewModel
{
    public Guid? Id { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; } = new(8, 0, 0);
    public TimeSpan EndTime { get; set; } = new(14, 0, 0);
    public bool Active { get; set; } = true;
}

public class AmenityBlackoutFormViewModel
{
    public Guid? Id { get; set; }
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime EndDate { get; set; } = DateTime.Today;
    public string Reason { get; set; } = string.Empty;
}

public class AmenitySlotAvailabilityViewModel
{
    public Guid SlotId { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool Available { get; set; }
    public string? OccupiedByUnitNumber { get; set; }
    public string? OccupiedStatus { get; set; }
    public Guid? BookingId { get; set; }
}

public class AmenityBookingViewModel
{
    public Guid Id { get; set; }
    public Guid AmenityId { get; set; }
    public string AmenityName { get; set; } = string.Empty;
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public Guid? ResidentId { get; set; }
    public string? ResidentName { get; set; }
    public DateTime Date { get; set; }
    public Guid SlotId { get; set; }
    public TimeSpan SlotStartTime { get; set; }
    public TimeSpan SlotEndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? TermsAcceptedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string CancelReason { get; set; } = string.Empty;
}

public class AmenityBookingFormViewModel
{
    public Guid UnitId { get; set; }
    public Guid? ResidentId { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public Guid SlotId { get; set; }
    public bool TermsAccepted { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class AmenityUnitSearchViewModel
{
    public Guid UnitId { get; set; }
    public string UnitNumber { get; set; } = string.Empty;
    public string BlockName { get; set; } = string.Empty;
    public List<AmenityUnitSearchResidentViewModel> Residents { get; set; } = [];
}

public class AmenityUnitSearchResidentViewModel
{
    public Guid ResidentId { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build Condotify`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Condotify/Models/AmenityViewModels.cs
git commit -m "feat: add frontend view models for Amenities"
```

---

### Task 13: `CondotifyApiClient` methods for Amenities

**Files:**
- Modify: `Condotify/Services/CondotifyApiClient.cs` (append new methods near the existing `GetAccessRoutesAsync`/`SaveAccessRouteAsync`/`DeleteAccessRouteAsync` group, around line 393)

**Interfaces:**
- Consumes: view models from Task 12.
- Produces: `GetAmenitiesAsync`, `SaveAmenityAsync`, `DeleteAmenityAsync`, `SearchAmenityUnitsAsync`, `GetAmenityAvailabilityAsync`, `GetAmenityBookingsAsync`, `CreateAmenityBookingAsync`, `ApproveAmenityBookingAsync`, `RejectAmenityBookingAsync`, `CancelAmenityBookingAsync` — consumed by Tasks 14, 15, 16.

- [ ] **Step 1: Add the methods**

Insert after `DeleteAccessRouteAsync` (`Condotify/Services/CondotifyApiClient.cs:393-394`):

```csharp
    public Task<ApiResult<List<AmenityViewModel>>> GetAmenitiesAsync(Guid licenseId, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityViewModel>>($"api/access/licenses/{licenseId}/amenities", cancellationToken);

    public Task<ApiResult<List<AmenityUnitSearchViewModel>>> SearchAmenityUnitsAsync(Guid licenseId, string query, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityUnitSearchViewModel>>($"api/access/licenses/{licenseId}/amenities/unit-search?query={Uri.EscapeDataString(query)}", cancellationToken);

    public Task<ApiResult<AmenityViewModel>> SaveAmenityAsync(Guid licenseId, Guid? amenityId, AmenityFormViewModel model, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            model.Capacity,
            model.Active,
            model.FeeAmount,
            FeeDescription = model.FeeDescription.Trim(),
            model.RequiresApproval,
            model.RequiresTermsAcceptance,
            TermsText = model.TermsText.Trim(),
            model.MonthlyLimitPerUnit,
            model.MinAdvanceNoticeHours,
            model.MaxAdvanceDays,
            model.CancellationCutoffHours,
            ScheduleSlots = model.ScheduleSlots.Select(x => new { x.Id, x.DayOfWeek, x.StartTime, x.EndTime, x.Active }),
            Blackouts = model.Blackouts.Select(x => new { x.Id, x.StartDate, x.EndDate, Reason = x.Reason.Trim() })
        };
        return SendForAsync<AmenityViewModel>(amenityId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            amenityId.HasValue ? $"api/access/licenses/{licenseId}/amenities/{amenityId}" : $"api/access/licenses/{licenseId}/amenities",
            payload,
            cancellationToken);
    }

    public Task<ApiResult<bool>> DeleteAmenityAsync(Guid licenseId, Guid amenityId, CancellationToken cancellationToken = default) =>
        DeleteAsync($"api/access/licenses/{licenseId}/amenities/{amenityId}", cancellationToken);

    public Task<ApiResult<List<AmenitySlotAvailabilityViewModel>>> GetAmenityAvailabilityAsync(Guid licenseId, Guid amenityId, DateTime date, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenitySlotAvailabilityViewModel>>($"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/availability?date={date:yyyy-MM-dd}", cancellationToken);

    public Task<ApiResult<List<AmenityBookingViewModel>>> GetAmenityBookingsAsync(Guid licenseId, Guid amenityId, DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        GetAsync<List<AmenityBookingViewModel>>($"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> CreateAmenityBookingAsync(Guid licenseId, Guid amenityId, AmenityBookingFormViewModel model, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Post, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings", new
        {
            model.UnitId,
            model.ResidentId,
            model.Date,
            model.SlotId,
            model.TermsAccepted,
            Notes = model.Notes.Trim()
        }, cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> ApproveAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Put, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}/approve", new { }, cancellationToken);

    public Task<ApiResult<AmenityBookingViewModel>> RejectAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, string reason, CancellationToken cancellationToken = default) =>
        SendForAsync<AmenityBookingViewModel>(HttpMethod.Put, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}/reject", new { Reason = reason }, cancellationToken);

    public Task<ApiResult<bool>> CancelAmenityBookingAsync(Guid licenseId, Guid amenityId, Guid bookingId, string reason, CancellationToken cancellationToken = default) =>
        SendForBoolAsync(HttpMethod.Delete, $"api/access/licenses/{licenseId}/amenities/{amenityId}/bookings/{bookingId}", new { Reason = reason }, cancellationToken);
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build Condotify`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Condotify/Services/CondotifyApiClient.cs
git commit -m "feat: add CondotifyApiClient methods for Amenities"
```

---

### Task 14: `AmenityFormDialog.razor` — create/edit a local

**Files:**
- Create: `Condotify/Components/Dialogs/AmenityFormDialog.razor`

**Interfaces:**
- Consumes: `AmenityViewModel`, `AmenityFormViewModel`, `AmenityScheduleSlotFormViewModel`, `AmenityBlackoutFormViewModel` (Task 12), `Api.SaveAmenityAsync` (Task 13).
- Produces: a dialog invoked by Task 15 with `Dialog.Close(DialogResult.Ok(AmenityViewModel))` on success.

- [ ] **Step 1: Create the dialog**

```razor
@inject CondotifyApiClient Api

<MudDialog Class="entity-dialog">
    <TitleContent>
        <div class="dialog-title-block">
            <MudIcon Icon="@Icons.Material.Outlined.Deck" />
            <div><MudText Typo="Typo.h6">@(Amenity is null ? "Novo local" : "Editar local")</MudText><MudText Typo="Typo.caption">Defina as regras de agendamento deste local.</MudText></div>
        </div>
    </TitleContent>
    <DialogContent>
        @if (!string.IsNullOrWhiteSpace(_error))
        {
            <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert>
        }
        <EditForm Model="_form" OnValidSubmit="SaveAsync">
            <DataAnnotationsValidator />
            <div class="form-grid form-grid-two">
                <MudTextField T="string" @bind-Value="_form.Name" Label="Nome do local" Required Variant="Variant.Outlined" />
                <MudNumericField T="int?" @bind-Value="_form.Capacity" Label="Capacidade (pessoas)" Min="1" Variant="Variant.Outlined" />
                <MudTextField T="string" @bind-Value="_form.Description" Label="Descricao" Variant="Variant.Outlined" Class="form-grid-span-2" />
                <MudNumericField T="decimal?" @bind-Value="_form.FeeAmount" Label="Taxa (R$, informativo)" Variant="Variant.Outlined" />
                <MudTextField T="string" @bind-Value="_form.FeeDescription" Label="Observacao sobre a taxa" Variant="Variant.Outlined" />
            </div>

            <div class="visit-policy-options">
                <MudSwitch T="bool" @bind-Value="_form.Active" Color="Color.Primary" Label="Local ativo" />
                <MudSwitch T="bool" @bind-Value="_form.RequiresApproval" Color="Color.Primary" Label="Exige aprovacao manual" />
                <MudSwitch T="bool" @bind-Value="_form.RequiresTermsAcceptance" Color="Color.Primary" Label="Exige aceite de termo de uso" />
            </div>
            @if (_form.RequiresTermsAcceptance)
            {
                <MudTextField T="string" @bind-Value="_form.TermsText" Label="Texto do termo de uso" Lines="4" Variant="Variant.Outlined" />
            }

            <div class="form-grid form-grid-three">
                <MudNumericField T="int?" @bind-Value="_form.MonthlyLimitPerUnit" Label="Limite mensal por unidade" Min="0" Variant="Variant.Outlined" HelperText="0 ou vazio = sem limite" />
                <MudNumericField T="int" @bind-Value="_form.MinAdvanceNoticeHours" Label="Antecedencia minima (horas)" Min="0" Variant="Variant.Outlined" />
                <MudNumericField T="int" @bind-Value="_form.MaxAdvanceDays" Label="Janela maxima (dias)" Min="1" Variant="Variant.Outlined" />
                <MudNumericField T="int" @bind-Value="_form.CancellationCutoffHours" Label="Prazo de cancelamento (horas)" Min="0" Variant="Variant.Outlined" />
            </div>

            <div class="form-section-heading"><span>2</span><div><strong>Grade semanal de horarios</strong><small>Defina os horarios disponiveis por dia da semana.</small></div></div>
            @foreach (var slot in _form.ScheduleSlots)
            {
                <div class="form-grid form-grid-three">
                    <MudSelect T="DayOfWeek" Value="slot.DayOfWeek" ValueChanged="@(v => slot.DayOfWeek = v)" Label="Dia da semana" Variant="Variant.Outlined">
                        @foreach (var day in Enum.GetValues<DayOfWeek>())
                        {
                            <MudSelectItem Value="day">@DayLabel(day)</MudSelectItem>
                        }
                    </MudSelect>
                    <MudTimePicker Time="slot.StartTime" TimeChanged="@(v => slot.StartTime = v ?? slot.StartTime)" Label="Inicio" Variant="Variant.Outlined" />
                    <MudTimePicker Time="slot.EndTime" TimeChanged="@(v => slot.EndTime = v ?? slot.EndTime)" Label="Fim" Variant="Variant.Outlined" />
                    <MudIconButton Icon="@Icons.Material.Outlined.DeleteOutline" OnClick="@(() => _form.ScheduleSlots.Remove(slot))" />
                </div>
            }
            <MudButton Variant="Variant.Text" StartIcon="@Icons.Material.Outlined.Add" OnClick="AddSlot">Adicionar horario</MudButton>

            <div class="form-section-heading"><span>3</span><div><strong>Bloqueios de data</strong><small>Datas indisponiveis para manutencao, feriados ou obras.</small></div></div>
            @foreach (var blackout in _form.Blackouts)
            {
                <div class="form-grid form-grid-three">
                    <MudDatePicker Date="blackout.StartDate" DateChanged="@(v => blackout.StartDate = v ?? blackout.StartDate)" Label="Data inicial" Variant="Variant.Outlined" />
                    <MudDatePicker Date="blackout.EndDate" DateChanged="@(v => blackout.EndDate = v ?? blackout.EndDate)" Label="Data final" Variant="Variant.Outlined" />
                    <MudTextField T="string" @bind-Value="blackout.Reason" Label="Motivo" Variant="Variant.Outlined" />
                    <MudIconButton Icon="@Icons.Material.Outlined.DeleteOutline" OnClick="@(() => _form.Blackouts.Remove(blackout))" />
                </div>
            }
            <MudButton Variant="Variant.Text" StartIcon="@Icons.Material.Outlined.Add" OnClick="AddBlackout">Adicionar bloqueio</MudButton>

            <div class="form-actions dialog-form-actions">
                <MudButton Variant="Variant.Text" Disabled="_saving" OnClick="Cancel">Cancelar</MudButton>
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Disabled="_saving">@(_saving ? "Salvando..." : "Salvar local")</MudButton>
            </div>
        </EditForm>
    </DialogContent>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter] public AmenityViewModel? Amenity { get; set; }

    private readonly AmenityFormViewModel _form = new();
    private bool _saving;
    private string? _error;

    protected override void OnInitialized()
    {
        if (Amenity is null) return;
        _form.Name = Amenity.Name;
        _form.Description = Amenity.Description;
        _form.Capacity = Amenity.Capacity;
        _form.Active = Amenity.Active;
        _form.FeeAmount = Amenity.FeeAmount;
        _form.FeeDescription = Amenity.FeeDescription;
        _form.RequiresApproval = Amenity.RequiresApproval;
        _form.RequiresTermsAcceptance = Amenity.RequiresTermsAcceptance;
        _form.TermsText = Amenity.TermsText;
        _form.MonthlyLimitPerUnit = Amenity.MonthlyLimitPerUnit;
        _form.MinAdvanceNoticeHours = Amenity.MinAdvanceNoticeHours;
        _form.MaxAdvanceDays = Amenity.MaxAdvanceDays;
        _form.CancellationCutoffHours = Amenity.CancellationCutoffHours;
        _form.ScheduleSlots = Amenity.ScheduleSlots.Select(x => new AmenityScheduleSlotFormViewModel { Id = x.Id, DayOfWeek = x.DayOfWeek, StartTime = x.StartTime, EndTime = x.EndTime, Active = x.Active }).ToList();
        _form.Blackouts = Amenity.Blackouts.Select(x => new AmenityBlackoutFormViewModel { Id = x.Id, StartDate = x.StartDate, EndDate = x.EndDate, Reason = x.Reason }).ToList();
    }

    private void AddSlot() => _form.ScheduleSlots.Add(new AmenityScheduleSlotFormViewModel());
    private void AddBlackout() => _form.Blackouts.Add(new AmenityBlackoutFormViewModel());

    private async Task SaveAsync()
    {
        _saving = true;
        _error = null;
        var result = await Api.SaveAmenityAsync(LicenseId, Amenity?.Id, _form);
        _saving = false;
        if (!result.Success) { _error = result.Error; return; }
        Dialog.Close(DialogResult.Ok(result.Value));
    }

    private void Cancel() => Dialog.Cancel();

    private static string DayLabel(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => "Domingo",
        DayOfWeek.Monday => "Segunda",
        DayOfWeek.Tuesday => "Terca",
        DayOfWeek.Wednesday => "Quarta",
        DayOfWeek.Thursday => "Quinta",
        DayOfWeek.Friday => "Sexta",
        _ => "Sabado"
    };
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build Condotify`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Condotify/Components/Dialogs/AmenityFormDialog.razor
git commit -m "feat: add AmenityFormDialog for creating and editing locais"
```

---

### Task 15: `AmenityBookingFormDialog.razor` — register a booking

**Files:**
- Create: `Condotify/Components/Dialogs/AmenityBookingFormDialog.razor`

**Interfaces:**
- Consumes: `AmenityViewModel`, `AmenitySlotAvailabilityViewModel`, `AmenityUnitSearchViewModel`, `AmenityBookingFormViewModel` (Task 12), `Api.SearchAmenityUnitsAsync`/`Api.GetAmenityAvailabilityAsync`/`Api.CreateAmenityBookingAsync` (Task 13).
- Produces: a dialog invoked by Task 16 with `Dialog.Close(DialogResult.Ok(AmenityBookingViewModel))` on success.

- [ ] **Step 1: Create the dialog**

```razor
@inject CondotifyApiClient Api

<MudDialog Class="entity-dialog">
    <TitleContent>
        <div class="dialog-title-block">
            <MudIcon Icon="@Icons.Material.Outlined.EventAvailable" />
            <div><MudText Typo="Typo.h6">Agendar @Amenity.Name</MudText><MudText Typo="Typo.caption">Selecione a unidade, a data e o horario.</MudText></div>
        </div>
    </TitleContent>
    <DialogContent>
        @if (!string.IsNullOrWhiteSpace(_error))
        {
            <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert>
        }
        <div class="form-section-heading"><span>1</span><div><strong>Unidade</strong><small>Busque pelo numero da unidade ou nome do morador.</small></div></div>
        <MudAutocomplete T="AmenityUnitSearchViewModel" @bind-Value="_unit" SearchFunc="SearchUnitsAsync"
                         ToStringFunc="@(x => x is null ? string.Empty : $"{x.BlockName} / {x.UnitNumber}")"
                         Label="Unidade" Placeholder="Digite pelo menos 2 caracteres" Variant="Variant.Outlined"
                         Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Outlined.Search" Clearable="true"
                         ValueChanged="OnUnitSelectedAsync" />

        @if (_unit is not null && _unit.Residents.Count > 0)
        {
            <MudSelect T="Guid?" @bind-Value="_form.ResidentId" Label="Morador (opcional)" Variant="Variant.Outlined">
                <MudSelectItem T="Guid?" Value="null">Nao vincular a um morador</MudSelectItem>
                @foreach (var resident in _unit.Residents)
                {
                    <MudSelectItem T="Guid?" Value="resident.ResidentId">@resident.Name</MudSelectItem>
                }
            </MudSelect>
        }

        <div class="form-section-heading"><span>2</span><div><strong>Data e horario</strong><small>Somente horarios livres podem ser selecionados.</small></div></div>
        <MudDatePicker Date="_form.Date" DateChanged="OnDateChangedAsync" Label="Data" Variant="Variant.Outlined" MinDate="DateTime.Today" />

        @if (_loadingAvailability)
        {
            <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" Size="Size.Small" /></div>
        }
        else if (_availability.Count == 0)
        {
            <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">Nenhum horario configurado para este dia da semana.</MudAlert>
        }
        else
        {
            <div class="credential-segment" role="radiogroup" aria-label="Horarios disponiveis">
                @foreach (var slot in _availability)
                {
                    <button type="button" role="radio" aria-checked="@(_form.SlotId == slot.SlotId)" disabled="@(!slot.Available)"
                            class="@SlotClass(slot)" @onclick="@(() => SelectSlot(slot))">
                        <span>
                            <strong>@slot.StartTime.ToString(@"hh\:mm") - @slot.EndTime.ToString(@"hh\:mm")</strong>
                            <small>@(slot.Available ? "Disponivel" : $"Ocupado - {slot.OccupiedByUnitNumber}")</small>
                        </span>
                    </button>
                }
            </div>
        }

        @if (Amenity.RequiresTermsAcceptance)
        {
            <div class="form-section-heading"><span>3</span><div><strong>Termo de uso</strong></div></div>
            <MudPaper Class="pa-4" Elevation="0" Style="white-space: pre-wrap; background: var(--mud-palette-background-grey);">@Amenity.TermsText</MudPaper>
            <MudCheckBox T="bool" @bind-Value="_form.TermsAccepted" Color="Color.Primary" Label="Li e aceito o termo de uso" />
        }

        @if (Amenity.FeeAmount is > 0)
        {
            <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">Taxa informativa: R$ @Amenity.FeeAmount.Value.ToString("N2") @Amenity.FeeDescription</MudAlert>
        }

        <MudTextField T="string" @bind-Value="_form.Notes" Label="Observacoes" Lines="2" Variant="Variant.Outlined" />

        <div class="form-actions dialog-form-actions">
            <MudButton Variant="Variant.Text" Disabled="_saving" OnClick="Cancel">Cancelar</MudButton>
            <MudButton Variant="Variant.Filled" Color="Color.Primary" Disabled="@(_saving || !CanSave)" OnClick="SaveAsync">@(_saving ? "Agendando..." : "Confirmar agendamento")</MudButton>
        </div>
    </DialogContent>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter, EditorRequired] public AmenityViewModel Amenity { get; set; } = null!;

    private readonly AmenityBookingFormViewModel _form = new();
    private AmenityUnitSearchViewModel? _unit;
    private List<AmenitySlotAvailabilityViewModel> _availability = [];
    private bool _loadingAvailability;
    private bool _saving;
    private string? _error;

    private bool CanSave => _unit is not null && _form.SlotId != Guid.Empty && (!Amenity.RequiresTermsAcceptance || _form.TermsAccepted);

    protected override async Task OnInitializedAsync() => await LoadAvailabilityAsync();

    private async Task<IEnumerable<AmenityUnitSearchViewModel>> SearchUnitsAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2) return [];
        var result = await Api.SearchAmenityUnitsAsync(LicenseId, value, cancellationToken);
        return result.Success ? result.Value ?? [] : [];
    }

    private Task OnUnitSelectedAsync(AmenityUnitSearchViewModel? unit)
    {
        _unit = unit;
        _form.UnitId = unit?.UnitId ?? Guid.Empty;
        _form.ResidentId = null;
        return Task.CompletedTask;
    }

    private async Task OnDateChangedAsync(DateTime? date)
    {
        _form.Date = date ?? DateTime.Today;
        await LoadAvailabilityAsync();
    }

    private async Task LoadAvailabilityAsync()
    {
        _loadingAvailability = true;
        _form.SlotId = Guid.Empty;
        var result = await Api.GetAmenityAvailabilityAsync(LicenseId, Amenity.Id, _form.Date);
        _loadingAvailability = false;
        _availability = result.Success ? result.Value ?? [] : [];
        if (!result.Success) _error = result.Error;
    }

    private void SelectSlot(AmenitySlotAvailabilityViewModel slot)
    {
        if (!slot.Available) return;
        _form.SlotId = slot.SlotId;
    }

    private string SlotClass(AmenitySlotAvailabilityViewModel slot) => _form.SlotId == slot.SlotId
        ? "credential-segment-option selected"
        : "credential-segment-option";

    private async Task SaveAsync()
    {
        if (!CanSave) return;
        _saving = true;
        _error = null;
        var result = await Api.CreateAmenityBookingAsync(LicenseId, Amenity.Id, _form);
        _saving = false;
        if (!result.Success) { _error = result.Error; return; }
        Dialog.Close(DialogResult.Ok(result.Value));
    }

    private void Cancel() => Dialog.Cancel();
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build Condotify`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Condotify/Components/Dialogs/AmenityBookingFormDialog.razor
git commit -m "feat: add AmenityBookingFormDialog for registering bookings"
```

---

### Task 16: `AgendamentoModule.razor` and `LicenseWorkspace.razor` wiring

**Files:**
- Create: `Condotify/Components/LicenseModules/AgendamentoModule.razor`
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor:26-98` (nav button, section-allowed switch, case in the module switch)

**Interfaces:**
- Consumes: everything from Tasks 12-15.

- [ ] **Step 1: Create the module**

```razor
@inject CondotifyApiClient Api
@inject IDialogService DialogService
@inject ISnackbar Snackbar

<section class="module-intro">
    <div>
        <MudText Typo="Typo.h2">Agendamento de areas comuns</MudText>
        <MudText Typo="Typo.body2" Color="Color.Secondary">Cadastre os locais agendaveis e registre reservas em nome das unidades.</MudText>
    </div>
    @if (CanManage && _tab == 0)
    {
        <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Add" OnClick="@(() => OpenAmenityAsync(null))">Novo local</MudButton>
    }
</section>

<MudTabs @bind-ActivePanelIndex="_tab" Color="Color.Primary" Class="mb-4">
    <MudTabPanel Text="Locais" />
    <MudTabPanel Text="Agendamentos" />
</MudTabs>

@if (!string.IsNullOrWhiteSpace(_error))
{
    <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert>
}

@if (_loading)
{
    <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div>
}
else if (_tab == 0)
{
    @if (_amenities.Count == 0)
    {
        <div class="empty-state compact-empty">
            <MudIcon Icon="@Icons.Material.Outlined.Deck" Size="Size.Large" Color="Color.Primary" />
            <MudText Typo="Typo.h5">Nenhum local cadastrado</MudText>
            <MudText Typo="Typo.body2" Color="Color.Secondary">Cadastre a churrasqueira, a piscina ou o salao de festas para comecar a receber agendamentos.</MudText>
            @if (CanManage) { <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Add" OnClick="@(() => OpenAmenityAsync(null))">Cadastrar primeiro local</MudButton> }
        </div>
    }
    else
    {
        <MudPaper Class="content-panel" Elevation="0">
            <div class="access-route-list">
                @foreach (var amenity in _amenities)
                {
                    <article class="access-route-row">
                        <span class="access-route-icon"><MudIcon Icon="@Icons.Material.Outlined.Deck" /></span>
                        <div class="access-route-main"><strong>@amenity.Name</strong><span>@amenity.Description</span></div>
                        <div class="access-route-audience"><small>Capacidade</small><strong>@(amenity.Capacity?.ToString() ?? "-")</strong></div>
                        <div class="access-route-schedule"><small>Taxa</small><strong>@(amenity.FeeAmount is > 0 ? $"R$ {amenity.FeeAmount:N2}" : "Sem taxa")</strong></div>
                        <MudChip T="string" Size="Size.Small" Color="@(amenity.RequiresApproval ? Color.Warning : Color.Success)" Variant="Variant.Outlined">@(amenity.RequiresApproval ? "Aprovacao manual" : "Aprovacao automatica")</MudChip>
                        <MudChip T="string" Size="Size.Small" Color="@(amenity.Active ? Color.Success : Color.Default)" Variant="Variant.Outlined">@(amenity.Active ? "Ativo" : "Inativo")</MudChip>
                        @if (CanManage)
                        {
                            <MudMenu Icon="@Icons.Material.Outlined.MoreVert" AnchorOrigin="Origin.BottomRight" TransformOrigin="Origin.TopRight">
                                <MudMenuItem Icon="@Icons.Material.Outlined.Edit" OnClick="@(() => OpenAmenityAsync(amenity))">Editar local</MudMenuItem>
                                <MudDivider />
                                <MudMenuItem Icon="@Icons.Material.Outlined.DeleteOutline" Class="danger-menu-item" OnClick="@(() => DeleteAmenityAsync(amenity))">Excluir local</MudMenuItem>
                            </MudMenu>
                        }
                    </article>
                }
            </div>
        </MudPaper>
    }
}
else
{
    <div class="form-grid form-grid-two mb-4">
        <MudSelect T="Guid" Value="_selectedAmenityId" ValueChanged="OnAmenitySelectedAsync" Label="Local" Variant="Variant.Outlined">
            @foreach (var amenity in _amenities)
            {
                <MudSelectItem Value="amenity.Id">@amenity.Name</MudSelectItem>
            }
        </MudSelect>
        @if (CanManage && _selectedAmenity is not null)
        {
            <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.EventAvailable" OnClick="OpenBookingAsync">Agendar</MudButton>
        }
    </div>

    @if (_selectedAmenity is null)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">Cadastre um local na aba "Locais" para comecar a agendar.</MudAlert>
    }
    else if (_loadingBookings)
    {
        <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div>
    }
    else if (_bookings.Count == 0)
    {
        <MudAlert Severity="Severity.Info" Variant="Variant.Outlined">Nenhum agendamento nos proximos 30 dias para este local.</MudAlert>
    }
    else
    {
        <MudPaper Class="content-panel" Elevation="0">
            <div class="access-route-list">
                @foreach (var booking in _bookings)
                {
                    <article class="access-route-row">
                        <span class="access-route-icon"><MudIcon Icon="@Icons.Material.Outlined.Event" /></span>
                        <div class="access-route-main"><strong>@booking.BlockName / @booking.UnitNumber</strong><span>@(booking.ResidentName ?? "Sem morador vinculado")</span></div>
                        <div class="access-route-schedule"><small>Data</small><strong>@booking.Date.ToString("dd/MM/yyyy")</strong></div>
                        <div class="access-route-targets"><small>Horario</small><strong>@booking.SlotStartTime.ToString(@"hh\:mm") - @booking.SlotEndTime.ToString(@"hh\:mm")</strong></div>
                        <MudChip T="string" Size="Size.Small" Color="@StatusColor(booking.Status)" Variant="Variant.Outlined">@StatusLabel(booking.Status)</MudChip>
                        @if (CanManage && booking.Status == "Pending")
                        {
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Success" OnClick="@(() => ApproveAsync(booking))">Aprovar</MudButton>
                            <MudButton Size="Size.Small" Variant="Variant.Text" Color="Color.Error" OnClick="@(() => RejectAsync(booking))">Recusar</MudButton>
                        }
                        @if (booking.Status is "Pending" or "Confirmed")
                        {
                            <MudIconButton Icon="@Icons.Material.Outlined.EventBusy" Color="Color.Error" OnClick="@(() => CancelAsync(booking))" />
                        }
                    </article>
                }
            </div>
        </MudPaper>
    }
}

@code {
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter] public bool CanManage { get; set; }

    private int _tab;
    private bool _loading = true;
    private bool _loadingBookings;
    private Guid _loadedLicenseId;
    private string? _error;
    private List<AmenityViewModel> _amenities = [];
    private Guid _selectedAmenityId;
    private AmenityViewModel? _selectedAmenity;
    private List<AmenityBookingViewModel> _bookings = [];

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedLicenseId == LicenseId) return;
        _loadedLicenseId = LicenseId;
        await LoadAmenitiesAsync();
    }

    private async Task LoadAmenitiesAsync()
    {
        _loading = true;
        _error = null;
        var result = await Api.GetAmenitiesAsync(LicenseId);
        _loading = false;
        if (result.Success && result.Value is not null) _amenities = result.Value;
        else { _error = result.Error ?? "Nao foi possivel carregar os locais."; return; }

        if (_selectedAmenityId == Guid.Empty && _amenities.Count > 0)
            await OnAmenitySelectedAsync(_amenities[0].Id);
    }

    private async Task OnAmenitySelectedAsync(Guid amenityId)
    {
        _selectedAmenityId = amenityId;
        _selectedAmenity = _amenities.FirstOrDefault(x => x.Id == amenityId);
        await LoadBookingsAsync();
    }

    private async Task LoadBookingsAsync()
    {
        if (_selectedAmenity is null) return;
        _loadingBookings = true;
        var result = await Api.GetAmenityBookingsAsync(LicenseId, _selectedAmenity.Id, DateTime.Today, DateTime.Today.AddDays(30));
        _loadingBookings = false;
        if (result.Success && result.Value is not null) _bookings = result.Value;
        else _error = result.Error ?? "Nao foi possivel carregar os agendamentos.";
    }

    private async Task OpenAmenityAsync(AmenityViewModel? amenity)
    {
        var parameters = new DialogParameters { [nameof(AmenityFormDialog.LicenseId)] = LicenseId, [nameof(AmenityFormDialog.Amenity)] = amenity };
        var dialog = await DialogService.ShowAsync<AmenityFormDialog>(amenity is null ? "Novo local" : "Editar local", parameters,
            new DialogOptions { CloseButton = true, FullWidth = true, MaxWidth = MaxWidth.Medium });
        var result = await dialog.Result;
        if (result?.Canceled != false) return;
        Snackbar.Add(amenity is null ? "Local cadastrado." : "Local atualizado.", Severity.Success);
        await LoadAmenitiesAsync();
    }

    private async Task DeleteAmenityAsync(AmenityViewModel amenity)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync("Excluir local", $"Excluir o local {amenity.Name}?", yesText: "Excluir", cancelText: "Cancelar");
        if (confirmed != true) return;
        var result = await Api.DeleteAmenityAsync(LicenseId, amenity.Id);
        if (!result.Success) { Snackbar.Add(result.Error ?? "Nao foi possivel excluir o local.", Severity.Error); return; }
        Snackbar.Add("Local excluido.", Severity.Success);
        await LoadAmenitiesAsync();
    }

    private async Task OpenBookingAsync()
    {
        if (_selectedAmenity is null) return;
        var parameters = new DialogParameters { [nameof(AmenityBookingFormDialog.LicenseId)] = LicenseId, [nameof(AmenityBookingFormDialog.Amenity)] = _selectedAmenity };
        var dialog = await DialogService.ShowAsync<AmenityBookingFormDialog>("Agendar", parameters,
            new DialogOptions { CloseButton = true, FullWidth = true, MaxWidth = MaxWidth.Medium });
        var result = await dialog.Result;
        if (result?.Canceled != false) return;
        Snackbar.Add("Agendamento registrado.", Severity.Success);
        await LoadBookingsAsync();
    }

    private async Task ApproveAsync(AmenityBookingViewModel booking)
    {
        var result = await Api.ApproveAmenityBookingAsync(LicenseId, booking.AmenityId, booking.Id);
        if (!result.Success) { Snackbar.Add(result.Error ?? "Nao foi possivel aprovar.", Severity.Error); return; }
        Snackbar.Add("Agendamento aprovado.", Severity.Success);
        await LoadBookingsAsync();
    }

    private async Task RejectAsync(AmenityBookingViewModel booking)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync("Recusar agendamento", $"Recusar o agendamento de {booking.UnitNumber}?", yesText: "Recusar", cancelText: "Cancelar");
        if (confirmed != true) return;
        var result = await Api.RejectAmenityBookingAsync(LicenseId, booking.AmenityId, booking.Id, "Recusado pela administracao");
        if (!result.Success) { Snackbar.Add(result.Error ?? "Nao foi possivel recusar.", Severity.Error); return; }
        Snackbar.Add("Agendamento recusado.", Severity.Success);
        await LoadBookingsAsync();
    }

    private async Task CancelAsync(AmenityBookingViewModel booking)
    {
        var confirmed = await DialogService.ShowMessageBoxAsync("Cancelar agendamento", $"Cancelar o agendamento de {booking.UnitNumber}?", yesText: "Cancelar agendamento", cancelText: "Voltar");
        if (confirmed != true) return;
        var result = await Api.CancelAmenityBookingAsync(LicenseId, booking.AmenityId, booking.Id, "Cancelado pela administracao");
        if (!result.Success) { Snackbar.Add(result.Error ?? "Nao foi possivel cancelar.", Severity.Error); return; }
        Snackbar.Add("Agendamento cancelado.", Severity.Success);
        await LoadBookingsAsync();
    }

    private static Color StatusColor(string status) => status switch
    {
        "Confirmed" => Color.Success,
        "Pending" => Color.Warning,
        "Rejected" or "Cancelled" => Color.Error,
        _ => Color.Default
    };

    private static string StatusLabel(string status) => status switch
    {
        "Confirmed" => "Confirmado",
        "Pending" => "Pendente",
        "Rejected" => "Recusado",
        "Cancelled" => "Cancelado",
        "Completed" => "Concluido",
        _ => status
    };
}
```

- [ ] **Step 2: Wire the nav button and switch case into `LicenseWorkspace.razor`**

In `Condotify/Components/Pages/LicenseWorkspace.razor`, add the nav button next to the `ViewDeliveries` one (after line 37):

```razor
        @if (Has(LicensePermission.ViewDeliveries)) { @NavButton("encomendas", "Encomendas", Icons.Material.Outlined.Inventory2) }
        @if (Has(LicensePermission.ViewBookings)) { @NavButton("agendamento", "Agendamento", Icons.Material.Outlined.Deck) }
```

Add the case in the module switch (after the `encomendas` case, around line 63):

```razor
            case "agendamento":
                <AgendamentoModule LicenseId="LicenseId" CanManage="@Has(LicensePermission.ManageBookings)" />
                break;
```

Add `"agendamento"` to `SectionAllowed` (around line 135):

```csharp
        "encomendas" => Has(LicensePermission.ViewDeliveries),
        "agendamento" => Has(LicensePermission.ViewBookings),
```

Add a fallback to `DefaultSection` (around line 96, right before the `administracao` fallback) so users whose only permission is bookings still land somewhere:

```csharp
        : Has(LicensePermission.ViewDeliveries) ? "encomendas"
        : Has(LicensePermission.ViewBookings) ? "agendamento"
```

- [ ] **Step 3: Build to confirm it compiles**

Run: `dotnet build Condotify`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Condotify/Components/LicenseModules/AgendamentoModule.razor Condotify/Components/Pages/LicenseWorkspace.razor
git commit -m "feat: add AgendamentoModule and wire it into LicenseWorkspace navigation"
```

---

### Task 17: Manual verification

**Files:** none (verification task)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded.` with 0 errors across all 5 projects.

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`
Expected: all tests pass (backend + the new Amenity tests).

- [ ] **Step 3: Start the app and exercise the golden path**

Use the `run` skill (or `dotnet run --project CondotifyAPI` and `dotnet run --project Condotify` in separate terminals) to start both the API and the portal, then in the browser:
1. Open a condo's workspace (`/licencas/{id}/agendamento`) — confirm the "Agendamento" tab appears for a user with `ViewBookings`.
2. Create a local (e.g. "Churrasqueira") with a Saturday 08:00-14:00 slot and `RequiresApproval = true`.
3. Switch to "Agendamentos", pick a unit, pick that Saturday, book the slot — confirm it shows as "Pendente".
4. Click "Aprovar" — confirm it becomes "Confirmado".
5. Try booking the same slot/date again — confirm it is shown as occupied and cannot be double-booked.
6. Cancel the booking and confirm it disappears from the active list.

Report any mismatch between this manual walkthrough and the spec before considering the feature done.
