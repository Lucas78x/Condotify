# Agendamento de Áreas Comuns — Design

Data: 2026-07-18

## Contexto

O Condotify hoje não tem nenhum recurso de agendamento de áreas comuns (churrasqueira, piscina, salão de festas, quadra etc.). É uma funcionalidade greenfield. O padrão mais próximo a seguir no código existente é a feature `AccessRoutes` (`CondotifyAPI/Controllers/AccessRoutesController.cs`, `CondotifyAPI.Infrastructure/ContextConfiguration/AccessControl/AccessRouteConfiguration.cs`): um recurso preso a `LicenseId` (o condomínio/tenant), com controller fino falando direto com o `DatabaseContext`, DTOs manuais (sem AutoMapper), e autorização via `RequireLicensePermissionAttribute` + `HasLicenseAccessAsync`.

**Importante:** hoje não existe nenhum login/portal para o morador — só autenticação para o time interno (síndico/porteiro/admin), via cookie no projeto `Condotify` e JWT na `CondotifyAPI`. `ResidentAccess` existe apenas como credencial de controle de acesso físico (catraca/QR/facial), não como conta web. Por isso, esta v1 é **staff-only**: o porteiro/síndico registra o agendamento em nome de uma unidade, dentro do portal administrativo já existente. O modelo de dados guarda `ResidentId` desde já para que um futuro portal de morador (autenticação própria, fora de escopo aqui) possa plugar por cima sem precisar redesenhar o domínio.

## Escopo

Dentro do escopo:
- Síndico/admin cadastra os locais agendáveis por condomínio (License), cada um com suas próprias regras.
- Grade de horários (slots) configurável por dia da semana.
- Bloqueio pontual de datas (manutenção, feriado, obra).
- Aprovação configurável por local: automática ou manual (pendente até staff aprovar/recusar).
- Taxa informativa (sem cobrança online — apenas exibida e reconhecida no agendamento).
- Termo de uso opcional por local, aceito via checkbox e registrado com data/hora.
- Limite mensal de agendamentos por unidade, configurável por local (0/nulo = sem limite).
- Antecedência mínima e janela máxima futura para agendar, configuráveis por local.
- Cancelamento permitido até X horas antes do horário (configurável por local).

Fora do escopo (v1):
- Login/portal do morador (autenticação própria) — feature separada e maior, não coberta aqui.
- Pagamento online (gateway de pagamento) — taxa é apenas informativa.
- Assinatura formal de termo (é um checkbox de aceite, não assinatura digital/certificado).

## Modelo de dados

### `Amenity` (o local agendável)
| Campo | Tipo | Observação |
|---|---|---|
| `Id` | Guid | PK |
| `LicenseId` | Guid | FK → `Licenses`, cascade delete |
| `Name` | string | Ex: "Churrasqueira Gourmet" |
| `Description` | string? | |
| `Capacity` | int? | Capacidade de pessoas |
| `Active` | bool | Desativar sem apagar |
| `FeeAmount` | decimal? | Informativo, sem integração de pagamento |
| `FeeDescription` | string? | |
| `RequiresApproval` | bool | Confirma na hora ou fica `Pending` |
| `RequiresTermsAcceptance` | bool | |
| `TermsText` | string? | Texto exibido no checkbox de aceite |
| `MonthlyLimitPerUnit` | int? | 0/null = sem limite |
| `MinAdvanceNoticeHours` | int | Antecedência mínima para agendar |
| `MaxAdvanceDays` | int | Janela máxima no futuro |
| `CancellationCutoffHours` | int | Prazo limite para cancelar sem bloqueio |
| `CreatedAt`/`UpdatedAt` | DateTime | |

### `AmenityScheduleSlot` (grade semanal recorrente)
| Campo | Tipo | Observação |
|---|---|---|
| `Id` | Guid | PK |
| `AmenityId` | Guid | FK → `Amenity`, cascade delete |
| `DayOfWeek` | enum (0-6) | Domingo=0 ... Sábado=6 |
| `StartTime` | TimeSpan | |
| `EndTime` | TimeSpan | |
| `Active` | bool | |

Exemplo: Sáb/Dom → 3 slots (08–14h, 14–20h, 20–23h); Seg-Sex → 1 slot (18–22h).

### `AmenityBlackout` (bloqueio pontual)
| Campo | Tipo | Observação |
|---|---|---|
| `Id` | Guid | PK |
| `AmenityId` | Guid | FK → `Amenity`, cascade delete |
| `StartDate` | Date | |
| `EndDate` | Date | |
| `Reason` | string? | |

### `AmenityBooking` (o agendamento)
| Campo | Tipo | Observação |
|---|---|---|
| `Id` | Guid | PK |
| `AmenityId` | Guid | FK → `Amenity` |
| `LicenseId` | Guid | Denormalizado, facilita query multi-tenant |
| `UnitId` | Guid | FK → `Units` |
| `ResidentId` | Guid? | FK → `Resident`, nullable (staff pode não vincular a um morador específico) |
| `Date` | Date | |
| `SlotId` | Guid | FK → `AmenityScheduleSlot` |
| `Status` | enum | `Pending`, `Confirmed`, `Rejected`, `Cancelled`, `Completed` |
| `TermsAcceptedAt` | DateTime? | |
| `Notes` | string? | |
| `CreatedByUserId` | Guid | Staff que registrou |
| `CreatedAt` | DateTime | |
| `CancelledAt` | DateTime? | |
| `CancelReason` | string? | |

Índice único parcial (`AmenityId`, `SlotId`, `Date`) filtrado por `Status IN (Pending, Confirmed)` — impede dois agendamentos ativos no mesmo local/slot/data, mesmo sob concorrência.

## Backend

### Permissões
Novos flags em `LicensePermissionEnum` (`CondotifyAPI.Domain/Enums/License/LicenseAccessEnums.cs`), após o último flag existente (`ManageSettings = 1L << 16`):
```csharp
ViewBookings = 1L << 17,
ManageBookings = 1L << 18,
All = (1L << 19) - 1
```
- `Normalize`: `ManageBookings` implica `ViewBookings`.
- `ForRole`: `Concierge` e `Operator` ganham `ViewBookings` + `ManageBookings` (é o time que opera reservas no dia a dia); `Viewer`/default ganham só `ViewBookings`.

### Controllers
Seguem o padrão do `AccessRoutesController`: controller fino, `DatabaseContext` direto, DTOs manuais em um arquivo por área, transação em updates, `HasLicenseAccessAsync(licenseId)` em toda ação.

**`AmenitiesController`** — `api/access/licenses/{licenseId:guid}/amenities`
- `[RequireLicensePermission(ViewBookings)]` na classe; `ManageBookings` sobrescrito em POST/PUT/DELETE.
- CRUD do `Amenity`, com `AmenityScheduleSlot` e `AmenityBlackout` como sub-recursos salvos junto no mesmo payload (como `AccessRoutesController` faz com filhos de rota).

**`AmenityBookingsController`** — `api/access/licenses/{licenseId:guid}/amenities/{amenityId:guid}/bookings`
- `GET .../availability?date=` → calcula slots do dia (grade semanal menos blackout menos bookings `Pending`/`Confirmed`) e retorna disponibilidade.
- `POST` → cria o agendamento. Ordem de validação:
  1. Local `Active`.
  2. Data dentro da janela (`MinAdvanceNoticeHours` / `MaxAdvanceDays`).
  3. Data não coberta por `AmenityBlackout`.
  4. Slot existe para o dia da semana e está livre (índice único cobre a corrida).
  5. Limite mensal da unidade (`MonthlyLimitPerUnit`) não excedido.
  6. Termo aceito (`TermsAcceptedAt` preenchido) se `RequiresTermsAcceptance`.
  7. Grava com `Status = RequiresApproval ? Pending : Confirmed`.
- `PUT .../{id}/approve` e `.../{id}/reject` → exige `Status == Pending` e `ManageBookings`.
- `DELETE .../{id}` (cancelamento) → permitido apenas se `Now < Date - CancellationCutoffHours`; soft-delete (`Status = Cancelled`, mantém histórico).

## Frontend (Blazor + MudBlazor)

Novo módulo `Condotify/Components/LicenseModules/AgendamentoModule.razor`, registrado em `LicenseWorkspace.razor` (novo `case` no switch de navegação, gated por `ViewBookings`/`ManageBookings`) — coordenar com o trabalho de `AccessRoutes` em andamento nesse mesmo arquivo para evitar conflito de merge.

Duas abas:
- **"Locais"** (edição exige `ManageBookings`): cards com nome/capacidade/taxa/badge de aprovação automática ou manual. Botão "+ Novo Local" abre `AmenityFormDialog.razor` — nome, descrição, capacidade, taxa, aprovação, termo, limite mensal, antecedência, cancelamento, editor de grade semanal por dia da semana, e lista de bloqueios de data.
- **"Agendamentos"**: seletor de local + visão por data dos slots (livre / ocupado com unidade / pendente). Botão "Agendar" abre `AmenityBookingFormDialog.razor` — busca unidade/morador, mostra o slot escolhido, taxa informativa, checkbox de termo (se exigido), confirma. Agendamentos pendentes mostram ações "Aprovar"/"Recusar" (visíveis só com `ManageBookings`).

## Testes

- Testes de unidade para a lógica de disponibilidade e validação de regras (ordem de validação acima), similar ao padrão de `CondotifyAPI.Tests/LicensePermissionFilterTests.cs`: local inativo, fora da janela de antecedência, data bloqueada, slot ocupado, limite mensal excedido, termo não aceito.
- Teste de concorrência: duas criações simultâneas para o mesmo `AmenityId`/`SlotId`/`Date` — apenas uma deve suceder (índice único).
- Teste do fluxo de cancelamento respeitando `CancellationCutoffHours`.
- Teste de permissão: `ViewBookings` sem `ManageBookings` não pode aprovar/recusar/criar/editar local.
