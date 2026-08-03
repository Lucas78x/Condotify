# Refinamento das telas restantes do Condotify Mobile — Plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Corrigir os vazamentos de enum cru (status/tipo em inglês/PascalCase aparecendo direto na UI) e adicionar faixas de resumo (`.metric-grid`) onde fazem sentido, nas telas que ainda não tinham sido auditadas visualmente — sem inventar layout novo.

**Architecture:** Reaproveitar exatamente os padrões já usados em `UnitDetails.razor`/`Visitors.razor`/`Alerts.razor`/`Concierge.razor`: método estático privado `StatusLabel`/`StatusClass` por página, span `<span class="row-status @StatusClass(x)">@StatusLabel(x)</span>`, e `.metric-grid` com contagens calculadas client-side a partir da lista já carregada (sem endpoint novo).

**Tech Stack:** .NET MAUI Blazor Hybrid (Condotify.Mobile), MudBlazor, CSS já existente em `Condotify.Mobile/wwwroot/css/app.css`.

## Global Constraints

- Nenhuma tela pode introduzir "selo de confiança" decorativo (ex: "100% seguro", "ambiente monitorado") sem uma feature real por trás — ver `docs/superpowers/specs/2026-08-03-mobile-remaining-pages-polish-design.md`.
- Reaproveitar classes CSS existentes (`.metric-grid`, `.metric`, `.row-status`, `.content-panel`, `.list-row`) — não criar classes novas para necessidades já cobertas.
- Mapas de rótulo (`StatusLabel`, `TypeLabel`, `CategoryLabel`) são métodos estáticos privados por página, no mesmo padrão já usado em `UnitDetails.razor`/`PersonDetails.razor`/`Concierge.razor`/`Alerts.razor` — não extrair um helper compartilhado novo.
- Todo texto exibido ao usuário é em português.
- Build/deploy no Android é lento (minutos) e o dispositivo físico não aceita toque simulado — a verificação em dispositivo acontece uma única vez, na Task 5, cobrindo tudo (incluindo as remoções de selo falso já feitas no login e no menu lateral).

---

### Task 1: Encomendas — status e tipo traduzidos + faixa de resumo

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Deliveries.razor`

**Interfaces:**
- Consumes: `DeliveryRowViewModel` (`Condotify.Contracts`) já carregado em `_rows`, campos `Status` (valores crus: `"Pending"`, `"Received"`, `"Delivered"`, `"Canceled"`) e `Type` (nome cru do enum `DeliveryTypeEnum`, ex: `"MercadoLivre"`, `"TotalExpress"`, `"UberEats"`).
- Produces: nada consumido por outras tarefas.

- [ ] **Passo 1: adicionar faixa de resumo e trocar o texto cru de status/tipo por rótulos + selo colorido**

Substituir o bloco `<PageState ...>` inteiro por:

```razor
<PageState Loading="_loading" Error="@_error" Empty="@(_rows.Count == 0)" EmptyTitle="Nenhuma encomenda" EmptyText="Recebimentos recentes aparecerão aqui." Retry="LoadAsync">
    <div class="metric-grid">
        <div class="metric warning"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.PendingActions" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Pending")</span><span class="metric-label">Aguardando</span></div>
        <div class="metric info"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Received")</span><span class="metric-label">Na portaria</span></div>
        <div class="metric success"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.CheckCircleOutline" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Delivered")</span><span class="metric-label">Entregues</span></div>
        <div class="metric primary"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.FactCheck" /></span><span class="metric-value">@_rows.Count</span><span class="metric-label">Total</span></div>
    </div>
    <section class="content-panel">
        @foreach (var row in _rows)
        {
            <div class="list-row">
                <MudAvatar Color="@StatusColor(row.Status)" Variant="Variant.Filled"><MudIcon Icon="@Icons.Material.Outlined.Inventory2" /></MudAvatar>
                <div class="list-main">
                    <div class="list-title">@row.Name</div>
                    <div class="list-meta"><span class="row-status @StatusClass(row.Status)">@StatusLabel(row.Status)</span> · @TypeLabel(row.Type)</div>
                    <div class="list-meta">@row.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm") @(!string.IsNullOrWhiteSpace(row.TrackingCode) ? $"- {row.TrackingCode}" : "")</div>
                </div>
                @if (Session.Current?.Principal == MobilePrincipalKind.Staff && row.StatusValue == 2)
                {
                    <MudButton Size="Size.Small" Variant="Variant.Outlined" Color="Color.Success" OnClick="() => DeliverAsync(row)">Entregar</MudButton>
                }
            </div>
        }
    </section>
</PageState>
```

- [ ] **Passo 2: trocar `StatusColor` por três métodos (`StatusColor`, `StatusClass`, `StatusLabel`) e adicionar `TypeLabel`**

Substituir a linha `private static Color StatusColor(string status) => status switch { "Delivered" => Color.Success, "Canceled" => Color.Error, "Received" => Color.Info, _ => Color.Warning };` por:

```csharp
    private static Color StatusColor(string status) => status switch { "Delivered" => Color.Success, "Canceled" => Color.Error, "Received" => Color.Info, _ => Color.Warning };
    private static string StatusClass(string status) => status switch { "Delivered" => "success", "Canceled" => "error", "Received" => "info", _ => "warning" };
    private static string StatusLabel(string status) => status switch { "Delivered" => "Entregue", "Canceled" => "Cancelada", "Received" => "Na portaria", "Pending" => "Aguardando", _ => status };
    private static string TypeLabel(string type) => type switch
    {
        "Correios" => "Correios", "Sedex" => "SEDEX", "PAC" => "PAC",
        "Jadlog" => "Jadlog", "TotalExpress" => "Total Express", "AzulCargo" => "Azul Cargo", "TNT" => "TNT", "DHL" => "DHL", "FedEx" => "FedEx", "UPS" => "UPS", "Loggi" => "Loggi",
        "MercadoLivre" => "Mercado Livre", "Amazon" => "Amazon", "Shopee" => "Shopee", "AliExpress" => "AliExpress", "MagazineLuiza" => "Magazine Luiza", "CasasBahia" => "Casas Bahia", "PontoFrio" => "Ponto Frio", "Kabum" => "KaBuM!", "Americanas" => "Americanas", "Submarino" => "Submarino", "Extra" => "Extra", "Carrefour" => "Carrefour", "Havan" => "Havan", "Shein" => "Shein",
        "Ifood" => "iFood", "Rappi" => "Rappi", "UberEats" => "Uber Eats", "ZeDelivery" => "Zé Delivery", "Aiqfome" => "AiQFome", "JamesDelivery" => "James Delivery", "NoventaNoveFood" => "99Food",
        "Mercado" => "Mercado", "Farmacia" => "Farmácia", "Drogasil" => "Drogasil", "DrogaRaia" => "Droga Raia", "Panvel" => "Panvel", "PagueMenos" => "Pague Menos",
        "Agua" => "Água", "Gas" => "Gás", "Documentos" => "Documentos", "EncomendaParticular" => "Encomenda particular",
        _ => "Outros"
    };
```

- [ ] **Passo 3: compilar a Condotify.Mobile.Core/Mobile (sem executar no dispositivo ainda — isso acontece só na Task 5)**

Rodar: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android` (sem `-t:Run`, só para pegar erro de sintaxe/tipo cedo).
Esperado: `Compilação com êxito`, 0 erros.

- [ ] **Passo 4: commit**

```bash
git add Condotify.Mobile/Components/Pages/Deliveries.razor
git commit -m "feat: translate delivery status/type labels and add summary strip"
```

---

### Task 2: Reservas — status traduzido + faixa de resumo

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Bookings.razor`

**Interfaces:**
- Consumes: `AmenityBookingViewModel.Status` (valores crus: `"Pending"`, `"Confirmed"`, `"Rejected"`, `"Cancelled"`, `"Completed"`).
- Produces: nada consumido por outras tarefas.

- [ ] **Passo 1: adicionar faixa de resumo e trocar o texto cru de status pelo rótulo + selo colorido**

Substituir o bloco `<PageState ...>` inteiro por:

```razor
<PageState Loading="_loading" Error="@_error" Empty="@(_rows.Count == 0)" EmptyTitle="Nenhuma reserva" EmptyText="Os próximos agendamentos aparecerão aqui." Retry="LoadAsync">
    <div class="metric-grid">
        <div class="metric warning"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.PendingActions" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Pending")</span><span class="metric-label">Pendentes</span></div>
        <div class="metric success"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.EventAvailable" Style="width:24px;height:24px;font-size:24px" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Confirmed")</span><span class="metric-label">Confirmadas</span></div>
        <div class="metric danger"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.EventBusy" /></span><span class="metric-value">@_rows.Count(x => x.Status == "Rejected" || x.Status == "Cancelled")</span><span class="metric-label">Recusadas/canceladas</span></div>
        <div class="metric primary"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" /></span><span class="metric-value">@_rows.Count</span><span class="metric-label">Total</span></div>
    </div>
    <section class="content-panel">
        @foreach (var row in _rows)
        {
            <div class="list-row">
                <MudAvatar Color="@StatusColor(row.Status)" Variant="Variant.Filled"><MudIcon Icon="@Icons.Material.Outlined.CalendarMonth" /></MudAvatar>
                <div class="list-main">
                    <div class="list-title">@row.AmenityName</div>
                    <div class="list-meta">@row.Date.ToString("dd/MM/yyyy") - @FormatTime(row.SlotStartTime) as @FormatTime(row.SlotEndTime)</div>
                    <div class="list-meta"><span class="row-status @StatusClass(row.Status)">@StatusLabel(row.Status)</span> · @row.BlockName / @row.UnitNumber</div>
                </div>
                @if (Session.Current?.Principal == MobilePrincipalKind.Staff && row.Status == "Pending")
                {
                    <MudIconButton Icon="@Icons.Material.Outlined.Close" Color="Color.Error" OnClick="() => DecideAsync(row, false)" aria-label="Recusar" />
                    <MudIconButton Icon="@Icons.Material.Outlined.Check" Color="Color.Success" OnClick="() => DecideAsync(row, true)" aria-label="Aprovar" />
                }
                else if (Session.Current?.Principal == MobilePrincipalKind.Resident && IsCancelable(row))
                {
                    <MudIconButton Icon="@Icons.Material.Outlined.EventBusy" Color="Color.Error" Disabled="@(_cancelling == row.Id)"
                                   OnClick="() => CancelAsync(row)" aria-label="Cancelar reserva" />
                }
            </div>
        }
    </section>
</PageState>
```

- [ ] **Passo 2: acrescentar `StatusClass`/`StatusLabel` ao lado de `StatusColor`**

Substituir a linha `private static Color StatusColor(string status) => status switch { "Confirmed" => Color.Success, "Pending" => Color.Warning, "Rejected" or "Cancelled" => Color.Error, _ => Color.Default };` por:

```csharp
    private static Color StatusColor(string status) => status switch { "Confirmed" => Color.Success, "Pending" => Color.Warning, "Rejected" or "Cancelled" => Color.Error, _ => Color.Default };
    private static string StatusClass(string status) => status switch { "Confirmed" => "success", "Pending" => "warning", "Rejected" or "Cancelled" => "error", "Completed" => "neutral", _ => "info" };
    private static string StatusLabel(string status) => status switch { "Confirmed" => "Confirmada", "Pending" => "Pendente", "Rejected" => "Recusada", "Cancelled" => "Cancelada", "Completed" => "Concluída", _ => status };
```

- [ ] **Passo 3: compilar sem executar**

Rodar: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android`
Esperado: `Compilação com êxito`, 0 erros.

- [ ] **Passo 4: commit**

```bash
git add Condotify.Mobile/Components/Pages/Bookings.razor
git commit -m "feat: translate booking status labels and add summary strip"
```

---

### Task 3: Meu perfil — tipo de vínculo traduzido + foto do morador

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Profile.razor`

**Interfaces:**
- Consumes: `ResidentProfileViewModel.AccessType` (valores crus do enum `ResidentAccessTypeEnum`: `"Default"`, `"Responsible"`, `"NonResponsible"`, `"Guest"`, `"ServiceProvider"`), `ResidentProfileViewModel.PhotoUrl` (referência `/private-media/{licenseId}/{mediaId}`, mesmo formato já tratado em `PersonDetails.razor`).
- Consumes: `CondotifyApiClient.GetPersonPhotoAsync(Guid licenseId, Guid mediaId, CancellationToken)` e `CondotifyApiClient.TryParseMediaReference(string?, out Guid, out Guid)` — já existem, implementados nesta mesma sessão para `PersonDetails.razor`.
- Produces: nada consumido por outras tarefas.

- [ ] **Passo 1: mostrar a foto real do morador (com fallback de iniciais) e traduzir o tipo de vínculo**

Substituir o bloco `<section class="content-panel profile-panel">...</section>` inteiro por:

```razor
    <section class="content-panel profile-panel">
        <div class="profile-hero">
            @if (!string.IsNullOrWhiteSpace(_photoDataUrl))
            {
                <MudAvatar Size="Size.Large" Class="photo-avatar"><img src="@_photoDataUrl" alt="" /></MudAvatar>
            }
            else
            {
                <MudAvatar Size="Size.Large" Color="Color.Primary">@Initials</MudAvatar>
            }
            <div><MudText Typo="Typo.h5">@Session.Current?.Name</MudText><MudText Typo="Typo.body2" Color="Color.Secondary">@Session.Current?.Email</MudText></div>
            <MudChip T="string" Size="Size.Small" Variant="Variant.Outlined" Color="Color.Success">Ativo</MudChip>
        </div>
        @if (_resident is not null)
        {
            <div class="profile-fields">
                <div><span>Telefone</span><strong>@Value(_resident.PhoneNumber)</strong></div>
                <div><span>Vínculo</span><strong>@AccessTypeLabel(_resident.AccessType)</strong></div>
                <div><span>Condomínio</span><strong>@_resident.LicenseName</strong></div>
                <div><span>Unidades</span><strong>@_resident.Units.Count</strong></div>
            </div>
        }
        else
        {
            <div class="profile-fields">
                <div><span>Tipo de conta</span><strong>Equipe operacional</strong></div>
                <div><span>Empresa</span><strong>@(Session.Current?.EnterpriseId?.ToString("D") ?? "Nao informada")</strong></div>
            </div>
        }
    </section>
```

- [ ] **Passo 2: carregar a foto após o perfil e adicionar o rótulo do tipo de vínculo**

Substituir o `@code` inteiro por:

```csharp
@code {
    private ResidentProfileViewModel? _resident;
    private string? _photoDataUrl;
    private bool _loading;
    private string _error = string.Empty;
    private string Initials => string.Concat((Session.Current?.Name ?? "U").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => x[0])).ToUpperInvariant();

    protected override Task OnInitializedAsync() => LoadAsync();
    private async Task LoadAsync()
    {
        if (Session.Current?.Principal != MobilePrincipalKind.Resident) return;
        _loading = true; _error = string.Empty;
        var result = await Api.GetResidentProfileAsync();
        _resident = result.Value;
        _photoDataUrl = null;
        if (!result.Success) _error = result.Error ?? "Nao foi possivel carregar seu perfil.";
        _loading = false;
        if (_resident is not null) _ = LoadPhotoAsync(_resident.PhotoUrl);
    }

    private async Task LoadPhotoAsync(string? reference)
    {
        if (!CondotifyApiClient.TryParseMediaReference(reference, out var licenseId, out var mediaId)) return;
        var result = await Api.GetPersonPhotoAsync(licenseId, mediaId);
        if (!result.Success) return;
        _photoDataUrl = result.Value;
        await InvokeAsync(StateHasChanged);
    }

    private static string Value(string value) => string.IsNullOrWhiteSpace(value) ? "Nao informado" : value;
    private static string AccessTypeLabel(string accessType) => accessType switch { "Responsible" => "Responsável", "NonResponsible" => "Morador", "Guest" => "Convidado", "ServiceProvider" => "Prestador", "Default" => "Morador", _ => accessType };
}
```

- [ ] **Passo 3: compilar sem executar**

Rodar: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android`
Esperado: `Compilação com êxito`, 0 erros.

- [ ] **Passo 4: commit**

```bash
git add Condotify.Mobile/Components/Pages/Profile.razor
git commit -m "feat: show resident photo and translated access type on profile"
```

---

### Task 4: Equipamentos — faixa de resumo

**Files:**
- Modify: `Condotify.Mobile/Components/Pages/Devices.razor`

**Interfaces:**
- Consumes: `AccessDeviceRowViewModel.IsActive` (bool) já carregado em `_devices`.
- Produces: nada consumido por outras tarefas.

- [ ] **Passo 1: adicionar faixa de resumo (online/offline/total) no topo da lista**

Substituir `<PageState Loading="_loading" Error="@_error" Empty="@(_devices.Count == 0)" EmptyTitle="Nenhum equipamento" EmptyText="Cadastre equipamentos pelo portal administrativo." Retry="LoadAsync">` e a linha de abertura de `<section class="content-panel">` por:

```razor
<PageState Loading="_loading" Error="@_error" Empty="@(_devices.Count == 0)" EmptyTitle="Nenhum equipamento" EmptyText="Cadastre equipamentos pelo portal administrativo." Retry="LoadAsync">
    <div class="metric-grid">
        <div class="metric success"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.SensorDoor" /></span><span class="metric-value">@_devices.Count(x => x.IsActive)</span><span class="metric-label">Disponíveis</span></div>
        <div class="metric danger"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.SensorsOff" /></span><span class="metric-value">@_devices.Count(x => !x.IsActive)</span><span class="metric-label">Indisponíveis</span></div>
        <div class="metric primary"><span class="metric-icon"><MudIcon Icon="@Icons.Material.Outlined.DoorFront" /></span><span class="metric-value">@_devices.Count</span><span class="metric-label">Total</span></div>
    </div>
    <section class="content-panel">
```

(o restante do arquivo, incluindo o `@foreach` de dispositivos, permanece exatamente igual — só a abertura do `PageState`/`content-panel` muda; a faixa de resumo entra entre as duas tags, como nas outras telas.)

- [ ] **Passo 2: compilar sem executar**

Rodar: `dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android`
Esperado: `Compilação com êxito`, 0 erros.

- [ ] **Passo 3: commit**

```bash
git add Condotify.Mobile/Components/Pages/Devices.razor
git commit -m "feat: add summary strip to devices screen"
```

---

### Task 5: Build único, instalação e verificação visual de tudo

**Files:**
- Nenhum arquivo novo — esta tarefa só builda, instala e verifica no dispositivo físico tudo que foi implementado nas Tasks 1-4, mais as remoções de selo falso (login e menu lateral) já commitadas antes deste plano.

**Interfaces:**
- Consumes: build funcional das Tasks 1-4 e commits anteriores de remoção de selo falso.
- Produces: nada — tarefa terminal do plano.

- [ ] **Passo 1: build e deploy no dispositivo físico**

Rodar (device já deve estar autorizado via `adb devices -l`; se aparecer `unauthorized`, pedir ao usuário para tocar em "Permitir" no celular antes de prosseguir):

```bash
cd "D:/repos/Condotify" && dotnet build Condotify.Mobile/Condotify.Mobile.csproj -f net9.0-android -p:CondotifyApiBaseUrl=http://172.30.2.163:5093 -t:Run > D:/repos/Condotify/build_final.log 2>&1
echo "REAL_EXIT_CODE:$?" >> D:/repos/Condotify/build_final.log
```

Esperado: `REAL_EXIT_CODE:0` e `Compilação com êxito` no log — **nunca confiar apenas no exit code do wrapper bash, sempre checar o texto do log**, como aconteceu nesta sessão com falhas mascaradas por espaço em disco.

- [ ] **Passo 2: verificar processo rodando e ausência de crash**

```bash
ADB="$LOCALAPPDATA/Android/Sdk/platform-tools/adb.exe"
"$ADB" shell ps | grep -i condotify
"$ADB" logcat -d --pid=<PID> | grep -iE "UNHANDLED EXCEPTION|AndroidRuntime"
```

Esperado: processo presente, nenhuma linha de exceção não tratada.

- [ ] **Passo 3: capturar e revisar prints de todas as 13 telas + Login + menu lateral**

Para cada tela (Pessoas, Unidade de exemplo, Cadastro de uma pessoa de exemplo, Visitantes, Portaria, Câmeras, Acionamentos, Encomendas, Reservas, Alertas, Notificações, Meu perfil, Mais, e o rodapé do menu lateral em modo desktop se der para simular largura), como o dispositivo não aceita toque simulado: pedir ao usuário para navegar manualmente até cada tela e avisar, tirar o print via `adb shell screencap` + `adb pull`, revisar cada print especificamente por:
- Nenhum texto cru em inglês/PascalCase restante.
- Nenhum selo de confiança decorativo.
- Ícones com peso visual consistente entre si na mesma `.metric-grid` (comparar lado a lado, como foi feito para achar o ícone de "Reservas pendentes" desproporcional).
- Fotos carregando (não aparecendo ícone de imagem quebrada).

Qualquer problema encontrado nesta revisão que não estava previsto nas Tasks 1-4: corrigir inline nesta mesma tarefa (é o motivo de reservar uma passada de QA visual completa no final).

- [ ] **Passo 4: limpar arquivos de captura/log temporários**

```bash
rm -f D:/repos/Condotify/*.png D:/repos/Condotify/build_final.log
```

- [ ] **Passo 5: commit de qualquer ajuste feito no Passo 3 (se houver)**

```bash
git add -u
git commit -m "fix: visual QA pass on remaining mobile screens"
```

Se nenhum ajuste foi necessário, pular este passo (nada para commitar).
