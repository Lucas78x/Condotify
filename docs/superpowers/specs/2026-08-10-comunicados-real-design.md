# Comunicados (real) — Design

**Status:** aprovado pelo usuário, pronto para virar plano de implementação.

## Contexto

Um levantamento do código atual confirmou que "Comunicados" não existe como feature no Condotify hoje — nenhum controller, DTO, tabela, rota ou item de navegação dedicado. O único artefato relacionado é uma categoria de documento mal-nomeada (`ResourceDocumentCategoryEnum.Announcement`, rotulada "Comunicado" na UI) dentro do módulo genérico de Documentos — que exige upload de PDF, não tem título curto, não tem imagem, não tem confirmação de leitura e não tem segmentação por bloco/unidade. O spec original do produto (`contexto.txt`, seção 13) pede uma feature rica: destaques, urgência, confirmação de leitura, anexos/imagens/links, segmentação por bloco/unidade/perfil, histórico com busca.

Esta reforma constrói o **MVP** dessa feature — um board de avisos simples, todo-condomínio, sem os recursos mais caros do spec original (segmentação granular, confirmação de leitura, anexos), deixando-os para uma iteração futura.

**Fora de escopo** (avaliado e descartado nas perguntas de esclarecimento):
- Segmentação por bloco/unidade/perfil — todo comunicado é visível para todos os moradores ativos da licença nesta rodada.
- Confirmação de leitura por morador.
- Anexos, imagens, links como campos estruturados — corpo é texto simples.
- Diferenciar push por urgência — toda publicação dispara push, urgente ou não.
- Tela dedicada na barra de navegação inferior do mobile — entra no menu "Mais", mesmo lugar de Documentos hoje.

## Arquitetura

Três frentes sequenciadas (schema/backend precisa vir antes do frontend consumir):

1. **Modelo e permissões**: nova entidade `AnnouncementDTO` (`ILicenseScoped`), nova permissão `LicensePermissionEnum.ManageAnnouncements`, novo bit `LicenseModuleEnum.Announcements` (mesmo padrão aditivo já usado para `Documents`/`Deliveries` — ver `docs/superpowers/plans/2026-08-08-license-module-flags.md`), nova categoria `MobileNotificationCategory.Announcement`.
2. **Backend**: `AnnouncementsController` (CRUD para a equipe, license-scoped, permission-gated) + endpoint `api/resident/announcements` (leitura para o morador, mesmo padrão de `api/resident/deliveries`/`api/resident/documents`). Criação dispara push via `IPlatformPushNotifier.NotifyLicenseUsersAsync` com a nova categoria. Exclusão usa `IRecycleBinService` (soft-delete, mesmo padrão já usado para outras entidades no sistema).
3. **Frontend**: aba nova "Comunicados" em `LicenseWorkspace.razor` (portal, equipe) + página nova `Comunicados.razor` (mobile, morador) com entrada no menu "Mais".

## Modelo e permissões

`AnnouncementDTO` (nova, `CondotifyAPI.Domain/DTO/Announcements/AnnouncementDTO.cs`, implementa `ILicenseScoped`):
- `Id`, `LicenseId`, `Title` (string, obrigatório), `Body` (string, obrigatório, texto simples), `IsUrgent` (bool, default false), `CreatedBy` (string, nome/identificador de quem publicou), `CreatedAt`, `UpdatedAt`.

Nova permissão `ManageAnnouncements` em `LicensePermissionEnum` (bit novo, mesmo padrão de `ManageDeliveries`/`ManageDocuments`) — controla criar/editar/apagar pelo lado da equipe. Não há permissão de "visualizar" do lado do morador — qualquer morador ativo da licença vê todos os comunicados dela, sem checagem granular adicional (mesma postura que Documentos/Encomendas hoje têm para o morador).

Novo bit `Announcements` em `LicenseModuleEnum` (`CondotifyAPI.Domain.Enums.License` + espelho em `Condotify.Contracts`) — controla se a aba/tela aparece, mesmo padrão aditivo de todo módulo já adicionado.

## Backend

`AnnouncementsController` (`api/access/licenses/{licenseId}/announcements`, equipe):
- `GET` — lista todos os comunicados da licença, mais recentes primeiro, sem paginação nesta rodada (mesmo padrão simples de `DeliveriesModule`/`IncidentsModule` hoje — volume esperado é baixo).
- `POST` — cria, valida `Title`/`Body` não vazios, dispara `IPlatformPushNotifier.NotifyLicenseUsersAsync(licenseId, MobileNotificationCategory.Announcement, title, body-truncado, deep-link, idempotency-key)` para todo morador ativo da licença (mesmo fan-out que `ResourceDocumentsController.cs:83-96` já faz para Documentos).
- `PUT /{id}` — edita `Title`/`Body`/`IsUrgent`, **não** reenvia push.
- `DELETE /{id}` — soft-delete via `IRecycleBinService` (captura antes de remover, mesmo padrão de outras entidades no sistema), some da lista do morador imediatamente.

Endpoint do morador `api/resident/announcements` (`GET` apenas) — resolve a licença do morador autenticado (mesmo padrão de resolução já usado em `api/resident/deliveries`/`api/resident/documents`) e retorna a lista de comunicados daquela licença, mais recentes primeiro, urgentes destacados no payload (`IsUrgent` já é suficiente — a UI decide como destacar).

## Frontend

**Portal** (`Condotify/Components/LicenseModules/AnnouncementsModule.razor`, nova aba em `LicenseWorkspace.razor`, grupo "Operação"): lista em tabela (título, autor, data, badge de urgente), botão "Publicar" abre diálogo (título, corpo, checkbox "Marcar como urgente"), ações de editar/apagar por linha — mesmo padrão visual e de confirmação já usado em `DeliveriesModule.razor`/`IncidentsModule.razor`.

**Mobile** (`Condotify.Mobile/Components/Pages/Comunicados.razor`, nova): lista de cards (comunicados urgentes com destaque visual — cor/ícone diferente, mesmo padrão já usado para "Negados"/alertas em outras telas), pull-to-refresh (mesmo padrão já adotado em Visitors/Deliveries/Bookings/Notifications nesta sessão). Entrada de navegação: `Condotify.Mobile/Components/Pages/More.razor` (branch do morador), nova linha "Comunicados" ao lado de "Documentos", gated por `LicenseModuleEnum.Announcements` — mesmo padrão das outras linhas dessa tela.

## Tratamento de erros

- `POST`/`PUT` com título ou corpo vazio: `400 BadRequest`, mesma convenção de mensagem de erro já usada nos outros controllers deste projeto (`{ Errors = "..." }`).
- Falha ao enviar push após criar o comunicado: não deve reverter a criação — o comunicado já existe e aparece na lista do morador no próximo carregamento/pull-to-refresh, mesma postura de resiliência que `ResourceDocumentsController` já tem (o push é best-effort, a leitura por polling/pull é o caminho garantido).
- `DELETE` de um comunicado já apagado: `404 NotFound`, mesmo padrão do resto do sistema.

## Testes

- Backend: testes de integração para o CRUD (criação, edição, exclusão via recycle bin, isolamento de tenant — comunicado de uma licença não pode aparecer para morador de outra), teste de que a criação dispara o push com a categoria correta.
- Frontend: sem testes de UI automatizados (bUnit não é usado neste projeto) — verificação manual.

## Decisões YAGNI (explicitamente fora de escopo)

- Sem segmentação por bloco/unidade/perfil, sem confirmação de leitura, sem anexos/imagens/links estruturados, sem diferenciação de push por urgência, sem paginação (volume baixo esperado), sem tela dedicada na navegação principal do mobile — ver seção "Fora de escopo" acima.
