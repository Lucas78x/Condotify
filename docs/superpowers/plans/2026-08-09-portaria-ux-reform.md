# Reforma da UX da Portaria — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidar a Portaria (`/portaria`) em um hub único (Agenda, Encomendas, Eventos), trocar o polling de 15s por atualização em tempo real via SignalR, e trocar o campo de placa em texto livre por um autocomplete contra veículos cadastrados, além de habilitar captura de foto (chegada + entrega) em Encomendas.

**Architecture:** .NET 8 — `CondotifyAPI` (Web API, controllers em `CondotifyAPI/Controllers/`), `Condotify` (Blazor Server, MudBlazor, `Condotify/Components/`), `Condotify.ApiClient` (cliente HTTP compartilhado), `Condotify.Contracts` (view models compartilhados, namespace `Condotify.Models`/`Condotify.Services`). Nenhum SignalR existe hoje na solução — é infraestrutura nova, adicionada de forma escopada a este domínio.

**Tech Stack:** ASP.NET Core Web API + EF Core (Npgsql), Blazor Server + MudBlazor 9.7.0, SignalR (`Microsoft.AspNetCore.SignalR` no servidor — já incluído no shared framework; `Microsoft.AspNetCore.SignalR.Client` novo no portal), xUnit para testes de backend.

## Global Constraints

- Fora de escopo (não implementar): check-in por QR/câmera no desktop, integração com LPR/OCR, push de status online/offline de terminal, testes de componente Blazor (bUnit) novos, redesign visual além do necessário para caber as novas abas.
- Rotas (`AccessRoutesModule`) permanece fora da Portaria — configuração de política, não muda neste plano.
- Toda nova consulta/endpoint deve respeitar o isolamento de tenant já implementado (filtro global de licença — ver `docs/superpowers/plans/2026-08-08-ef-core-tenant-filter.md`); nenhuma consulta nova usa `.IgnoreQueryFilters()`.
- Seguir os padrões já estabelecidos no código: `HasLicenseAccessAsync`/`HasAccessAsync` como segunda checagem depois de `[RequireLicensePermission]`, `MudAutocomplete` + `SearchFunc` para buscas, `FacePhotoProcessor.PrepareAsync` para captura/compressão de foto no cliente, `IPrivateMediaStore.StoreDataUriAsync` para persistir fotos em base64 no servidor.
- Todo teste de integração de backend que precise de Postgres usa a mesma convenção já em uso no projeto: `Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres` via `CONDOTIFY_DB_CONNECTION`, container `condotify-postgres` (ver `docker ps --filter "name=condotify-postgres"`).
- Sem testes de UI automatizados para Blazor — verificação de frontend é manual (rodar o portal localmente).

---

## Task 1: Endpoint de busca de placa + autocomplete no `ConciergeVisitDialog`

**Files:**
- Modify: `CondotifyAPI/Controllers/PeopleManagementController.cs`
- Modify: `Condotify.Contracts/LicenseManagementViewModels.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Modify: `Condotify/Components/Dialogs/ConciergeVisitDialog.razor`
- Test: `CondotifyAPI.Tests/VehicleSearchTests.cs`

**Interfaces:**
- Produces: `GET api/access/licenses/{licenseId}/vehicles/search?plate=` retornando `List<VehiclePlateSearchOut>`; `CondotifyApiClient.SearchVehiclesByPlateAsync(Guid licenseId, string plate, CancellationToken)` retornando `ApiResult<List<VehiclePlateSearchViewModel>>`.

- [ ] **Step 1: Escrever o teste do endpoint (falha primeiro)**

`PeopleManagementController` já tem o helper `NormalizePlate` (linha 497) e `HasLicenseAccessAsync` (linha 386) — reaproveitar ambos.

```csharp
// CondotifyAPI.Tests/VehicleSearchTests.cs
using System.Security.Claims;
using CondotifyAPI.Domain.DTO.Block;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.DTO.Resident;
using CondotifyAPI.Domain.DTO.Unit;
using CondotifyAPI.Domain.DTO.Vehicle;
using CondotifyAPI.Domain.Enums.Resident;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class VehicleSearchTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _blockId;
    private Guid _unitId;
    private Guid _residentId;
    private Guid _vehicleId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);
        _tenant.MarkUnrestricted();

        _enterpriseId = Guid.NewGuid();
        _licenseId = Guid.NewGuid();
        _blockId = Guid.NewGuid();
        _unitId = Guid.NewGuid();
        _residentId = Guid.NewGuid();
        _vehicleId = Guid.NewGuid();

        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Placa {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca placa", Code = $"PLT-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Blocks.Add(new BlockDTO { Id = _blockId, LicenseId = _licenseId, Name = "Bloco A" });
        _context.Units.Add(new UnitDTO { Id = _unitId, BlockId = _blockId, Number = "101" });
        await _context.SaveChangesAsync();

        _context.Residents.Add(new ResidentAccessDTO
        {
            Id = _residentId, UnitId = _unitId, Name = "Morador Placa", Email = $"{_residentId:N}@teste.condotify.local",
            Password = string.Empty, PhoneNumber = string.Empty, CommercialPhone = string.Empty, CPF = string.Empty, RG = string.Empty,
            BirthDate = string.Empty, ApartmentNumber = "101", ImgUrl = string.Empty, Description = string.Empty,
            AccessType = ResidentAccessTypeEnum.Responsible, FirstAccess = false, NotifyAccess = false, IsActive = true,
            Temporary = false, LastAccess = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, AccessCredentials = []
        });
        _context.Vehicles.Add(new VehicleDTO { Id = _vehicleId, UnitId = _unitId, ResidentId = _residentId, Plate = "ABC1D23", Brand = "Fiat", Model = "Argo", Color = "Prata", Type = "Carro", TagIdentifier = string.Empty, IsActive = true });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Vehicles.Where(x => x.Id == _vehicleId).ExecuteDelete();
        _context.Residents.Where(x => x.Id == _residentId).ExecuteDelete();
        _context.Units.Where(x => x.Id == _unitId).ExecuteDelete();
        _context.Blocks.Where(x => x.Id == _blockId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    private static PeopleManagementController.PlateSearchTestSeam BuildController(DatabaseContext context) => new(context);

    [Fact]
    public async Task SearchVehiclesByPlate_ReturnsMatch_WithOwnerAndUnit()
    {
        var results = await PeopleManagementController.SearchVehiclesByPlateCore(_context, _licenseId, "abc1");

        Assert.Single(results);
        Assert.Equal("ABC1D23", results[0].Plate);
        Assert.Equal("Morador Placa", results[0].ResidentName);
        Assert.Equal("Bloco A / 101", results[0].UnitLabel);
    }

    [Fact]
    public async Task SearchVehiclesByPlate_DoesNotLeakVehiclesFromOtherLicenses()
    {
        var otherLicenseId = Guid.NewGuid();
        var results = await PeopleManagementController.SearchVehiclesByPlateCore(_context, otherLicenseId, "abc1");

        Assert.Empty(results);
    }
}
```

Antes de escrever isto, ler `CondotifyAPI.Domain/DTO/Vehicle/VehicleDTO.cs` e `CondotifyAPI.Domain/DTO/Resident/ResidentAccessDTO.cs` para confirmar nomes exatos de propriedades obrigatórias (o teste acima já reflete os campos vistos em `PeopleManagementController.CreateVehicle`/`ToVehicle`, mas confirme antes de rodar).

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~VehicleSearchTests"`
Expected: FAIL — `PeopleManagementController.SearchVehiclesByPlateCore` ainda não existe.

- [ ] **Step 3: Implementar o endpoint**

Em `CondotifyAPI/Controllers/PeopleManagementController.cs`, adicionar (por exemplo logo após o método `GetUnit`, linha ~63):

```csharp
    [HttpGet("vehicles/search")]
    [RequireLicensePermission(LicensePermissionEnum.ViewPeople)]
    public async Task<IActionResult> SearchVehiclesByPlate(Guid licenseId, [FromQuery] string plate)
    {
        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();
        if (string.IsNullOrWhiteSpace(plate) || plate.Trim().Length < 2) return Ok(new List<VehiclePlateSearchOut>());
        var results = await SearchVehiclesByPlateCore(_context, licenseId, plate);
        return Ok(results);
    }

    internal static async Task<List<VehiclePlateSearchOut>> SearchVehiclesByPlateCore(DatabaseContext context, Guid licenseId, string plate)
    {
        var normalized = NormalizePlate(plate);
        if (string.IsNullOrWhiteSpace(normalized)) return [];
        return await context.Vehicles.AsNoTracking()
            .Where(x => x.IsActive && x.Unit.Block.LicenseId == licenseId && x.Plate.StartsWith(normalized))
            .OrderBy(x => x.Plate)
            .Take(10)
            .Select(x => new VehiclePlateSearchOut
            {
                VehicleId = x.Id,
                Plate = x.Plate,
                ResidentName = x.Resident != null ? x.Resident.Name : string.Empty,
                UnitLabel = x.Unit.Block.Name + " / " + x.Unit.Number
            })
            .ToListAsync();
    }
```

Adicionar a classe de saída perto das outras (`VehicleOut`, `VehicleStatusIn`) no final do arquivo:

```csharp
public class VehiclePlateSearchOut
{
    public Guid VehicleId { get; set; }
    public string Plate { get; set; } = string.Empty;
    public string ResidentName { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
}
```

`NormalizePlate` já existe em `PeopleManagementController.cs:497` (`private static string NormalizePlate(string? plate) => new((plate ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());`) — não duplicar, apenas usar.

- [ ] **Step 4: Rodar o teste de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~VehicleSearchTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Contrato de cliente + `CondotifyApiClient`**

Em `Condotify.Contracts/LicenseManagementViewModels.cs`, adicionar perto de `GlobalResidentSearchViewModel`:

```csharp
    public class VehiclePlateSearchViewModel
    {
        public Guid VehicleId { get; set; }
        public string Plate { get; set; } = string.Empty;
        public string ResidentName { get; set; } = string.Empty;
        public string UnitLabel { get; set; } = string.Empty;
    }
```

Em `Condotify.ApiClient/CondotifyApiClient.cs`, adicionar perto de `SearchResidentsAsync`:

```csharp
    public Task<ApiResult<List<VehiclePlateSearchViewModel>>> SearchVehiclesByPlateAsync(Guid licenseId, string plate, CancellationToken cancellationToken = default) =>
        GetAsync<List<VehiclePlateSearchViewModel>>($"api/access/licenses/{licenseId}/vehicles/search?plate={Uri.EscapeDataString(plate)}", cancellationToken);
```

- [ ] **Step 6: Trocar o campo de placa por `MudAutocomplete` em `ConciergeVisitDialog.razor`**

Substituir, dentro do `form-grid form-grid-two` (linha 33 hoje):

```razor
                    <MudTextField T="string" @bind-Value="_form.VehiclePlate" Label="Placa do veículo" Variant="Variant.Outlined" />
```

por:

```razor
                    <MudAutocomplete T="string" @bind-Value="_form.VehiclePlate" SearchFunc="SearchPlatesAsync"
                                     ToStringFunc="@(x => x ?? string.Empty)" CoerceText="true" CoerceValue="true"
                                     Label="Placa do veículo" Placeholder="Digite ao menos 2 caracteres" Variant="Variant.Outlined"
                                     Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Outlined.DirectionsCar" Clearable="true">
                        <ItemTemplate Context="plateResult">
                            <div><strong>@plateResult</strong><br /><small>@_plateOwnerByValue.GetValueOrDefault(plateResult)</small></div>
                        </ItemTemplate>
                        <NoItemsTemplate><MudText Typo="Typo.body2" Class="pa-2">Nenhum veículo encontrado — pode digitar a placa manualmente.</MudText></NoItemsTemplate>
                    </MudAutocomplete>
```

`CoerceText="true" CoerceValue="true"` mantém o comportamento de texto livre exigido pela spec (visitante sem veículo cadastrado) — o autocomplete sugere, mas não trava o campo a uma opção da lista.

No `@code`, adicionar:

```csharp
    private readonly Dictionary<string, string> _plateOwnerByValue = new();

    private async Task<IEnumerable<string>> SearchPlatesAsync(string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < 2) return [];
        var result = await Api.SearchVehiclesByPlateAsync(LicenseId, value, cancellationToken);
        if (!result.Success) return [];
        _plateOwnerByValue.Clear();
        foreach (var vehicle in result.Value ?? [])
            _plateOwnerByValue[vehicle.Plate] = $"{vehicle.ResidentName} - {vehicle.UnitLabel}";
        return (result.Value ?? []).Select(x => x.Plate);
    }
```

- [ ] **Step 7: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task1-check && rm -rf /tmp/portaria-task1-check`
Run: `dotnet build Condotify/Condotify.csproj -o /tmp/portaria-task1-check2 && rm -rf /tmp/portaria-task1-check2`
Run: `dotnet test CondotifyAPI.Tests`
Expected: builds limpos, toda a suíte passa.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI/Controllers/PeopleManagementController.cs CondotifyAPI.Tests/VehicleSearchTests.cs \
        Condotify.Contracts/LicenseManagementViewModels.cs Condotify.ApiClient/CondotifyApiClient.cs \
        Condotify/Components/Dialogs/ConciergeVisitDialog.razor
git commit -m "feat(concierge): autocomplete de placa contra veiculos cadastrados na criacao de visita"
```

---

## Task 2: Captura de foto em Encomendas — backend

**Files:**
- Modify: `CondotifyAPI/Data/Deliveries/LicenseDeliveryDtos.cs`
- Modify: `CondotifyAPI/Controllers/LicenseStructureController.cs`
- Test: `CondotifyAPI.Tests/DeliveryPhotoTests.cs`

**Interfaces:**
- Consumes: `IPrivateMediaStore.StoreDataUriAsync(Guid licenseId, string dataUri, CancellationToken)` (já existe, `CondotifyAPI/Services/Security/PrivateMediaStore.cs`).
- Produces: `CreateDeliveryIn.PhotoBase64` (novo campo), `UpdateDeliveryStatusIn.ProofBase64` (novo campo) — ambos opcionais.

`CreateDeliveryIn.PhotoUrl` e `UpdateDeliveryStatusIn.ProofUrl` (campos atuais) nunca são preenchidos por nenhuma UI hoje (confirmado: `DeliveryFormViewModel`/`DeliveryFormDialog.razor` não setam `PhotoUrl`) — seguro adicionar os novos campos sem quebrar nada existente.

- [ ] **Step 1: Escrever o teste (falha primeiro)**

```csharp
// CondotifyAPI.Tests/DeliveryPhotoTests.cs
using CondotifyAPI.Data.Deliveries;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class DeliveryPhotoTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);
        _tenant.MarkUnrestricted();

        _enterpriseId = Guid.NewGuid();
        _licenseId = Guid.NewGuid();
        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Foto entrega {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca foto entrega", Code = $"FT-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.Deliveries.Where(x => x.LicenseId == _licenseId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
        if (Directory.Exists(_mediaRoot)) Directory.Delete(_mediaRoot, recursive: true);
    }

    private const string OnePixelPngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
    private readonly string _mediaRoot = Path.Combine(Path.GetTempPath(), $"condotify-media-tests-{Guid.NewGuid():N}");

    private PrivateMediaStore BuildMediaStore()
    {
        Environment.SetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET", "test-media-secret-with-enough-entropy");
        Environment.SetEnvironmentVariable("CONDOTIFY_PRIVATE_MEDIA_PATH", _mediaRoot);
        return new PrivateMediaStore(new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task CreateDeliveryCore_WithPhotoBase64_StoresPhotoUrlViaMediaStore()
    {
        var media = BuildMediaStore();
        var input = new CreateDeliveryIn { Name = "Encomenda com foto", PhotoBase64 = OnePixelPngDataUri };

        var delivery = await LicenseStructureController.CreateDeliveryCore(_context, media, _licenseId, input, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(delivery.PhotoUrl));
        Assert.NotEqual(OnePixelPngDataUri, delivery.PhotoUrl);
    }

    [Fact]
    public async Task CreateDeliveryCore_WithoutPhoto_LeavesPhotoUrlEmpty()
    {
        var media = BuildMediaStore();
        var input = new CreateDeliveryIn { Name = "Encomenda sem foto" };

        var delivery = await LicenseStructureController.CreateDeliveryCore(_context, media, _licenseId, input, CancellationToken.None);

        Assert.Equal(string.Empty, delivery.PhotoUrl);
    }
}
```

`PrivateMediaStore` (a classe concreta real, não uma interface fake) exige um `IConfiguration` no construtor e as variáveis de ambiente `CONDOTIFY_MEDIA_SECRET`/`CONDOTIFY_PRIVATE_MEDIA_PATH` (senão lança `InvalidOperationException`) — o padrão acima (`BuildMediaStore`, com `ConfigurationBuilder().Build()` vazio) é copiado verbatim do teste já existente `CondotifyAPI.Tests/SecurityServicesTests.cs` (~linha 126-129), que já exercita esta mesma classe com sucesso. Adicionar `using Microsoft.Extensions.Configuration;` ao topo do arquivo de teste.

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~DeliveryPhotoTests"`
Expected: FAIL — `CreateDeliveryIn.PhotoBase64` e `LicenseStructureController.CreateDeliveryCore` ainda não existem.

- [ ] **Step 3: Adicionar os campos base64 aos DTOs de entrada**

Em `CondotifyAPI/Data/Deliveries/LicenseDeliveryDtos.cs`, adicionar um campo em cada classe de entrada (manter `PhotoUrl`/`ProofUrl` como estão, por compatibilidade — apenas acrescentar):

```csharp
public class CreateDeliveryIn
{
    public DeliveryTypeEnum Type { get; set; } = DeliveryTypeEnum.Outros;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TrackingCode { get; set; } = string.Empty;
    public string PhotoUrl { get; set; } = string.Empty;
    public string PhotoBase64 { get; set; } = string.Empty;
    public string ReceivedBy { get; set; } = string.Empty;
    public Guid? RecipientResidentId { get; set; }
    public Guid? UnitId { get; set; }
}

public class UpdateDeliveryStatusIn
{
    public DeliveryStatusEnum Status { get; set; }
    public Guid? PersonId { get; set; }
    public string PersonName { get; set; } = string.Empty;
    public string ProofUrl { get; set; } = string.Empty;
    public string ProofBase64 { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Extrair a lógica de criação para um método testável, processar a foto**

Em `CondotifyAPI/Controllers/LicenseStructureController.cs`, adicionar `IPrivateMediaStore media` ao construtor:

```csharp
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControlService;
    private readonly IMapper _mapper;
    private readonly IRecycleBinService _recycleBin;
    private readonly IPrivateMediaStore _media;
    private readonly IPlatformPushNotifier? _push;

    public LicenseStructureController(
        DatabaseContext context,
        IAccessControlService accessControlService,
        IMapper mapper,
        IRecycleBinService recycleBin,
        IPrivateMediaStore media,
        IPlatformPushNotifier? push = null)
    {
        _context = context;
        _accessControlService = accessControlService;
        _mapper = mapper;
        _recycleBin = recycleBin;
        _media = media;
        _push = push;
    }
```

Adicionar `using CondotifyAPI.Services.Security;` ao topo do arquivo se ainda não existir.

Substituir o corpo de `CreateDelivery` (linhas 596-650 hoje) para delegar a um método estático testável:

```csharp
    [HttpPost("deliveries")]
    [RequireLicensePermission(LicensePermissionEnum.ManageDeliveries)]
    public async Task<IActionResult> CreateDelivery(Guid licenseId, [FromBody] CreateDeliveryIn input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { Result = "InvalidRequest", Errors = "Nome da encomenda e obrigatorio." });

        if (!await HasLicenseAccessAsync(licenseId)) return NotFound();

        if (input.RecipientResidentId.HasValue != input.UnitId.HasValue)
            return BadRequest(new { Result = "InvalidDestination", Errors = "Selecione o morador e a unidade de destino." });

        if (input.RecipientResidentId.HasValue)
        {
            var validDestination = await _context.ResidentUnitLinks.AsNoTracking()
                .AnyAsync(x => x.ResidentId == input.RecipientResidentId.Value &&
                               x.UnitId == input.UnitId!.Value &&
                               x.IsActive &&
                               x.Unit.Block.LicenseId == licenseId);
            if (!validDestination)
                return BadRequest(new { Result = "InvalidDestination", Errors = "O destinatario nao possui vinculo ativo com a unidade selecionada." });
        }

        var delivery = await CreateDeliveryCore(_context, _media, licenseId, input, HttpContext.RequestAborted);
        await _context.SaveChangesAsync();

        await NotifyDeliveryAsync(
            delivery,
            "Nova encomenda recebida",
            $"{delivery.Name} foi registrada na portaria.",
            $"delivery-created:{delivery.Id:N}");

        return Created("", ToDeliveryOut(delivery));
    }

    internal static async Task<DeliveryDTO> CreateDeliveryCore(DatabaseContext context, IPrivateMediaStore media, Guid licenseId, CreateDeliveryIn input, CancellationToken cancellationToken)
    {
        var photoUrl = string.IsNullOrWhiteSpace(input.PhotoBase64)
            ? input.PhotoUrl?.Trim() ?? string.Empty
            : await media.StoreDataUriAsync(licenseId, input.PhotoBase64.Trim(), cancellationToken);

        var now = DateTime.UtcNow;
        var delivery = new DeliveryDTO
        {
            Id = Guid.NewGuid(),
            LicenseId = licenseId,
            Type = input.Type,
            Status = DeliveryStatusEnum.Received,
            Name = input.Name.Trim(),
            Description = input.Description?.Trim() ?? string.Empty,
            TrackingCode = input.TrackingCode?.Trim() ?? string.Empty,
            PhotoUrl = photoUrl,
            DeliveryProofUrl = string.Empty,
            ReceivedBy = input.ReceivedBy?.Trim() ?? string.Empty,
            ReceivedAt = now,
            RecipientResidentId = input.RecipientResidentId,
            UnitId = input.UnitId,
            DeliveredTo = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.Deliveries.Add(delivery);
        return delivery;
    }
```

Note que `CreateDeliveryCore` NÃO chama `SaveChangesAsync` — quem chama (o action `CreateDelivery`) é responsável por isso, exatamente como o teste do Step 1 assume (o teste lê `delivery.PhotoUrl` direto do objeto retornado, sem precisar persistir).

Fazer o mesmo para `UpdateDeliveryStatus` (linhas 652-692 hoje) — trocar o bloco `if (input.Status == DeliveryStatusEnum.Delivered)`:

```csharp
        if (input.Status == DeliveryStatusEnum.Delivered)
        {
            delivery.DeliveredToId = input.PersonId;
            delivery.DeliveredTo = input.PersonName?.Trim() ?? string.Empty;
            delivery.DeliveredAt = now;
            delivery.DeliveryProofUrl = string.IsNullOrWhiteSpace(input.ProofBase64)
                ? (input.ProofUrl?.Trim() ?? delivery.DeliveryProofUrl)
                : await _media.StoreDataUriAsync(licenseId, input.ProofBase64.Trim(), HttpContext.RequestAborted);
        }
```

(Isto torna `UpdateDeliveryStatus` `async` de forma mais completa — já era `async Task<IActionResult>`, então só o `await` extra é necessário, sem mudar a assinatura.)

- [ ] **Step 5: Rodar o teste de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~DeliveryPhotoTests"`
Expected: PASS (2/2).

- [ ] **Step 6: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task2-check && rm -rf /tmp/portaria-task2-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo (o registro de DI de `LicenseStructureController` é resolvido automaticamente pelo container — `IPrivateMediaStore` já está registrado em `Program.cs`, nenhuma mudança de `Program.cs` necessária nesta task), toda a suíte passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Data/Deliveries/LicenseDeliveryDtos.cs CondotifyAPI/Controllers/LicenseStructureController.cs CondotifyAPI.Tests/DeliveryPhotoTests.cs
git commit -m "feat(deliveries): aceitar foto em base64 na chegada e na entrega, armazenada via IPrivateMediaStore"
```

---

## Task 3: Captura de foto em Encomendas — frontend

**Files:**
- Modify: `Condotify.Contracts/LicenseManagementViewModels.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Modify: `Condotify/Components/Dialogs/DeliveryFormDialog.razor`
- Create: `Condotify/Components/Dialogs/DeliveryProofDialog.razor`

**Interfaces:**
- Consumes: `FacePhotoProcessor.PrepareAsync(IBrowserFile, CancellationToken)` (`Condotify.ApiClient/FacePhotoProcessor.cs`, já existe).
- Consumes (Task 2): `CreateDeliveryIn.PhotoBase64`, `UpdateDeliveryStatusIn.ProofBase64`.

- [ ] **Step 1: Contrato de cliente**

Em `Condotify.Contracts/LicenseManagementViewModels.cs`, adicionar `PhotoBase64` a `DeliveryFormViewModel` (linha ~202-215 hoje):

```csharp
    public class DeliveryFormViewModel
    {
        public int Type { get; set; } = 999;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;
        public string TrackingCode { get; set; } = string.Empty;
        public string PhotoUrl { get; set; } = string.Empty;
        public string PhotoBase64 { get; set; } = string.Empty;
        public string ReceivedBy { get; set; } = string.Empty;
        public Guid? RecipientResidentId { get; set; }
        public Guid? UnitId { get; set; }
    }
```

Adicionar um view model novo para a confirmação de entrega com foto, perto de `DeliveryStatusFormViewModel`:

```csharp
    public class DeliveryProofFormViewModel
    {
        public Guid? PersonId { get; set; }
        [Required] public string PersonName { get; set; } = string.Empty;
        public string ProofBase64 { get; set; } = string.Empty;
    }
```

- [ ] **Step 2: `CondotifyApiClient`**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, atualizar `CreateDeliveryAsync` (linha 1340 hoje) para incluir `PhotoBase64`:

```csharp
    public Task<ApiResult<bool>> CreateDeliveryAsync(Guid licenseId, DeliveryFormViewModel model, CancellationToken cancellationToken = default) =>
        PostAsync($"api/access/licenses/{licenseId}/deliveries", new
        {
            model.Type,
            Name = model.Name.Trim(),
            Description = model.Description.Trim(),
            TrackingCode = model.TrackingCode.Trim(),
            PhotoUrl = model.PhotoUrl.Trim(),
            model.PhotoBase64,
            ReceivedBy = model.ReceivedBy.Trim(),
            model.RecipientResidentId,
            model.UnitId
        }, false, cancellationToken);
```

Substituir `UpdateDeliveryStatusAsync` (linha 1353 hoje) por uma sobrecarga que aceita a foto de comprovante:

```csharp
    public Task<ApiResult<bool>> UpdateDeliveryStatusAsync(Guid licenseId, Guid deliveryId, int status, string personName, Guid? personId = null, string proofBase64 = "", CancellationToken cancellationToken = default) =>
        PatchAsync($"api/access/licenses/{licenseId}/deliveries/{deliveryId}/status", new { Status = status, PersonName = personName, PersonId = personId, ProofBase64 = proofBase64 }, cancellationToken);
```

(Parâmetros novos têm default — todas as chamadas existentes a `UpdateDeliveryStatusAsync(LicenseId, id, status, personName)`, incluindo a de `DeliveriesModule.ChangeStatusAsync`/futura `ConciergePackagesTab`, continuam compilando sem alteração.)

- [ ] **Step 3: Campo de foto em `DeliveryFormDialog.razor`**

Adicionar após o campo `Observações` (linha 21 hoje), dentro do `form-grid`:

```razor
                <div class="visitor-photo-field full">
                    @if (string.IsNullOrWhiteSpace(_form.PhotoBase64))
                    {
                        <div class="visitor-photo-placeholder"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></div>
                    }
                    else
                    {
                        <img src="@_form.PhotoBase64" alt="Pré-visualização da foto da encomenda" />
                    }
                    <div><strong>@(_photoName ?? "Foto da encomenda (opcional)")</strong><small>JPG ou PNG.</small></div>
                    <label class="file-button"><MudIcon Icon="@Icons.Material.Outlined.AddAPhoto" Size="Size.Small" /> Selecionar foto<InputFile OnChange="ReadPhotoAsync" accept="image/jpeg,image/png" /></label>
                </div>
```

No `@code`, adicionar:

```csharp
    private string? _photoName;

    private async Task ReadPhotoAsync(InputFileChangeEventArgs args)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _form.PhotoBase64 = await FacePhotoProcessor.PrepareAsync(args.File, timeout.Token).WaitAsync(timeout.Token);
            _photoName = args.File.Name;
            _error = null;
        }
        catch (OperationCanceledException) { _error = "O processamento da foto demorou demais. Tente uma imagem menor."; }
        catch (Exception ex) { _error = ex.Message; }
    }
```

(`FacePhotoProcessor` está em `Condotify.Services` — o `using` já deve vir de `Condotify.ApiClient` referenciado pelo projeto; se o build reclamar, adicionar `@using Condotify.Services` no topo do arquivo, mesmo padrão de `ConciergeVisitDialog.razor`, que não precisa de `using` explícito porque o namespace já está em `_Imports.razor`. Conferir `Condotify/_Imports.razor` antes de adicionar um `using` redundante.)

A foto é opcional — não adicionar validação obrigatória em `SaveAsync`, mantendo a UX de graceful-degradation descrita na spec.

- [ ] **Step 4: Diálogo de confirmação de entrega com foto**

```razor
@* Condotify/Components/Dialogs/DeliveryProofDialog.razor *@
@inject CondotifyApiClient Api

<MudDialog Class="entity-dialog">
    <DialogContent>
        <div class="dialog-description">
            <MudIcon Icon="@Icons.Material.Outlined.HowToReg" />
            <div><strong>Confirmar entrega</strong><span>Registre quem retirou a encomenda e, se quiser, uma foto de comprovante.</span></div>
        </div>
        @if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }
        <EditForm Model="_form" OnValidSubmit="SaveAsync">
            <DataAnnotationsValidator />
            <div class="form-grid">
                <MudTextField T="string" @bind-Value="_form.PersonName" Label="Retirado por" Required Variant="Variant.Outlined" For="@(() => _form.PersonName)" Class="full" />
                <div class="visitor-photo-field full">
                    @if (string.IsNullOrWhiteSpace(_form.ProofBase64))
                    {
                        <div class="visitor-photo-placeholder"><MudIcon Icon="@Icons.Material.Outlined.HowToReg" /></div>
                    }
                    else
                    {
                        <img src="@_form.ProofBase64" alt="Pré-visualização do comprovante" />
                    }
                    <div><strong>@(_photoName ?? "Foto de comprovante (opcional)")</strong><small>JPG ou PNG.</small></div>
                    <label class="file-button"><MudIcon Icon="@Icons.Material.Outlined.AddAPhoto" Size="Size.Small" /> Selecionar foto<InputFile OnChange="ReadPhotoAsync" accept="image/jpeg,image/png" /></label>
                </div>
            </div>
            <div class="form-actions dialog-form-actions">
                <MudButton Variant="Variant.Text" Disabled="_saving" OnClick="Cancel">Cancelar</MudButton>
                <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Success" StartIcon="@Icons.Material.Outlined.HowToReg" Disabled="_saving">@(_saving ? "Confirmando..." : "Confirmar entrega")</MudButton>
            </div>
        </EditForm>
    </DialogContent>
</MudDialog>

@code {
    [CascadingParameter] private IMudDialogInstance Dialog { get; set; } = null!;
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter, EditorRequired] public Guid DeliveryId { get; set; }
    private readonly DeliveryProofFormViewModel _form = new();
    private bool _saving;
    private string? _error;
    private string? _photoName;
    private void Cancel() => Dialog.Cancel();

    private async Task ReadPhotoAsync(InputFileChangeEventArgs args)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _form.ProofBase64 = await FacePhotoProcessor.PrepareAsync(args.File, timeout.Token).WaitAsync(timeout.Token);
            _photoName = args.File.Name;
            _error = null;
        }
        catch (OperationCanceledException) { _error = "O processamento da foto demorou demais. Tente uma imagem menor."; }
        catch (Exception ex) { _error = ex.Message; }
    }

    private async Task SaveAsync()
    {
        _saving = true; _error = null;
        var result = await Api.UpdateDeliveryStatusAsync(LicenseId, DeliveryId, 3, _form.PersonName.Trim(), proofBase64: _form.ProofBase64);
        _saving = false;
        if (!result.Success) { _error = result.Error; return; }
        Dialog.Close(DialogResult.Ok(true));
    }
}
```

`3` é `DeliveryStatusEnum.Delivered` (ver `CondotifyAPI.Domain/Enums/Delivery/DeliveryStatus.cs`), mesmo valor literal já usado em `DeliveriesModule.razor:24` (`ChangeStatusAsync(context.Id, 3)`).

- [ ] **Step 5: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/portaria-task3-check && rm -rf /tmp/portaria-task3-check`
Expected: build limpo. Sem teste automatizado de UI — verificação manual roda junto com a Task 5 (quando `ConciergePackagesTab` já estiver ligando este diálogo).

- [ ] **Step 6: Commit**

```bash
git add Condotify.Contracts/LicenseManagementViewModels.cs Condotify.ApiClient/CondotifyApiClient.cs \
        Condotify/Components/Dialogs/DeliveryFormDialog.razor Condotify/Components/Dialogs/DeliveryProofDialog.razor
git commit -m "feat(deliveries): capturar foto da encomenda na chegada e comprovante na entrega"
```

---

## Task 4: Endpoint paginado/filtrável de eventos de acesso (para a aba Eventos)

**Files:**
- Modify: `CondotifyAPI/Controllers/ConciergeController.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs`
- Test: `CondotifyAPI.Tests/ConciergeEventsFeedTests.cs`

**Interfaces:**
- Produces: `GET api/access/licenses/{licenseId}/concierge/events?search=&result=&take=` retornando `List<ConciergeEventOut>` (tipo já existe, `CondotifyAPI/Data/Operations/ConciergeDtos.cs:90`). `CondotifyApiClient.GetConciergeEventsFeedAsync(...)`.

`ConciergeController.Dashboard` (linhas 49-52 hoje) já consulta `context.AccessEventRecords` combinando todos os dispositivos da licença, mas limitado a 80 linhas fixas sem filtro — esta task generaliza essa mesma consulta.

- [ ] **Step 1: Escrever o teste (falha primeiro)**

```csharp
// CondotifyAPI.Tests/ConciergeEventsFeedTests.cs
using CondotifyAPI.Domain.DTO.AccessControl;
using CondotifyAPI.Domain.DTO.Enterprise;
using CondotifyAPI.Domain.DTO.License;
using CondotifyAPI.Domain.Services;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class ConciergeEventsFeedTests : IAsyncLifetime
{
    private DatabaseContext _context = null!;
    private CurrentTenantAccessor _tenant = null!;
    private Guid _enterpriseId;
    private Guid _licenseId;
    private Guid _deviceId;

    public async Task InitializeAsync()
    {
        _tenant = new CurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("CONDOTIFY_DB_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=Condotify;Username=postgres;Password=postgres")
            .Options;
        _context = new DatabaseContext(options, _tenant);
        _tenant.MarkUnrestricted();

        _enterpriseId = Guid.NewGuid();
        _licenseId = Guid.NewGuid();
        _deviceId = Guid.NewGuid();
        _context.Enterprises.Add(new EnterpriseDTO { Id = _enterpriseId, Name = $"Eventos {_enterpriseId:N}", CNPJ = $"{Random.Shared.NextInt64(10000000000000, 99999999999999)}", Email = $"{_enterpriseId:N}@teste.condotify.local" });
        _context.Licenses.Add(new LicenseDTO { Id = _licenseId, EnterpriseId = _enterpriseId, Name = "Licenca eventos", Code = $"EV-{_licenseId:N}"[..20], ExpireDate = DateTime.UtcNow.AddYears(1), CreatedAt = DateTime.UtcNow });
        _context.Devices.Add(new AccessControlDeviceDTO { Id = _deviceId, LicenseId = _licenseId, Name = "Portaria principal", Model = "Teste", IsActive = true });
        await _context.SaveChangesAsync();

        _context.AccessEventRecords.Add(new AccessEventRecordDTO { Id = Guid.NewGuid(), LicenseId = _licenseId, DeviceId = _deviceId, ExternalEventId = "1", Event = "Entrada", Authorized = true, OccurredAt = DateTime.UtcNow, PersonName = "Joao Autorizado", CreatedAt = DateTime.UtcNow });
        _context.AccessEventRecords.Add(new AccessEventRecordDTO { Id = Guid.NewGuid(), LicenseId = _licenseId, DeviceId = _deviceId, ExternalEventId = "2", Event = "Negado", Authorized = false, OccurredAt = DateTime.UtcNow, PersonName = "Maria Negada", CreatedAt = DateTime.UtcNow });
        await _context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _context.AccessEventRecords.Where(x => x.LicenseId == _licenseId).ExecuteDelete();
        _context.Devices.Where(x => x.Id == _deviceId).ExecuteDelete();
        _context.Licenses.Where(x => x.Id == _licenseId).ExecuteDelete();
        _context.Enterprises.Where(x => x.Id == _enterpriseId).ExecuteDelete();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task GetEventsFeedCore_WithoutFilter_ReturnsAllCombinedAcrossDevices()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, null, null, 50);

        Assert.Equal(2, events.Count);
    }

    [Fact]
    public async Task GetEventsFeedCore_WithSearch_FiltersByPersonName()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, "joao", null, 50);

        Assert.Single(events);
        Assert.Equal("Joao Autorizado", events[0].PersonName);
    }

    [Fact]
    public async Task GetEventsFeedCore_WithResultFilter_FiltersByAuthorized()
    {
        var events = await ConciergeController.GetEventsFeedCore(_context, _licenseId, null, false, 50);

        Assert.Single(events);
        Assert.Equal("Maria Negada", events[0].PersonName);
    }
}
```

Antes de escrever isto, ler `CondotifyAPI.Domain/DTO/AccessControl/AccessControlDeviceDTO.cs` e `AccessEventRecordDTO.cs` para confirmar propriedades obrigatórias (a Task 6 do plano `2026-08-08-ef-core-tenant-filter.md` já leu ambos ao marcar `ILicenseScoped` — os nomes acima refletem o que `ConciergeController.Dashboard` já usa).

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~ConciergeEventsFeedTests"`
Expected: FAIL — `ConciergeController.GetEventsFeedCore` ainda não existe.

- [ ] **Step 3: Implementar o endpoint**

Em `CondotifyAPI/Controllers/ConciergeController.cs`, adicionar logo após `Dashboard` (depois da linha 76 hoje):

```csharp
    [HttpGet("events")]
    [RequireLicensePermission(LicensePermissionEnum.ViewEvents)]
    public async Task<IActionResult> EventsFeed(Guid licenseId, [FromQuery] string? search, [FromQuery] bool? authorized, [FromQuery] int take = 100)
    {
        if (!await HasAccessAsync(licenseId)) return NotFound();
        var events = await GetEventsFeedCore(context, licenseId, search, authorized, take);
        return Ok(events);
    }

    internal static async Task<List<ConciergeEventOut>> GetEventsFeedCore(DatabaseContext context, Guid licenseId, string? search, bool? authorized, int take)
    {
        var query = context.AccessEventRecords.AsNoTracking().Where(x => x.LicenseId == licenseId);
        if (authorized.HasValue) query = query.Where(x => x.Authorized == authorized.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.PersonName, pattern) || EF.Functions.ILike(x.Portal, pattern) || EF.Functions.ILike(x.Device.Name, pattern));
        }
        return await query.OrderByDescending(x => x.OccurredAt).Take(Math.Clamp(take, 1, 500))
            .Select(x => new ConciergeEventOut { Id = x.Id, DeviceName = x.Device.Name, PersonName = x.PersonName, Event = x.Event, Authorized = x.Authorized, Portal = x.Portal, OccurredAt = x.OccurredAt })
            .ToListAsync();
    }
```

- [ ] **Step 4: Rodar os testes de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~ConciergeEventsFeedTests"`
Expected: PASS (3/3).

- [ ] **Step 5: `CondotifyApiClient`**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, adicionar perto de `GetConciergeDashboardAsync`:

```csharp
    public Task<ApiResult<List<ConciergeEventViewModel>>> GetConciergeEventsFeedAsync(Guid licenseId, string? search, bool? authorized, int take = 100, CancellationToken cancellationToken = default)
    {
        var query = $"take={take}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (authorized.HasValue) query += $"&authorized={authorized.Value.ToString().ToLowerInvariant()}";
        return GetAsync<List<ConciergeEventViewModel>>($"api/access/licenses/{licenseId}/concierge/events?{query}", cancellationToken);
    }
```

`ConciergeEventViewModel` já existe em `Condotify.Contracts/LicenseManagementViewModels.cs:969` (é o tipo usado por `ConciergeDashboardViewModel.Events`) — reaproveitar exatamente esse tipo, sem criar um novo.

- [ ] **Step 6: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task4-check && rm -rf /tmp/portaria-task4-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Controllers/ConciergeController.cs CondotifyAPI.Tests/ConciergeEventsFeedTests.cs Condotify.ApiClient/CondotifyApiClient.cs
git commit -m "feat(concierge): endpoint paginavel de eventos combinando todos os dispositivos da licenca"
```

---

## Task 5: Consolidação de navegação — abas dentro de `/portaria`

**Files:**
- Create: `Condotify/Components/Concierge/ConciergePackagesTab.razor`
- Create: `Condotify/Components/Concierge/ConciergeEventsTab.razor`
- Modify: `Condotify/Components/_Imports.razor`
- Modify: `Condotify/Components/Pages/Concierge.razor`
- Modify: `Condotify/Components/Pages/LicenseWorkspace.razor`
- Delete: `Condotify/Components/LicenseModules/DeliveriesModule.razor`
- Delete: `Condotify/Components/LicenseModules/AccessEventsModule.razor`

**Interfaces:**
- Consumes: `CondotifyApiClient.GetDeliveriesAsync`, `.CreateDeliveryAsync`, `.UpdateDeliveryStatusAsync` (existentes), `.GetConciergeEventsFeedAsync` (Task 4), `DeliveryFormDialog`/`DeliveryProofDialog` (Tasks 2-3).

- [ ] **Step 0: Registrar o namespace dos novos componentes em `_Imports.razor`**

Componentes Blazor só resolvem por tag (`<ConciergePackagesTab .../>`) se o namespace do arquivo estiver em escopo. `Condotify/Components/_Imports.razor` já tem `@using Condotify.Components.Dialogs` (linha 14) para os diálogos, mas não tem uma entrada equivalente para a nova pasta `Condotify/Components/Concierge/` (cujo namespace implícito, pela convenção de pasta do Blazor, é `Condotify.Components.Concierge`). Adicionar uma linha nova ao arquivo, junto das demais entradas de `Condotify.Components.*`:

```razor
@using Condotify.Components.Concierge
```

Sem isto, `Concierge.razor` (Step 3 abaixo) não compila — as tags `<ConciergePackagesTab>`/`<ConciergeEventsTab>` ficariam sem namespace resolvido.

- [ ] **Step 1: Criar `ConciergePackagesTab.razor` a partir do `DeliveriesModule.razor` atual, com busca/filtro**

```razor
@* Condotify/Components/Concierge/ConciergePackagesTab.razor *@
@inject CondotifyApiClient Api
@inject ISnackbar Snackbar
@inject IDialogService DialogService

<section class="module-intro">
    <div><MudText Typo="Typo.h6" Class="panel-title">Encomendas</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">Acompanhe recebimentos, retiradas e volumes aguardando na portaria.</MudText></div>
    @if (CanManage) { <MudButton Variant="Variant.Filled" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Add" OnClick="OpenCreateDialogAsync">Registrar entrada</MudButton> }
</section>

@if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }

<MudPaper Class="content-panel" Elevation="0">
    <div class="panel-toolbar">
        <div><MudText Typo="Typo.h6" Class="panel-title">Controle de volumes</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">@_deliveries.Count registro(s), @_deliveries.Count(x => x.StatusValue == 2) aguardando retirada</MudText></div>
        <div class="concierge-filters">
            <MudTextField T="string" @bind-Value="_search" Immediate Placeholder="Buscar morador, unidade ou rastreio" Variant="Variant.Outlined" Margin="Margin.Dense" Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Outlined.Search" />
            <MudSelect T="string" @bind-Value="_statusFilter" Variant="Variant.Outlined" Margin="Margin.Dense">
                <MudSelectItem T="string" Value="@("all")">Todos</MudSelectItem>
                <MudSelectItem T="string" Value="@("2")">Na portaria</MudSelectItem>
                <MudSelectItem T="string" Value="@("3")">Entregues</MudSelectItem>
                <MudSelectItem T="string" Value="@("4")">Cancelados</MudSelectItem>
            </MudSelect>
            <MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" Color="Color.Primary" />
        </div>
    </div>
    @if (_loading) { <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div> }
    else if (!FilteredDeliveries.Any()) { <div class="empty-state compact-empty"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" Size="Size.Large" Color="Color.Primary" /><MudText Typo="Typo.subtitle1">Nenhuma encomenda neste filtro</MudText>@if (CanManage) { <MudButton Variant="Variant.Outlined" Color="Color.Primary" StartIcon="@Icons.Material.Outlined.Add" OnClick="OpenCreateDialogAsync">Registrar primeira entrada</MudButton> }</div> }
    else
    {
        <div class="table-wrap"><MudTable Items="FilteredDeliveries" Hover Dense Elevation="0">
            <HeaderContent><MudTh>Encomenda</MudTh><MudTh>Recebimento</MudTh><MudTh>Status</MudTh><MudTh>Ações</MudTh></HeaderContent>
            <RowTemplate>
                <MudTd DataLabel="Encomenda"><div class="license-meta"><span class="license-title">@context.Name</span><span class="license-code">@(!string.IsNullOrWhiteSpace(context.TrackingCode) ? context.TrackingCode : context.Type)</span></div></MudTd>
                <MudTd DataLabel="Recebimento">@context.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")</MudTd>
                <MudTd DataLabel="Status"><MudChip T="string" Size="Size.Small" Color="@StatusColor(context.StatusValue)" Variant="Variant.Outlined">@StatusLabel(context.StatusValue)</MudChip></MudTd>
                <MudTd DataLabel="Ações">@if (CanManage && context.StatusValue == 2) { <MudTooltip Text="Marcar como entregue"><MudIconButton Icon="@Icons.Material.Outlined.HowToReg" Color="Color.Success" OnClick="@(() => OpenProofDialogAsync(context.Id))" /></MudTooltip><MudTooltip Text="Cancelar"><MudIconButton Icon="@Icons.Material.Outlined.Cancel" Color="Color.Error" OnClick="@(() => ChangeStatusAsync(context.Id, 4))" /></MudTooltip> }</MudTd>
            </RowTemplate>
        </MudTable></div>
    }
</MudPaper>

@code {
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    [Parameter] public bool CanManage { get; set; }
    private List<DeliveryRowViewModel> _deliveries = [];
    private bool _loading = true;
    private string? _error;
    private string _search = string.Empty;
    private string _statusFilter = "all";

    private IEnumerable<DeliveryRowViewModel> FilteredDeliveries => _deliveries
        .Where(x => _statusFilter == "all" || x.StatusValue.ToString() == _statusFilter)
        .Where(x => string.IsNullOrWhiteSpace(_search) || $"{x.Name} {x.Description} {x.TrackingCode} {x.ReceivedBy} {x.DeliveredTo}".Contains(_search, StringComparison.OrdinalIgnoreCase));

    protected override Task OnInitializedAsync() => LoadAsync();
    public Task ReloadAsync() => LoadAsync();
    private async Task LoadAsync() { _loading = true; _error = null; var result = await Api.GetDeliveriesAsync(LicenseId); _loading = false; if (result.Success) _deliveries = result.Value ?? []; else _error = result.Error; }

    private async Task OpenCreateDialogAsync()
    {
        var parameters = new DialogParameters { [nameof(DeliveryFormDialog.LicenseId)] = LicenseId };
        var dialog = await DialogService.ShowAsync<DeliveryFormDialog>("Registrar encomenda", parameters, new DialogOptions { CloseButton = true, FullWidth = true, MaxWidth = MaxWidth.Small });
        var result = await dialog.Result;
        if (result?.Canceled != false) return;
        Snackbar.Add("Encomenda registrada com sucesso.", Severity.Success); await LoadAsync();
    }

    private async Task OpenProofDialogAsync(Guid deliveryId)
    {
        var parameters = new DialogParameters { [nameof(DeliveryProofDialog.LicenseId)] = LicenseId, [nameof(DeliveryProofDialog.DeliveryId)] = deliveryId };
        var dialog = await DialogService.ShowAsync<DeliveryProofDialog>("Confirmar entrega", parameters, new DialogOptions { CloseButton = true, FullWidth = true, MaxWidth = MaxWidth.Small });
        var result = await dialog.Result;
        if (result?.Canceled != false) return;
        Snackbar.Add("Encomenda entregue.", Severity.Success); await LoadAsync();
    }

    private async Task ChangeStatusAsync(Guid id, int status) { var result = await Api.UpdateDeliveryStatusAsync(LicenseId, id, status, string.Empty); if (!result.Success) { _error = result.Error; return; } Snackbar.Add("Encomenda cancelada.", Severity.Success); await LoadAsync(); }
    private static string StatusLabel(int status) => status switch { 1 => "Pendente", 2 => "Na portaria", 3 => "Entregue", 4 => "Cancelada", _ => "Desconhecido" };
    private static Color StatusColor(int status) => status switch { 2 => Color.Warning, 3 => Color.Success, 4 => Color.Error, _ => Color.Default };
}
```

- [ ] **Step 2: Criar `ConciergeEventsTab.razor` consumindo o endpoint da Task 4**

```razor
@* Condotify/Components/Concierge/ConciergeEventsTab.razor *@
@inject CondotifyApiClient Api

<section class="module-intro">
    <div><MudText Typo="Typo.h6" Class="panel-title">Eventos de acesso</MudText><MudText Typo="Typo.caption" Color="Color.Secondary">Autorizações e recusas de todos os terminais desta licença.</MudText></div>
</section>

@if (!string.IsNullOrWhiteSpace(_error)) { <MudAlert Severity="Severity.Error" Variant="Variant.Outlined" Class="mb-4">@_error</MudAlert> }

<div class="module-kpis">
    <div class="mini-kpi"><span class="mini-kpi-icon blue"><MudIcon Icon="@Icons.Material.Outlined.ReceiptLong" /></span><div><strong>@_events.Count</strong><span>Eventos carregados</span></div></div>
    <div class="mini-kpi"><span class="mini-kpi-icon green"><MudIcon Icon="@Icons.Material.Outlined.CheckCircle" /></span><div><strong>@_events.Count(x => x.Authorized)</strong><span>Autorizados</span></div></div>
    <div class="mini-kpi"><span class="mini-kpi-icon amber"><MudIcon Icon="@Icons.Material.Outlined.Block" /></span><div><strong>@_events.Count(x => !x.Authorized)</strong><span>Negados ou alertas</span></div></div>
</div>

<MudPaper Class="content-panel" Elevation="0">
    <div class="panel-toolbar">
        <div><MudText Typo="Typo.h6" Class="panel-title">Linha do tempo</MudText></div>
        <div class="access-event-filters">
            <MudTextField T="string" @bind-Value="_search" Placeholder="Buscar pessoa, porta ou terminal" Variant="Variant.Outlined" Margin="Margin.Dense" Immediate Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Outlined.Search" OnKeyUp="@(_ => DebouncedReloadAsync())" />
            <MudSelect T="string" @bind-Value="_resultFilter" Label="Resultado" Variant="Variant.Outlined" Margin="Margin.Dense" ValueChanged="@(async value => { _resultFilter = value; await LoadAsync(); })">
                <MudSelectItem T="string" Value="@("all")">Todos</MudSelectItem>
                <MudSelectItem T="string" Value="@("authorized")">Autorizados</MudSelectItem>
                <MudSelectItem T="string" Value="@("denied")">Negados e alertas</MudSelectItem>
            </MudSelect>
            <MudIconButton Icon="@Icons.Material.Outlined.Refresh" OnClick="LoadAsync" Color="Color.Primary" />
        </div>
    </div>
    @if (_loading) { <div class="loading-state"><MudProgressCircular Indeterminate Color="Color.Primary" /></div> }
    else if (_events.Count == 0) { <div class="empty-state compact-empty"><MudIcon Icon="@Icons.Material.Outlined.EventNote" Size="Size.Large" Color="Color.Primary" /><MudText Typo="Typo.subtitle1">Nenhum evento encontrado</MudText></div> }
    else
    {
        <div class="table-wrap">
            <MudTable Items="_events" Hover Dense Elevation="0">
                <HeaderContent><MudTh>Data e hora</MudTh><MudTh>Resultado</MudTh><MudTh>Pessoa</MudTh><MudTh>Porta</MudTh><MudTh>Equipamento</MudTh></HeaderContent>
                <RowTemplate>
                    <MudTd DataLabel="Data e hora">@context.OccurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")</MudTd>
                    <MudTd DataLabel="Resultado"><MudChip T="string" Size="Size.Small" Color="@(context.Authorized ? Color.Success : Color.Error)" Variant="Variant.Outlined" Icon="@(context.Authorized ? Icons.Material.Outlined.Check : Icons.Material.Outlined.Close)">@context.Event</MudChip></MudTd>
                    <MudTd DataLabel="Pessoa">@(!string.IsNullOrWhiteSpace(context.PersonName) ? context.PersonName : "-")</MudTd>
                    <MudTd DataLabel="Porta">@(!string.IsNullOrWhiteSpace(context.Portal) ? context.Portal : "-")</MudTd>
                    <MudTd DataLabel="Equipamento">@context.DeviceName</MudTd>
                </RowTemplate>
            </MudTable>
        </div>
    }
</MudPaper>

@code {
    [Parameter, EditorRequired] public Guid LicenseId { get; set; }
    private List<ConciergeEventViewModel> _events = [];
    private bool _loading = true;
    private string? _error;
    private string _search = string.Empty;
    private string _resultFilter = "all";
    private CancellationTokenSource? _debounce;

    protected override Task OnInitializedAsync() => LoadAsync();
    public Task ReloadAsync() => LoadAsync();
    public void PrependEvent(ConciergeEventViewModel newEvent) { _events.Insert(0, newEvent); StateHasChanged(); }

    private async Task DebouncedReloadAsync()
    {
        _debounce?.Cancel();
        _debounce = new CancellationTokenSource();
        try { await Task.Delay(400, _debounce.Token); await LoadAsync(); }
        catch (TaskCanceledException) { }
    }

    private async Task LoadAsync()
    {
        _loading = true; _error = null;
        bool? authorized = _resultFilter switch { "authorized" => true, "denied" => false, _ => null };
        var result = await Api.GetConciergeEventsFeedAsync(LicenseId, string.IsNullOrWhiteSpace(_search) ? null : _search, authorized);
        _loading = false;
        if (result.Success) _events = result.Value ?? []; else _error = result.Error;
    }
}
```

`PrependEvent` fica pronto para a Task 9 (cliente SignalR) inserir eventos ao vivo sem recarregar a lista inteira — não é usado ainda nesta task, mas expõe a interface pública que a Task 9 vai consumir.

- [ ] **Step 3: Envolver `Concierge.razor` em `MudTabs`, com checagem de módulo habilitado**

Em `Condotify/Components/Pages/Concierge.razor`, o bloco `else if (_dashboard is not null) { <div class="concierge-kpis">...} <div class="concierge-workspace">...</div> }` (linhas 42-140 hoje) passa a:

```razor
else if (_dashboard is not null)
{
    <div class="concierge-kpis">
        <div class="concierge-kpi"><span class="kpi-icon blue"><MudIcon Icon="@Icons.Material.Outlined.EventAvailable" /></span><div><strong>@_dashboard.ExpectedToday</strong><small>Esperados hoje</small></div></div>
        <div class="concierge-kpi"><span class="kpi-icon green"><MudIcon Icon="@Icons.Material.Outlined.Login" /></span><div><strong>@_dashboard.InsideNow</strong><small>Dentro agora</small></div></div>
        <div class="concierge-kpi"><span class="kpi-icon red"><MudIcon Icon="@Icons.Material.Outlined.GppBad" /></span><div><strong>@_dashboard.DeniedToday</strong><small>Negados hoje</small></div></div>
        <div class="concierge-kpi"><span class="kpi-icon amber"><MudIcon Icon="@Icons.Material.Outlined.Router" /></span><div><strong>@_dashboard.OfflineDevices</strong><small>Terminais offline</small></div></div>
        <div class="concierge-kpi"><span class="kpi-icon blue"><MudIcon Icon="@Icons.Material.Outlined.Approval" /></span><div><strong>@_dashboard.PendingApprovals</strong><small>Aguardando aprovação</small></div></div>
        <div class="concierge-kpi"><span class="kpi-icon red"><MudIcon Icon="@Icons.Material.Outlined.TimerOff" /></span><div><strong>@_dashboard.Overstays</strong><small>Permanência excedida</small></div></div>
    </div>

    <MudTabs Elevation="0" Rounded PanelClass="pa-0" Class="concierge-tabs">
        <MudTabPanel Text="Agenda de acessos" Icon="@Icons.Material.Outlined.EventNote">
            <div class="concierge-workspace">
                <MudPaper Class="content-panel concierge-visits-panel" Elevation="0">
                    @* ... conteudo identico ao painel "Agenda de acessos" atual (linhas 54-103 de hoje), sem nenhuma mudanca ... *@
                </MudPaper>

                <div class="concierge-side-column">
                    @* ... conteudo identico aos paineis "Acionamentos", "Eventos recentes" e "Restrições ativas" (linhas 105-138 de hoje), sem nenhuma mudanca ... *@
                </div>
            </div>
        </MudTabPanel>
        @if (IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Deliveries))
        {
            <MudTabPanel Text="Encomendas" Icon="@Icons.Material.Outlined.Inventory2">
                <ConciergePackagesTab @ref="_packagesTab" LicenseId="_licenseId!.Value" CanManage="true" />
            </MudTabPanel>
        }
        <MudTabPanel Text="Eventos" Icon="@Icons.Material.Outlined.FactCheck">
            <ConciergeEventsTab @ref="_eventsTab" LicenseId="_licenseId!.Value" />
        </MudTabPanel>
    </MudTabs>
}
```

O painel "Agenda de acessos" e a coluna lateral ("Acionamentos", "Eventos recentes", "Restrições ativas") mantêm exatamente o markup e a lógica já existentes — apenas movem para dentro do primeiro `MudTabPanel`. Não reescrever essa parte; copiar literalmente do arquivo atual.

`CanManage="true"` no `ConciergePackagesTab`: quem acessa `/portaria` já passou por `[Authorize]` e o próprio backend valida `[RequireLicensePermission(LicensePermissionEnum.ManageDeliveries)]` nos endpoints de escrita — a portaria historicamente não teve um controle de permissão fino por botão nesta tela (todo porteiro autenticado que acessa `/portaria` pode registrar visita, abrir porta, etc.), então manter esse mesmo nível de confiança para Encomendas é consistente, não uma regressão de segurança (o servidor continua sendo a autoridade real).

No `@code`, adicionar:

```csharp
    private ConciergePackagesTab? _packagesTab;
    private ConciergeEventsTab? _eventsTab;

    private bool IsModuleEnabled(Condotify.Models.LicenseModuleEnum module) =>
        _licenses.FirstOrDefault(x => Guid.TryParse(x.Id, out var id) && id == _licenseId)?.EnabledModules is not { } enabled || (enabled & (long)module) != 0;
```

(`LicenseViewModel.EnabledModules` já vem preenchido em `_licenses`, carregado em `OnInitializedAsync` — nenhuma chamada de API extra é necessária. Se `EnabledModules` for `0`/ausente para uma licença antiga, o padrão do restante do sistema — ver `LicenseWorkspace.razor:195-196` — trata "sem valor" como "todos os módulos habilitados"; a expressão acima replica esse mesmo comportamento via o operador `is not { }` sobre o valor anulável do `FirstOrDefault`.)

- [ ] **Step 4: Remover as rotas antigas de `LicenseWorkspace.razor`**

Em `Condotify/Components/Pages/LicenseWorkspace.razor`:
- Remover o `case "encomendas": <DeliveriesModule .../> break;` (linhas 71-73 hoje).
- Remover o `case "acessos": <AccessEventsModule .../> break;` (linhas 86-88 hoje).
- Remover a entrada `new("encomendas", "Encomendas", ...)` do grupo `"Operação"` (linha 129 hoje).
- Remover a entrada `new("acessos", "Acessos", ...)` do grupo `"Monitoramento"` (linha 123 hoje).
- Em `DefaultSection` (linhas 150-165 hoje), remover a linha `: Has(LicensePermission.ViewDeliveries) && IsModuleEnabled(Condotify.Models.LicenseModuleEnum.Deliveries) ? "encomendas"` e a linha `: Has(LicensePermission.ViewEvents) ? "acessos"`.
- Em `SectionAllowed` (linhas 202-218 hoje), remover os cases `"encomendas" => Has(LicensePermission.ViewDeliveries),` e `"acessos" => Has(LicensePermission.ViewEvents),`.
- Em `ModuleFor` (linhas 220-233 hoje), remover o case `"encomendas" => Condotify.Models.LicenseModuleEnum.Deliveries,`.

Isto não quebra links antigos de forma silenciosa: qualquer navegação para `/licencas/{id}/encomendas` ou `/licencas/{id}/acessos` cai no `default` do `switch` (que já existe, renderiza `OverviewModule`) porque `CurrentSection` não bate com nenhum `case` restante — comportamento aceitável (a spec não pediu um redirect explícito, e a Visão geral é um destino razoável para um link desatualizado). Se algum outro componente do portal ainda linkar explicitamente para essas rotas antigas (`Href="/licencas/{id}/encomendas"` em algum menu/atalho), procurar com `grep -rn "licencas/{.*}/encomendas\|licencas/{.*}/acessos" Condotify/Components` e atualizar esses links para `/portaria` antes de finalizar esta task.

- [ ] **Step 5: Apagar os módulos antigos**

```bash
git rm Condotify/Components/LicenseModules/DeliveriesModule.razor
git rm Condotify/Components/LicenseModules/AccessEventsModule.razor
```

- [ ] **Step 6: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/portaria-task5-check && rm -rf /tmp/portaria-task5-check`
Expected: build limpo — nenhuma referência remanescente a `DeliveriesModule`/`AccessEventsModule` em nenhum arquivo (o build falha com "tipo não encontrado" se sobrar alguma).

- [ ] **Step 7: Verificação manual**

Sem suíte de testes de UI: rodar o portal localmente (`dotnet run --project Condotify`), navegar para `/portaria`, confirmar que as três abas aparecem (Agenda, Encomendas, Eventos), que registrar/cancelar/entregar uma encomenda funciona, que a busca e o filtro de eventos funcionam, e que `/licencas/{id}` não mostra mais "Encomendas"/"Acessos" na navegação lateral.

- [ ] **Step 8: Commit**

```bash
git add Condotify/Components/Concierge/ConciergePackagesTab.razor Condotify/Components/Concierge/ConciergeEventsTab.razor \
        Condotify/Components/_Imports.razor Condotify/Components/Pages/Concierge.razor Condotify/Components/Pages/LicenseWorkspace.razor
git rm Condotify/Components/LicenseModules/DeliveriesModule.razor Condotify/Components/LicenseModules/AccessEventsModule.razor
git commit -m "feat(concierge): consolidar Encomendas e Eventos como abas dentro de /portaria"
```

---

## Task 6: `ConciergeHub` — infraestrutura SignalR + autenticação por grupo

**Files:**
- Create: `CondotifyAPI/Hubs/ConciergeHub.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/ConciergeHubTests.cs`

**Interfaces:**
- Consumes: `ILicenseAuthorizationService.HasPermissionAsync(ClaimsPrincipal, Guid licenseId, LicensePermissionEnum, CancellationToken)` (já existe).
- Produces: `ConciergeHub` (rota `/hubs/concierge`), método de cliente `JoinLicenseGroup(Guid licenseId)`. Grupo `$"license-{licenseId}"` — nome usado por todas as publicações das Tasks 7-8.

- [ ] **Step 1: Escrever o teste (falha primeiro)**

```csharp
// CondotifyAPI.Tests/ConciergeHubTests.cs
using System.Security.Claims;
using CondotifyAPI.Hubs;
using CondotifyAPI.Services.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CondotifyAPI.Tests;

public sealed class ConciergeHubTests
{
    private sealed class FakeLicenseAuthorizationService(bool allowed) : ILicenseAuthorizationService
    {
        public Task<LicenseAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, Guid licenseId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> HasPermissionAsync(ClaimsPrincipal principal, Guid licenseId, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
        public Task<HashSet<Guid>> GetAccessibleLicenseIdsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, LicensePermissionEnum>> GetLicensePermissionsAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<HashSet<Guid>> GetLicenseIdsWithPermissionAsync(ClaimsPrincipal principal, LicensePermissionEnum permission, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public readonly List<(string ConnectionId, string GroupName)> Added = [];
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) { Added.Add((connectionId, groupName)); return Task.CompletedTask; }
        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ConciergeHub BuildHub(bool allowed, out FakeGroupManager groups)
    {
        groups = new FakeGroupManager();
        var hub = new ConciergeHub(new FakeLicenseAuthorizationService(allowed))
        {
            Groups = groups,
            Context = new HubCallerContextStub()
        };
        return hub;
    }

    private sealed class HubCallerContextStub : HubCallerContext
    {
        public override string ConnectionId { get; } = "conn-1";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "TestAuth"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted { get; } = CancellationToken.None;
        public override void Abort() { }
    }

    [Fact]
    public async Task JoinLicenseGroup_WhenAuthorized_AddsToGroup()
    {
        var hub = BuildHub(allowed: true, out var groups);

        await hub.JoinLicenseGroup(Guid.NewGuid());

        Assert.Single(groups.Added);
    }

    [Fact]
    public async Task JoinLicenseGroup_WhenNotAuthorized_DoesNotAddToGroup()
    {
        var hub = BuildHub(allowed: false, out var groups);

        await hub.JoinLicenseGroup(Guid.NewGuid());

        Assert.Empty(groups.Added);
    }
}
```

Este teste usa `Microsoft.AspNetCore.Http.Features` (`IFeatureCollection`, `FeatureCollection`) — adicionar `using Microsoft.AspNetCore.Http.Features;` no topo do arquivo de teste. `IGroupManager` e `HubCallerContext` são tipos abstratos do próprio ASP.NET Core (`Microsoft.AspNetCore.SignalR`), já disponíveis no projeto de testes porque `CondotifyAPI.Tests` referencia `CondotifyAPI` (que já tem o shared framework via `Microsoft.NET.Sdk.Web`) — se o build reclamar de referência faltando, adicionar `<FrameworkReference Include="Microsoft.AspNetCore.App" />` ao `CondotifyAPI.Tests.csproj` (verificar primeiro se já não está presente por herança do projeto referenciado).

- [ ] **Step 2: Rodar e confirmar falha**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~ConciergeHubTests"`
Expected: FAIL — `CondotifyAPI.Hubs.ConciergeHub` ainda não existe.

- [ ] **Step 3: Implementar o hub**

```csharp
// CondotifyAPI/Hubs/ConciergeHub.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using CondotifyAPI.Services.Authorization;

namespace CondotifyAPI.Hubs;

// Hub dedicado a atualizacoes ao vivo da Portaria (agenda de visitas, eventos de
// acesso, encomendas). Cada conexao entra em um grupo por licenca ("license-{id}")
// somente depois de confirmar que o principal autenticado tem permissao de ver
// eventos daquela licenca -- sem essa checagem, um porteiro autenticado poderia se
// inscrever no grupo de outra licenca e vazar eventos de outro condominio. Ver
// docs/superpowers/plans/2026-08-09-portaria-ux-reform.md, Task 6.
[Authorize]
public sealed class ConciergeHub(ILicenseAuthorizationService licenseAuth) : Hub
{
    public async Task JoinLicenseGroup(Guid licenseId)
    {
        var allowed = await licenseAuth.HasPermissionAsync(Context.User!, licenseId, LicensePermissionEnum.ViewEvents, Context.ConnectionAborted);
        if (!allowed) return;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(licenseId));
    }

    public static string GroupName(Guid licenseId) => $"license-{licenseId}";
}
```

- [ ] **Step 4: Rodar o teste de novo**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~ConciergeHubTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Registrar SignalR e o JWT sobre WebSocket em `Program.cs`**

Adicionar `builder.Services.AddSignalR();` logo após a linha `builder.Services.AddControllers()...` (linhas 138-142 hoje), antes de `AddExceptionHandler`.

O cliente SignalR do navegador não consegue enviar cabeçalhos customizados no handshake do WebSocket — o token JWT precisa vir via query string (`?access_token=...`), e o middleware de autenticação JWT precisa ser instruído a procurá-lo lá quando a requisição for para o hub. Adicionar um `OnMessageReceived` ao `AddJwtBearer` (linhas 101-114 hoje):

```csharp
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/concierge"))
                context.Token = accessToken;
            return Task.CompletedTask;
        }
    };
});
```

(`JwtBearerEvents` está em `Microsoft.AspNetCore.Authentication.JwtBearer`, mesmo `using` já presente no arquivo para `JwtBearerDefaults`.)

Mapear o hub logo após `app.MapControllers();` (linha 327 hoje):

```csharp
app.MapControllers();
app.MapHub<CondotifyAPI.Hubs.ConciergeHub>("/hubs/concierge");
```

- [ ] **Step 6: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task6-check && rm -rf /tmp/portaria-task6-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo, toda a suíte passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Hubs/ConciergeHub.cs CondotifyAPI/Program.cs CondotifyAPI.Tests/ConciergeHubTests.cs
git commit -m "feat(concierge): hub SignalR dedicado com autenticacao por grupo de licenca"
```

---

## Task 7: Publicar `VisitStatusChanged` e `DeliveryUpdated`

**Files:**
- Modify: `CondotifyAPI/Controllers/ConciergeController.cs`
- Modify: `CondotifyAPI/Controllers/LicenseStructureController.cs`
- Modify: `CondotifyAPI.Tests/CondotifyAPI.Tests.csproj`
- Test: `CondotifyAPI.Tests/ConciergeHubPublishTests.cs`

**Interfaces:**
- Consumes: `ConciergeHub.GroupName(Guid)` (Task 6).
- Produces: mensagens de hub `VisitStatusChanged` (payload `ConciergeVisitOut`) e `DeliveryUpdated` (payload `DeliveryOut`).

Este teste não abre uma conexão SignalR real (isso seria um teste de integração de infraestrutura, fora do escopo aqui) — verifica, com um `IHubContext`/`IClientProxy` falso, que o grupo e o nome do evento certos são usados quando o status muda.

- [ ] **Step 1: Escrever o teste (falha primeiro)**

```csharp
// CondotifyAPI.Tests/ConciergeHubPublishTests.cs
using CondotifyAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;

namespace CondotifyAPI.Tests;

public sealed class ConciergeHubPublishTests
{
    [Fact]
    public async Task PublishToLicenseGroup_SendsToCorrectGroupAndMethod()
    {
        var clientProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(x => x.Group(ConciergeHub.GroupName(TestLicenseId))).Returns(clientProxy.Object);
        var hubContext = new Mock<IHubContext<ConciergeHub>>();
        hubContext.Setup(x => x.Clients).Returns(clients.Object);

        await hubContext.Object.Clients.Group(ConciergeHub.GroupName(TestLicenseId))
            .SendAsync("VisitStatusChanged", new { Id = Guid.NewGuid() });

        clients.Verify(x => x.Group(ConciergeHub.GroupName(TestLicenseId)), Times.Once);
        clientProxy.Verify(x => x.SendCoreAsync("VisitStatusChanged", It.IsAny<object[]>(), default), Times.Once);
    }

    private static readonly Guid TestLicenseId = Guid.NewGuid();
}
```

`CondotifyAPI.Tests.csproj` hoje não referencia `Moq` (confirmado — só tem `coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`). Adicionar ao `ItemGroup` de `PackageReference` do `.csproj`:

```xml
<PackageReference Include="Moq" Version="4.20.72" />
```

Este teste específico prova o mecanismo de mock/verificação em si (não depende do código de produção ainda), então ele passa mesmo antes do Step 3 — o valor real desta task está nos Steps 3-4, que verificam manualmente/por leitura que os controllers chamam exatamente esse padrão.

- [ ] **Step 2: Rodar o teste**

Run: `dotnet test CondotifyAPI.Tests --filter "FullyQualifiedName~ConciergeHubPublishTests"`
Expected: PASS (o teste valida o mecanismo de mock, independente do código de produção — ver nota acima).

- [ ] **Step 3: Publicar `VisitStatusChanged` em `ConciergeController`**

Adicionar `IHubContext<ConciergeHub> hub` ao construtor (linha 25-28 hoje):

```csharp
public sealed class ConciergeController(
    DatabaseContext context,
    IPrivateMediaStore media,
    IHubContext<CondotifyAPI.Hubs.ConciergeHub> hub,
    IPlatformPushNotifier? push = null) : ControllerBase
```

Adicionar `using Microsoft.AspNetCore.SignalR;` ao topo do arquivo.

Em `UpdateStatus` (linhas 289-316 hoje), logo após `await context.SaveChangesAsync();` (linha 309) e antes do `await NotifyVisitAsync(...)`:

```csharp
        await context.SaveChangesAsync();
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(
```

Fazer o mesmo em `DecideApproval` (linhas 225-247 hoje) — aprovar/recusar também muda `visit.Status`, então a Agenda de acessos de outro porteiro precisa saber:

```csharp
        await context.SaveChangesAsync();
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(
```

E em `ScanVisit` (linhas 318-350 hoje, ainda que não usado pela UI do desktop hoje — o endpoint já é chamado pelo app mobile, então manter o hub consistente com todo caminho que muda `visit.Status`):

```csharp
        await context.SaveChangesAsync();
        await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("VisitStatusChanged", ToOut(visit), HttpContext.RequestAborted);
        await NotifyVisitAsync(visit, "Visitante na portaria", $"A entrada de {visit.VisitorName} foi registrada.", $"visitor-scan:{visit.Id:N}");
```

- [ ] **Step 4: Publicar `DeliveryUpdated` em `LicenseStructureController`**

Adicionar `IHubContext<CondotifyAPI.Hubs.ConciergeHub> hub` ao construtor (já modificado na Task 2 — acrescentar mais este parâmetro, não substituir `media`):

```csharp
    private readonly DatabaseContext _context;
    private readonly IAccessControlService _accessControlService;
    private readonly IMapper _mapper;
    private readonly IRecycleBinService _recycleBin;
    private readonly IPrivateMediaStore _media;
    private readonly IHubContext<CondotifyAPI.Hubs.ConciergeHub> _hub;
    private readonly IPlatformPushNotifier? _push;

    public LicenseStructureController(
        DatabaseContext context,
        IAccessControlService accessControlService,
        IMapper mapper,
        IRecycleBinService recycleBin,
        IPrivateMediaStore media,
        IHubContext<CondotifyAPI.Hubs.ConciergeHub> hub,
        IPlatformPushNotifier? push = null)
    {
        _context = context;
        _accessControlService = accessControlService;
        _mapper = mapper;
        _recycleBin = recycleBin;
        _media = media;
        _hub = hub;
        _push = push;
    }
```

Adicionar `using Microsoft.AspNetCore.SignalR;` ao topo do arquivo.

Em `CreateDelivery`, logo após `await _context.SaveChangesAsync();` (dentro do método, já modificado na Task 2):

```csharp
        var delivery = await CreateDeliveryCore(_context, _media, licenseId, input, HttpContext.RequestAborted);
        await _context.SaveChangesAsync();
        await _hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("DeliveryUpdated", ToDeliveryOut(delivery), HttpContext.RequestAborted);

        await NotifyDeliveryAsync(
```

Em `UpdateDeliveryStatus`, logo após `await _context.SaveChangesAsync();` (linha ~685 hoje):

```csharp
        await _context.SaveChangesAsync();
        await _hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("DeliveryUpdated", ToDeliveryOut(delivery), HttpContext.RequestAborted);
        await NotifyDeliveryAsync(
```

- [ ] **Step 5: Build + suíte completa**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task7-check && rm -rf /tmp/portaria-task7-check`
Run: `dotnet test CondotifyAPI.Tests`
Expected: build limpo (a injeção de `IHubContext<ConciergeHub>` é resolvida automaticamente pelo ASP.NET Core assim que `AddSignalR()` estiver registrado — Task 6 — nenhum registro manual adicional necessário), toda a suíte passa.

- [ ] **Step 6: Commit**

```bash
git add CondotifyAPI/Controllers/ConciergeController.cs CondotifyAPI/Controllers/LicenseStructureController.cs \
        CondotifyAPI.Tests/CondotifyAPI.Tests.csproj CondotifyAPI.Tests/ConciergeHubPublishTests.cs
git commit -m "feat(concierge): publicar VisitStatusChanged e DeliveryUpdated no grupo SignalR da licenca"
```

---

## Task 8: Publicar `AccessEventRecorded` do worker de ingestão

**Files:**
- Modify: `CondotifyAPI/Services/AccessControl/AccessEventIngestionWorker.cs`

**Interfaces:**
- Consumes: `ConciergeHub.GroupName(Guid)` (Task 6).
- Produces: mensagem de hub `AccessEventRecorded` (payload `ConciergeEventOut`).

Este worker roda fora do pipeline HTTP (`BackgroundService`, cria seu próprio escopo de DI a cada ciclo — mesmo padrão já em uso para `ICurrentTenantAccessor.MarkUnrestricted()`, linha 40 hoje). Sem teste de integração de SignalR real aqui pelo mesmo motivo da Task 7 — a mudança é pequena e mecânica o suficiente para revisão por leitura + verificação manual.

- [ ] **Step 1: Resolver `IHubContext<ConciergeHub>` no mesmo escopo já usado pelo worker**

Em `IngestAsync` (linhas 34-82 hoje), logo após a linha que resolve `mapper` (linha 42):

```csharp
            var mapper = scope.ServiceProvider.GetRequiredService<IMapper>();
            var hub = scope.ServiceProvider.GetRequiredService<IHubContext<CondotifyAPI.Hubs.ConciergeHub>>();
```

Adicionar `using Microsoft.AspNetCore.SignalR;` e `using CondotifyAPI.Hubs;` ao topo do arquivo.

- [ ] **Step 2: Coletar os eventos recém-persistidos e publicar depois do `SaveChangesAsync` bem-sucedido**

`PersistAsync` (linhas 84-140 hoje) é `static` e não tem acesso a `hub` — em vez de publicar de dentro dela (o que arriscaria notificar um evento que ainda pode falhar ao salvar), ela passa a devolver a lista de payloads recém-criados, e quem publica é `IngestAsync`, só depois que `SaveChangesAsync` confirma.

Trocar a assinatura de `PersistAsync`:

```csharp
    private static async Task<List<(Guid LicenseId, ConciergeEventOut Payload)>> PersistAsync(
        DatabaseContext context,
        Guid deviceId,
        Guid licenseId,
        string deviceName,
        IReadOnlyList<DeviceAccessEvent> events,
        Dictionary<Guid, HashSet<Guid>> reconciliationByLicense,
        CancellationToken cancellationToken)
    {
        var published = new List<(Guid, ConciergeEventOut)>();
        if (events.Count == 0) return published;
        var externalIds = events.Select(x => StableEventId(x)).Distinct().ToList();
        var existingIds = await context.AccessEventRecords.AsNoTracking()
            .Where(x => x.DeviceId == deviceId && externalIds.Contains(x.ExternalEventId))
            .Select(x => x.ExternalEventId).ToHashSetAsync(cancellationToken);
        var bindings = await context.ResidentAccessDevices
            .Include(x => x.Credential).ThenInclude(x => x.Resident)
            .Where(x => x.DeviceId == deviceId)
            .ToListAsync(cancellationToken);

        foreach (var accessEvent in events.OrderBy(x => x.OccurredAt))
        {
            var externalId = StableEventId(accessEvent);
            if (!existingIds.Add(externalId)) continue;
            var binding = ResolveBinding(bindings, accessEvent);
            var credential = binding?.Credential;
            var recordId = Guid.NewGuid();
            context.AccessEventRecords.Add(new AccessEventRecordDTO
            {
                Id = recordId, LicenseId = licenseId, DeviceId = deviceId, CredentialId = credential?.Id,
                ExternalEventId = externalId, Event = Short(accessEvent.Event, 120), Authorized = accessEvent.Authorized,
                OccurredAt = Utc(accessEvent.OccurredAt), ExternalUserId = Short(accessEvent.ExternalUserId, 150),
                PersonName = Short(accessEvent.PersonName, 200), Credential = Short(accessEvent.Credential, 200),
                Portal = Short(accessEvent.Portal, 120), Details = Short(accessEvent.Details, 1000), CreatedAt = DateTime.UtcNow
            });
            published.Add((licenseId, new ConciergeEventOut
            {
                Id = recordId, DeviceName = deviceName, PersonName = Short(accessEvent.PersonName, 200),
                Event = Short(accessEvent.Event, 120), Authorized = accessEvent.Authorized,
                Portal = Short(accessEvent.Portal, 120), OccurredAt = Utc(accessEvent.OccurredAt)
            }));

            if (!accessEvent.Authorized || credential is null || !credential.IsActive) continue;
            credential.UseCount++;
            credential.UpdatedAt = DateTime.UtcNow;
            if (credential.MaxUses is not > 0 || credential.UseCount < credential.MaxUses) continue;
            credential.IsActive = false;
            foreach (var item in credential.Devices)
            {
                item.IsSynced = false;
                item.SyncStatus = CredentialSyncStatusEnum.RemovalPending;
                item.NextAttemptAt = DateTime.UtcNow;
            }
            if (!reconciliationByLicense.TryGetValue(licenseId, out var ids))
                reconciliationByLicense[licenseId] = ids = [];
            ids.Add(credential.Id);
            context.AccessOperationAudits.Add(new AccessOperationAuditDTO
            {
                Id = Guid.NewGuid(), LicenseId = licenseId, EntityType = "Credential", EntityId = credential.Id,
                Action = "UsageLimitReached", Status = "Queued",
                Summary = $"{credential.Resident.Name}: limite de {credential.MaxUses} utilizacao(oes) atingido.",
                DetailsJson = JsonSerializer.Serialize(new { credential.UseCount, credential.MaxUses, deviceId, externalId }),
                UserName = "Monitor automatico", CreatedAt = DateTime.UtcNow
            });
        }
        return published;
    }
```

(Nota: `deviceName` vira um parâmetro novo, já que `ConciergeEventOut.DeviceName` precisa do nome do dispositivo, e `PersistAsync` só recebia `deviceId` antes.)

Em `IngestAsync`, coletar os payloads de cada dispositivo e publicar depois do `SaveChangesAsync`:

```csharp
            var devices = await context.Devices.Where(x => x.IsActive).ToListAsync(cancellationToken);
            var reconciliationByLicense = new Dictionary<Guid, HashSet<Guid>>();
            var pendingPublish = new List<(Guid LicenseId, ConciergeEventOut Payload)>();

            foreach (var device in devices)
            {
                try
                {
                    var events = await accessControl.GetAccessEventsAsync(mapper.Map<AccessControlDevice>(device), 200);
                    pendingPublish.AddRange(await PersistAsync(context, device.Id, device.LicenseId, device.Name, events, reconciliationByLicense, cancellationToken));
                    device.LastHealthCheckAt = DateTime.UtcNow;
                    device.LastSeenAt = DateTime.UtcNow;
                    device.HealthMessage = "Online; eventos atualizados.";
                }
                catch (Exception exception)
                {
                    device.LastHealthCheckAt = DateTime.UtcNow;
                    device.HealthMessage = Short(exception.Message, 300);
                    _logger.LogDebug(exception, "Nao foi possivel coletar eventos do equipamento {DeviceId}", device.Id);
                }
            }

            foreach (var (licenseId, credentialIds) in reconciliationByLicense.Where(x => x.Value.Count > 0))
            {
                context.AccessBatchOperations.Add(new AccessBatchOperationDTO
                {
                    Id = Guid.NewGuid(), LicenseId = licenseId, Operation = "ReconcileCredentials",
                    Status = AccessBatchStatusEnum.Queued, RequestedBy = "Limite automatico de utilizacoes",
                    FilterJson = JsonSerializer.Serialize(new { credentialIds }), CreatedAt = DateTime.UtcNow
                });
            }
            await context.SaveChangesAsync(cancellationToken);

            foreach (var (licenseId, payload) in pendingPublish)
                await hub.Clients.Group(CondotifyAPI.Hubs.ConciergeHub.GroupName(licenseId)).SendAsync("AccessEventRecorded", payload, cancellationToken);
```

A publicação roda depois do `SaveChangesAsync` — se salvar falhar (exceção capturada pelo `catch` externo, linha 78-81 hoje), nada é publicado, evitando notificar a UI sobre um evento que não persistiu de fato.

- [ ] **Step 2: Build**

Run: `dotnet build CondotifyAPI/CondotifyAPI.csproj -o /tmp/portaria-task8-check && rm -rf /tmp/portaria-task8-check`
Expected: build limpo.

- [ ] **Step 3: Suíte completa**

Run: `dotnet test CondotifyAPI.Tests`
Expected: toda a suíte passa (nenhum teste existente exercita `AccessEventIngestionWorker` diretamente — mudança verificada por build limpo + revisão de código).

- [ ] **Step 4: Commit**

```bash
git add CondotifyAPI/Services/AccessControl/AccessEventIngestionWorker.cs
git commit -m "feat(concierge): publicar AccessEventRecorded apos persistir eventos do worker de ingestao"
```

---

## Task 9: Cliente SignalR no portal — `Concierge.razor`

**Files:**
- Modify: `Condotify/Condotify.csproj`
- Modify: `Condotify/Components/Pages/Concierge.razor`
- Modify: `Condotify/Components/Concierge/ConciergeEventsTab.razor`
- Modify: `Condotify/Components/Concierge/ConciergePackagesTab.razor`

**Interfaces:**
- Consumes: hub `/hubs/concierge` (Task 6), mensagens `VisitStatusChanged`/`DeliveryUpdated`/`AccessEventRecorded` (Tasks 7-8), `ConciergeEventsTab.PrependEvent` e `ConciergePackagesTab.ReloadAsync`/`ConciergeEventsTab.ReloadAsync` (Task 5).

- [ ] **Step 1: Adicionar o pacote cliente**

Em `Condotify/Condotify.csproj`, no `ItemGroup` de `PackageReference` (linhas 11-15 hoje):

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="8.0.24" />
    <PackageReference Include="Microsoft.VisualStudio.Azure.Containers.Tools.Targets" Version="1.20.1" />
    <PackageReference Include="MudBlazor" Version="9.7.0" />
    <PackageReference Include="QRCoder" Version="1.8.0" />
  </ItemGroup>
```

(`8.0.24` para bater com a mesma versão já fixada para `Microsoft.AspNetCore.Authentication.JwtBearer` em `CondotifyAPI.csproj` e evitar conflitos de versão entre pacotes `Microsoft.AspNetCore.*` na solução.)

- [ ] **Step 2: Conectar e assinar o hub em `Concierge.razor`**

`Condotify.ApiClient/ISessionContextProvider.cs` já expõe exatamente o que é preciso: `ValueTask<string?> GetAccessTokenAsync(CancellationToken)` — o mesmo token Bearer que `CondotifyApiClient` usa em toda requisição HTTP (a web resolve a partir dos claims do cookie de sessão, o app MAUI a partir do SecureStorage; `ISessionContextProvider` já é injetável via DI, não precisa de um mecanismo novo). Adicionar ao topo do arquivo:

```razor
@using Microsoft.AspNetCore.SignalR.Client
@inject NavigationManager Navigation
@inject ISessionContextProvider SessionContext
```

No `@code`, adicionar:

```csharp
    private HubConnection? _hubConnection;
    private bool _reconnecting;

    private async Task ConnectHubAsync(Guid licenseId)
    {
        await DisconnectHubAsync();
        var token = await SessionContext.GetAccessTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(Navigation.ToAbsoluteUri("/hubs/concierge"), options => options.AccessTokenProvider = () => Task.FromResult<string?>(token))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.Reconnecting += _ => { _reconnecting = true; return InvokeAsync(StateHasChanged); };
        _hubConnection.Reconnected += async _ => { _reconnecting = false; await _hubConnection.InvokeAsync("JoinLicenseGroup", licenseId); await InvokeAsync(StateHasChanged); };

        // VisitStatusChanged/DeliveryUpdated disparam um recarregamento local (chamada HTTP
        // ja existente, GetConciergeDashboardAsync/GetDeliveriesAsync) em vez de mesclar o
        // payload recebido diretamente no estado -- e uma simplificacao deliberada (evita bugs
        // sutis de merge de estado parcial) que ainda cumpre o resultado pedido pela spec
        // ("atualiza a linha e os KPIs"): a diferenca pratica e uma chamada HTTP extra e quase
        // instantanea, nao mais um dashboard que so atualiza a cada 15-60s. So o
        // AccessEventRecorded (abaixo) faz merge local de verdade, via ConciergeEventsTab.PrependEvent,
        // porque inserir uma linha no topo de uma lista e trivial e nao tem risco de
        // inconsistencia como mesclar uma visita/entrega inteira.
        _hubConnection.On<ConciergeVisitViewModel>("VisitStatusChanged", async _ => await InvokeAsync(LoadDashboardAsync));
        _hubConnection.On<DeliveryRowViewModel>("DeliveryUpdated", async _ => { if (_packagesTab is not null) await InvokeAsync(_packagesTab.ReloadAsync); });
        _hubConnection.On<ConciergeEventViewModel>("AccessEventRecorded", async accessEvent =>
        {
            await InvokeAsync(() => { _eventsTab?.PrependEvent(accessEvent); return Task.CompletedTask; });
        });

        try
        {
            await _hubConnection.StartAsync();
            await _hubConnection.InvokeAsync("JoinLicenseGroup", licenseId);
        }
        catch (Exception)
        {
            // Falha ao conectar nao bloqueia a tela -- o polling de 60s (fallback) continua funcionando.
        }
    }

    private async Task DisconnectHubAsync()
    {
        if (_hubConnection is null) return;
        await _hubConnection.DisposeAsync();
        _hubConnection = null;
    }
```

- [ ] **Step 3: Conectar/desconectar no ciclo de vida certo, reduzir o polling para 60s**

Em `SelectLicenseAsync` (linhas 168-180 hoje), depois de `StartRefreshLoop();`:

```csharp
    private async Task SelectLicenseAsync(Guid? licenseId)
    {
        _licenseId = licenseId;
        _dashboard = null;
        _error = null;
        StopRefreshLoop();
        await DisconnectHubAsync();
        if (!licenseId.HasValue) return;
        _loading = true;
        var routesResult = await Api.GetAccessRoutesAsync(licenseId.Value);
        _routes = routesResult.Success ? routesResult.Value ?? [] : [];
        await LoadDashboardAsync();
        StartRefreshLoop();
        await ConnectHubAsync(licenseId.Value);
    }
```

Em `StartRefreshLoop` (linha 260-265 hoje), trocar o intervalo:

```csharp
    private void StartRefreshLoop()
    {
        _refreshCancellation = new CancellationTokenSource();
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        _ = RefreshLoopAsync(_refreshCancellation.Token);
    }
```

Em `DisposeAsync` (linha 272 hoje):

```csharp
    public async ValueTask DisposeAsync() { StopRefreshLoop(); await DisconnectHubAsync(); }
```

(A assinatura muda de `ValueTask DisposeAsync() { ...; return ValueTask.CompletedTask; }` para `async ValueTask DisposeAsync()` — `@implements IAsyncDisposable` já está declarado no topo do arquivo, linha 5, nenhuma mudança de diretiva necessária.)

- [ ] **Step 4: Indicador de reconexão**

Perto do `<span class="live-indicator">` (linha 21-24 hoje), adicionar:

```razor
    @if (_dashboard is not null)
    {
        @if (_reconnecting)
        {
            <MudChip T="string" Size="Size.Small" Color="Color.Warning" Icon="@Icons.Material.Outlined.SyncProblem">Reconectando…</MudChip>
        }
        else
        {
            <span class="live-indicator"><i></i> Atualização automática</span>
        }
    }
```

- [ ] **Step 5: Build**

Run: `dotnet build Condotify/Condotify.csproj -o /tmp/portaria-task9-check && rm -rf /tmp/portaria-task9-check`
Expected: build limpo.

- [ ] **Step 6: Verificação manual**

Sem suíte de testes de UI: rodar `CondotifyAPI` e `Condotify` localmente, abrir `/portaria` em duas abas do navegador autenticadas na mesma licença, registrar entrada/saída de uma visita em uma aba e confirmar que a outra atualiza sem apertar "Atualizar". Registrar uma encomenda e confirmar que a aba Encomendas da outra sessão atualiza. Se possível, simular um evento de acesso (ou aguardar o ciclo de 2 minutos do `AccessEventIngestionWorker` contra um dispositivo de teste) e confirmar que a aba Eventos recebe a linha nova sem recarregar. Desligar a API momentaneamente e confirmar que aparece "Reconectando…" e que, ao religar, a tela volta a atualizar sozinha (com ou sem esperar os 60s do fallback).

- [ ] **Step 7: Commit**

```bash
git add Condotify/Condotify.csproj Condotify/Components/Pages/Concierge.razor \
        Condotify/Components/Concierge/ConciergeEventsTab.razor Condotify/Components/Concierge/ConciergePackagesTab.razor
git commit -m "feat(concierge): conectar Concierge.razor ao ConciergeHub, com fallback de polling em 60s"
```

---

## Final check (todas as tasks completas)

- [ ] `dotnet build Condotify.sln` limpo (usar `-o` para pasta temporária se algum `dotnet run` estiver ativo; ignorar falhas pré-existentes e não relacionadas do target iOS de `Condotify.Mobile`, já documentadas como um gap de empacotamento do ambiente, não deste plano).
- [ ] `dotnet test CondotifyAPI.Tests` — todos os testes passam, incluindo os novos de placa, foto de encomenda, feed de eventos e hub SignalR.
- [ ] Nenhuma referência remanescente a `DeliveriesModule`/`AccessEventsModule` em nenhum arquivo do portal (`grep -rn "DeliveriesModule\|AccessEventsModule" Condotify/`).
- [ ] Verificação manual ponta a ponta descrita no Step 6 da Task 9.
- [ ] Revisar se o campo `EnabledModules` de uma licença sem nenhum módulo configurado (`0` ou ausente) continua mostrando a aba Encomendas na Portaria (comportamento de fallback "tudo habilitado", consistente com `LicenseWorkspace.razor`) — testar manualmente selecionando uma licença de desenvolvimento sem configuração explícita de módulos.
