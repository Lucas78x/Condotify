# Condotify Mobile — Roadmap e Decomposição

Data: 2026-07-31

## Contexto

Pedido original: um aplicativo .NET MAUI Blazor Hybrid completo, com MudBlazor, reaproveitando ao máximo a arquitetura existente, cobrindo moradores, porteiros, síndicos, administradores e demais perfis, com câmeras, acionamentos, agendamentos, visitantes, notificações push e mais de 17 módulos.

O diagnóstico da solution mostrou que a maior parte do esforço não está no app — está na API. Este documento registra o diagnóstico e a decomposição acordada.

## Diagnóstico da solução atual

### Estrutura

| Projeto | SDK | TFM | Papel |
|---|---|---|---|
| `Condotify` | `Web` | net8.0 | Blazor Server (InteractiveServer) + MudBlazor 9.7.0 + QRCoder |
| `CondotifyAPI` | `Web` | net8.0 | API REST, JWT, MediatR (parcial), EF Core |
| `CondotifyAPI.Domain` | `Library` | net8.0 | DTOs, Models, Enums, Interfaces |
| `CondotifyAPI.Infrastructure` | `Library` | net8.0 | DbContext, Migrations, Repositories, Mapping |
| `CondotifyAPI.Tests` | test | net8.0 | Testes |

Ambiente verificado: SDKs 8.0.402 e 9.0.306; workloads `maui 9.0.120`, `android`, `ios`, `maccatalyst` instaladas.

**Descoberta central:** `Condotify.csproj` não referencia `Domain` nem `Infrastructure`. A web já é consumidora HTTP pura da API — exatamente a arquitetura de que o mobile precisa. Todo o acoplamento passa por `Condotify/Services/CondotifyApiClient.cs` (1.184 linhas, ~130 métodos), com `ApiResult<T>` como resultado tipado.

### Inventário de funcionalidades existentes

| Módulo | Controller | Endpoints principais | Componentes Razor | Permissão exigida |
|---|---|---|---|---|
| Auth / MFA | `AuthController.cs:22` | `login`, `mfa/verify`, `mfa/setup\|enable\|disable`, `password/change`, `validate` | `Views/Login`, `Security.razor` | — |
| Licenças | `LicenseAccessController.cs:14` | `GET /licenses`, `by-user/{id}`, `{id}` | `Licenses.razor`, `LicenseLauncher.razor` | acesso à licença |
| Estrutura | `LicenseStructureController.cs:26` | `structure`, `blocks`, `units`, `residents` | `StructureModule.razor` | `ViewStructure` / `ManageStructure` |
| Pessoas / Veículos | `PeopleManagementController.cs:26` | `units/{id}/details`, `residents/{id}/profile`, `residents/{id}/vehicles`, `registration-invites` | `PersonProfileDialog`, `VehicleFormDialog` | `ViewPeople` / `ManagePeople` |
| Credenciais | `CredentialManagementController.cs:25` | `credentials`, `face-enrollment`, `renew`, `restore`, `devices/{id}/access-events` | `CredentialsModule.razor` | `ViewCredentials` / `ManageCredentials` |
| Dispositivos + acionamento | `LicenseStructureController.cs:320-520` | `devices`, `devices/{id}/test-connection`, `devices/{id}/open-door`, `devices/actions` | `DevicesModule.razor` | `ViewDevices` / `ManageDevices` / `OperateDevices` |
| Portaria / Visitantes | `ConciergeController.cs:23` | `concierge`, `visits`, `visits/{id}/approval`, `visits/{id}/status`, `watchlist` | `Concierge.razor` + 2 dialogs | `ViewEvents` / `ManagePeople` |
| Rotas de acesso | `AccessRoutesController.cs:18` | CRUD, `resolution/residents/{id}` | `AccessRoutesModule.razor` | `ViewStructure` / `ManageStructure` |
| Áreas comuns / Reservas | `AmenitiesController.cs`, `AmenityBookingsController.cs` | CRUD, `availability`, `approve`, `reject` | `AgendamentoModule.razor` | `ViewBookings` / `ManageBookings` |
| Encomendas | `LicenseStructureController.cs:552` | `deliveries`, `deliveries/{id}/status` | `DeliveriesModule.razor` | `ViewDeliveries` / `ManageDeliveries` |
| Alertas operacionais | `OperationalAlertsController.cs:16` | `alerts`, `summary`, `acknowledge`, `resolve`, `reopen`, `snooze` | `AlertIndicator`, `OperationalAlertCenter` | `ViewAlerts` / `ManageAlerts` |
| Dashboard operacional | `OperationsController.cs:16` | `dashboard`, `residents/search` | `Dashboard.razor`, `StatTile` | `ViewDashboard` |
| Câmeras (CFTV) | `CftvDeviceController.cs:13` | `by-license`, `test-connection` | `CamerasModule.razor` | `ViewDevices` / `ManageDevices` |
| Backups / Lixeira / Importações | 3 controllers | vários | 3 panels | `ViewBackups` / `ManageBackups` |

### Lacunas críticas

**Câmeras não têm streaming.** `ICFTVService` (`CondotifyAPI/Services/CFTV/ICFTVService.cs:8`) expõe um único método: `TestAsync`. `CamerasModule.razor` é CRUD de cadastro. Não existe HLS, WebRTC, MJPEG, snapshot nem proxy. Só RTSP direto, com usuário e senha guardados em `CFTVDevice`.

**Moradores não conseguem fazer login.** `AuthController.Login` autentica contra `_context.Users` (`UserAccess` = equipe). `ResidentAccess` tem campos `Email`/`Password` (`CondotifyAPI.Domain/Models/Resident/ResidentAccess.cs:9`) mas nenhum endpoint os usa. O modelo de permissão (`LicensePermissionEnum`) é por licença inteira; não há escopo por unidade.

**Não existe push.** `AlertNotificationChannelSender` suporta apenas `Email` e `Webhook`, servindo alertas operacionais à equipe. Zero ocorrências de Firebase/FCM/APNs na solution.

**Sessão inadequada para mobile.** JWT de 8h, sem refresh token, sem endpoint de logout, sem recuperação de senha. `LoginOut` devolve apenas `AccessToken`.

### Módulos inexistentes

Verificado por varredura em toda a solution: comunicados, enquetes, assembleias, documentos, boletos/financeiro, chat, achados e perdidos, classificados, botão de pânico, interfonia, controle de chaves, controle de mudanças, manutenções e assinaturas digitais **não existem**.

`TicketController` existe mas é create-only, protegido por API-Key, gera código de barras e não está ligado a nenhuma tela — não é um módulo de ocorrências.

### Dívidas técnicas relevantes

- **Permissões duplicadas:** `LicensePermissionEnum` (Domain) e `LicensePermission` (`Condotify/Models/LicenseManagementViewModels.cs`) são cópias manuais já divergentes — `LicensePermissionCatalog.Normalize` e `LicenseAccessDefaults.Normalize` tratam `OperateDevices` de formas diferentes.
- **Tema duplicado e divergente:** `MainLayout.razor:57` e `PublicLayout.razor` definem paletas separadas com valores diferentes (`Success` `#12805A` vs `#16845B`; raio `7px` vs `6px`). Não existe `PaletteDark`.
- **`open-door` sem idempotência:** `LicenseStructureController.cs:456` não aceita chave idempotente; a auditoria via `AddDeviceAudit` não registra IP nem origem da requisição.
- **Contratos hand-mirrored:** 118 tipos `*ViewModel` na web espelham manualmente 152 tipos `*In`/`*Out` da API.

## Decomposição

Cinco sub-projetos, cada um com spec, plano e implementação próprios.

| # | Sub-projeto | Natureza | Depende de |
|---|---|---|---|
| **SP-0** | Extração de contratos compartilhados | Refactor sem mudança de comportamento | — |
| **SP-1** | Gateway de mídia CFTV | Backend + infra | SP-0 |
| **SP-2** | Autenticação de morador, escopo por unidade e sessão mobile | Backend | SP-0 |
| **SP-3** | Push notifications e deep links | Backend + infra | SP-2 |
| **SP-4** | App MAUI Blazor Hybrid | Cliente | SP-0..SP-3 |

Ordem de execução acordada: **SP-0 → SP-1 → SP-2 → SP-3 → SP-4**.

SP-0 é o único que toca código existente da web. Os demais são aditivos: a versão web continua funcionando sem alteração.

### Consequência aceita da ordem escolhida

SP-1 é backend puro e não produz nada visível até o SP-4 existir. Sua validação usará um harness de teste (página HTML com player HLS/WebRTC apontando para o gateway) em vez de tela real. Isso foi apresentado e aceito.

### Decisões de escopo

1. **Público:** app atende equipe operacional **e** moradores. Como moradores não existem como contas hoje, o SP-2 é pré-requisito da experiência de morador.
2. **Câmeras:** SP-1 usa MediaMTX como plano de dados (RTSP → WebRTC/HLS) com a API do Condotify como plano de controle, mais proxy de snapshot para thumbnails e fallback offline.
3. **Módulos inexistentes:** fora de escopo. Cada um vira sub-projeto futuro próprio (backend + web + mobile). Criar qualquer um deles apenas no mobile fragmentaria a plataforma.

## Ativos reaproveitáveis identificados

- `CondotifyApiClient` (~130 métodos) e `ApiResult<T>` — reaproveitados integralmente via SP-0.
- `CFTVService` já resolve caminhos RTSP por fabricante (Intelbras/Dahua, Hikvision/Hilook, Uniview, Axis) e distingue stream principal de secundário (`subtype=0/1`, `Channels/101` vs `/102`). Reaproveitado pelo SP-1.
- `PrivateMediaStore` + `PrivateMediaController` + variável `CONDOTIFY_MEDIA_SECRET` (`docker-compose.yml:38`) — padrão de mídia privada assinada, estendido pelo SP-1 em vez de duplicado.
- `RequireLicensePermissionAttribute` + `LicenseAuthorizationService` — autorização server-side preservada e reutilizada por todos os sub-projetos.
- Tema MudBlazor de `MainLayout.razor:57` — consolidado pelo SP-0.

## Princípio de segurança transversal

Permissões continuam validadas no servidor em todos os sub-projetos. O app pode ocultar visualmente uma função, mas nunca é a autoridade sobre ela. Credenciais de câmeras e dispositivos jamais são enviadas ao cliente.
