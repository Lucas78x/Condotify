# Área de Documentos (repositório de documentos do condomínio)

## Contexto

Segundo item de uma lista maior que o usuário pediu ("Financeiro mais
amplo, segunda via, enquetes, assembleias, documentos, tudo que for
útil") — decompósto em sub-projetos independentes durante o brainstorming.
Segunda via já está coberta pelo módulo de Boletos (morador reabre/baixa
qualquer boleto publicado a qualquer momento). Financeiro amplo, Enquetes
e Assembleias ficam para specs futuras separadas.

Hoje não existe nenhum repositório de documentos no Condotify — atas,
regimento interno, convenção, comunicados e prestação de contas são
distribuídos fora da plataforma (e-mail, grupo, impressão). Módulo novo e
autocontido, seguindo os mesmos padrões já validados em Boletos: DTO EF em
`CondotifyAPI.Domain/DTO/<Área>`, storage criptografado dedicado a PDF,
permissão nova, módulo no portal web + tela no app mobile.

**Fora de escopo nesta entrega**:
- Múltiplos tipos de arquivo (só PDF — mesmo motivo do Boletos: cobre a
  esmagadora maioria dos documentos oficiais de condomínio e permite
  reaproveitar o padrão de armazenamento já existente).
- Versionamento (cada upload é um documento novo e independente; substituir
  um documento desatualizado é excluir o antigo e subir o novo).
- Visibilidade restrita por documento (tudo que é publicado aqui é visível
  a todos os moradores da licença — documento realmente interno/restrito
  simplesmente não é subido nesta área).
- Upload em lote (diferente de Boletos, aqui não há conceito de "vários de
  uma vez" — é sempre um documento por envio).

## Modelo de dados

Uma entidade só, sem lote e sem revisão (diferente de Boletos: aqui não há
casamento automático para revisar — o síndico já sabe exatamente qual
documento está subindo e para quem é).

**`ResourceDocumentDTO`**:
- `Id`, `LicenseId`
- `Category` (`ResourceDocumentCategoryEnum`: `Minutes` [Ata],
  `ByLaws` [Regimento Interno], `Covenant` [Convenção],
  `Announcement` [Comunicado], `FinancialStatement` [Prestação de Contas],
  `Other`)
- `Title` (texto livre, obrigatório)
- `Description` (texto livre, opcional)
- `StorageReference` (PDF cifrado — mesmo esquema AES-GCM do
  `BoletoDocumentStore`, novo serviço irmão dedicado a este módulo, mesmo
  teto de sanidade de 2 MB por arquivo)
- `UploadedByUserId`, `UploadedByName`
- `PublishedAt`, `CreatedAt`

Sem `Status` — o upload já publica na hora (contraste direto com Boletos,
onde existe um estado `PendingReview` antes de ficar visível). Não existe
"cancelar", só excluir.

## Permissões

Dois bits novos em `LicensePermissionEnum`: `ViewDocuments` = `1L << 33`,
`ManageDocuments` = `1L << 34` (os bits 31/32 já foram usados por
`ViewFinance`/`ManageFinance`). `All` passa a `(1L << 35) - 1`.
`Normalize()` ganha mais uma linha: `ManageDocuments` implica
`ViewDocuments`. Mesmo espelhamento em `Condotify.Contracts`
(`LicensePermission`, lembrando que o namespace real desse projeto é
`Condotify.Models`, não `Condotify.Contracts` — descoberto durante Boletos).
Administrator/Manager já ganham acesso automaticamente via `All`.

## Fluxo de upload (portal web, síndico/administração)

Tela nova "Documentos" (módulo por licença, mesmo padrão de
`BoletosModule.razor`): lista de documentos publicados, agrupados/filtráveis
por categoria, com botão "Novo documento" abrindo um dialog: categoria
(select), título, descrição opcional, upload do PDF (limite 2 MB, mesmo
teto do storage). Ao confirmar, o documento é criado e já fica visível na
hora — não há etapa de revisão nem publicação separada.

Exclusão: síndico remove um documento publicado a qualquer momento
(remove o arquivo cifrado e a linha do banco; some da lista do morador
imediatamente).

## Notificação ao morador

Diferente de Boletos (que notifica só os moradores da unidade dona do
documento), aqui o documento não pertence a uma unidade específica — ao
publicar, **todos os moradores com vínculo vigente em alguma unidade desta
licença** recebem um push (reaproveitando a mesma regra de vigência de
`ResidentAuthorizationService.LinkIsCurrentlyValid`, aplicada a
`ResidentUnitLinks` filtrado por licença, distinct por morador — não existe
hoje um helper pronto para "todos os moradores de uma licença": o
`IPlatformPushNotifier.NotifyLicenseUsersAsync` existente notifica a
**equipe** (`LicenseUserAccesses`), não moradores, então não serve aqui).

- Categoria: `MobileNotificationCategory.Operational` (já existente — não
  é criada uma categoria nova só para este módulo).
- Título/corpo: ex. `"Novo documento disponível: Ata da Assembleia de
  Julho/2026"`.
- `Route`: deep link para a tela "Documentos" do app (`/documentos`,
  adicionado a `MobileDeepLinks.StaticRoutes`).
- `DeduplicationKey`: escopado por `ResourceDocumentDTO.Id` + morador (o
  mesmo padrão usado no fan-out de `NotifyLicenseUsersAsync`, que já
  sufixa a chave com o id do destinatário para não colidir entre
  destinatários diferentes do mesmo evento).

## Visão do morador (só app mobile)

Mesma justificativa de sempre: não existe portal web de morador nesta
plataforma. Tela nova "Documentos", seguindo o padrão de `Boletos.razor`
(`PageHeader`, `PageState`, injeta `CondotifyApiClient`):

- Lista todos os documentos publicados da licença atual do morador,
  agrupados ou filtráveis por categoria, ordenados do mais recente para o
  mais antigo.
- Cada item: categoria, título, descrição (se houver), data de publicação,
  ação de abrir/baixar.
- Download reaproveita a correção já feita em Boletos: MAUI
  `Share.Default.RequestAsync` (folha de compartilhamento nativa), não
  JS/blob — o `window.open`/blob download já provou não funcionar no
  WebView do app.
- Autorização: só precisa confirmar que o morador tem uma sessão válida
  nesta licença (`ResidentAccessGrant.LicenseId`) — não depende de
  `UnitIds` como em Boletos, já que documento não é escopado por unidade.

## Casos de borda

- Morador sem nenhum vínculo vigente na licença → não é destinatário de
  notificação, mas ainda consegue ver a lista de documentos se acessar a
  tela (mesma lógica de "sessão válida na licença" — documento é
  informação pública do condomínio, não uma posse individual como boleto).
- Documento excluído → some da lista do morador na próxima carga; nenhuma
  notificação de remoção é enviada (fora de escopo).
- PDF corrompido/ilegível no upload → mesma validação já usada em Boletos
  (`IBoletoPdfProcessor`-equivalente aqui, ou reaproveitar o processor
  existente já que a validação é genérica de PDF, não específica de
  boleto — decisão de implementação, não de design).

## Testes

- Reflexão de atributos/rotas para os dois controllers (staff e morador),
  mesmo padrão já usado em todo o projeto (sem provider de banco em teste).
- Se a seleção de destinatários da notificação (moradores com vínculo
  vigente na licença, deduplicados) for extraída como função pura — mesmo
  padrão de `BoletosController.ResolveNotificationTargets` — ganha testes
  diretos sem banco, cobrindo: morador com vínculo vigente, morador com
  vínculo expirado (excluído), morador em outra licença (excluído),
  mesmo morador com vínculo em duas unidades da mesma licença (aparece uma
  única vez).
