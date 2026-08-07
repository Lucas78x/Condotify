# Área de Boletos (upload em massa com identificação automática por unidade)

## Contexto

Hoje a plataforma não tem nenhuma área financeira: não existe tabela,
controller, storage ou tela de boletos. Administradoras condominiais na
Bahia (ex: Exatta Gestão) tipicamente entregam ao síndico um único PDF com
uma cobrança por unidade (ex: 176 páginas para 176 unidades), uma por mês.
Hoje isso é distribuído manualmente (e-mail, grupo, impressão) — o objetivo
desta feature é permitir que o síndico/administração suba esse PDF único
uma vez e a plataforma identifique e organize automaticamente qual página
pertence a qual unidade, publicando tudo de uma vez para os moradores
corretos.

Módulo novo, autocontido — não modifica LPR, CFTV, Wallet ou qualquer
subsistema existente.

**Fora de escopo nesta entrega** (deliberadamente adiado):
- Extração de valor/vencimento por página (é informado uma vez, manualmente,
  para o lote inteiro).
- Qualquer integração de pagamento (Pix, gateway, baixa automática).
- Dashboards financeiros/relatórios ("Financeiro" como módulo mais amplo).
- Upload/revisão pelo app mobile — só pelo portal web (visualização do
  morador é web + mobile).

## Modelo de dados

**`BoletoBatch`** — o lote (uma "leva" de upload, tipicamente 1 por mês):

- `Id`, `LicenseId`
- `Reference` (string livre, ex.: `"Agosto/2026"`)
- `DueDate` (vencimento do lote, informado manualmente no upload)
- `UploadedByUserId`, `UploadedByName`
- `Status`: `Processing` → `PendingReview` → `Published` | `Cancelled`
- `SourceFileName`, `TotalPages`
- `CreatedAt`, `PublishedAt` (nulo até publicar)

**`BoletoDocument`** — cada página já separada do PDF original, uma por
unidade (ou pendente de vínculo):

- `Id`, `BatchId`
- `UnitId` (nulo até confirmado/publicado)
- `PageNumber` (posição no PDF original, 1-based — usado só para exibição
  na revisão, não é chave de negócio)
- `MatchMethod`: `Cpf` | `UnitText` | `Manual` | `Unmatched`
- `Ignored` (bool — marcado pelo síndico na revisão para páginas de capa/
  lixo que nunca devem virar boleto)
- `StorageReference` (arquivo PDF de 1 página, ver armazenamento abaixo)
- `ExtractedSnippet` (trecho de texto da página, guardado só para dar
  contexto visual ao síndico na tela de revisão — não é usado para nada
  depois de publicado)
- `CreatedAt`

Uma página com `Ignored = true` ou sem `UnitId` bloqueia a publicação do
lote (ver "Tela de revisão e publicação").

## Armazenamento do PDF

`PrivateMediaStore` (usado hoje para snapshots do LPR) só aceita imagem
(`image/jpeg|png|webp`) e limite de 5 MB — não serve para PDF sem alterar
sua validação de tipo, o que misturaria responsabilidades. Criar um serviço
irmão dedicado (`IBoletoDocumentStore` ou equivalente, a nomear na fase de
plano) seguindo o mesmo padrão de segurança já validado:

- Um arquivo por `BoletoDocument`, cifrado em repouso (AES-GCM, mesma
  abordagem do `PrivateMediaStore`), agrupado por `LicenseId` em disco.
- Cada arquivo é uma única página de PDF (pequeno, poucas dezenas/centenas
  de KB) — sem necessidade do limite de 5 MB pensado para imagem, mas cabe
  um teto de sanidade (ex.: 2 MB por página) para rejeitar arquivos
  corrompidos/anormais cedo.
- Download exige o mesmo tipo de checagem de posse já usada em outras
  áreas do app (`PrivateMediaController` para staff, endpoint próprio de
  morador com verificação de vínculo ativo com a unidade).

## Permissões

Dois bits novos em `LicensePermissionEnum` (staff): `ViewFinance` e
`ManageFinance`, seguindo o padrão já existente (`ViewX`/`ManageX` por
módulo). Como `Administrator` e `Manager` já herdam `LicensePermissionEnum.All`
em `LicenseAccessDefaults`, síndico/administração/gerente ganham acesso sem
nenhuma configuração adicional; `Concierge`/`Operator`/`Viewer` não recebem
por padrão. `All` precisa ser atualizado para incluir os bits novos (detalhe
de implementação, não de design).

Só quem tem `ManageFinance` sobe/revisa/publica/exclui boletos. `ViewFinance`
(sem `Manage`) só teria leitura do histórico de lotes já publicados — incluído
por consistência com o padrão do resto do app, mesmo que hoje nenhuma tela
o exija ainda.

## Fluxo de upload e matching automático

Tela nova no portal web (`Condotify/Components/LicenseModules/`, seguindo o
padrão de módulo por licença já usado por `StructureModule.razor` etc.),
visível a quem tem `ManageFinance`.

1. Síndico clica em "Novo lote", informa **Referência** e **Vencimento**, e
   sobe um único PDF.
2. API cria `BoletoBatch` (`Status = Processing`) e processa
   sincronamente (assíncrono/background só se o tamanho real do PDF exigir —
   decisão de implementação, não muda o contrato observável) página por
   página:
   - Extrai o texto da página via biblioteca de leitura de PDF gerenciada,
     sem dependência nativa (compatível com o ambiente Docker/Linux já em
     uso) — escolha exata de pacote fica para o plano.
   - Procura no texto um **CPF** (regex tolerante a pontuação/espaços,
     normalizado para 11 dígitos) e compara com os CPFs de
     `ResidentAccess` cadastrados nessa licença.
     - Exatamente **um** morador com aquele CPF → usa o vínculo de unidade
       principal (`ResidentUnitLink.IsPrimary`, ativo) dele.
     - **Mais de um** CPF válido e distinto encontrado na mesma página, ou
       CPF que bate com mais de um morador (não deveria acontecer, mas é
       tratado) → não confia no match automático, cai para o próximo
       critério ou fica `Unmatched`. Nunca escolhe "o primeiro que achar"
       quando há ambiguidade.
   - Se não achou CPF utilizável, tenta como reforço reconhecer padrões de
     texto tipo "Apto 101", "Unidade 101", "Bloco A" e casar contra
     `Block.Name` + `Unit.Number` cadastrados na licença (comparação
     tolerante a acentuação/caixa/espaços).
   - Sem match por nenhum critério → `MatchMethod = Unmatched`, `UnitId`
     nulo. A página é salva do mesmo jeito (vira `BoletoDocument`), só fica
     pendente de resolução manual na revisão.
3. Cada página vira um `BoletoDocument` (PDF de 1 página, já armazenado
   cifrado), e o lote passa para `Status = PendingReview`.

Nada disso é visível para moradores — é só preparação.

A lógica de extrair CPF/texto e decidir o match é uma função pura (recebe
texto da página + lista de moradores/unidades candidatos da licença,
devolve um resultado de match), isolada de I/O — ver "Testes".

## Tela de revisão e publicação

Depois do processamento, lista todas as páginas do lote:

- Cada linha: número da página, trecho de texto extraído (contexto visual),
  seletor de unidade, e o link para abrir/pré-visualizar aquela página
  isolada.
- Linhas com match automático já vêm com a unidade preenchida, marcadas
  como OK.
- Linhas `Unmatched` ficam destacadas, exigindo decisão manual: escolher a
  unidade certa, ou marcar "ignorar" (para páginas de capa/lixo do PDF
  exportado, que nunca devem virar boleto).
- **"Publicar lote"** só habilita quando toda página tiver unidade definida
  ou estiver marcada como ignorada. É uma ação única: todos os
  `BoletoDocument` do lote (não ignorados) ficam com `UnitId` confirmado,
  o lote vira `Status = Published`, `PublishedAt = now`, e dispara as
  notificações (ver abaixo) — tudo publicado de uma vez, não
  incrementalmente.
- Antes de publicar, o síndico pode **cancelar o lote inteiro**
  (`Status = Cancelled`): apaga todos os `BoletoDocument` e arquivos
  associados, sem deixar rastro visível para morador (nunca foi visível).
- Depois de publicado, um `BoletoDocument` individual pode ser **excluído**
  isoladamente (remove o acesso do morador àquele PDF específico) — para
  corrigir um engano pontual sem precisar cancelar/reemitir o lote inteiro.
  Não existe "substituir arquivo" nesta entrega: correção = excluir e, se
  necessário, subir um novo lote avulso só com a página certa.

### Explicativo animado de primeiro acesso

Enquanto a licença **nunca tiver tido nenhum `BoletoBatch` criado** (mesmo
que só criado e depois cancelado — o gatilho é a existência de qualquer
lote, não a publicação), a tela "Boletos" mostra, no lugar do estado vazio
padrão, um card animado explicando o fluxo em 3 passos — mesma linguagem
visual/técnica do `invite-phone-demo` já usado em
`RegistrationInvite.razor` (CSS puro, sem imagens, sem dependência de
biblioteca de animação):

1. **Envie o PDF** — o lote completo exportado da administradora
2. **Conferência automática** — sistema já casa cada página com a unidade
3. **Publique pros moradores** — um clique libera tudo de uma vez

Como deriva diretamente da existência de `BoletoBatch` no banco, não
precisa de nenhuma flag "já vi isso" salva à parte — funciona igual
independente de navegador/sessão. Assim que o primeiro lote é criado, some
para sempre e dá lugar à lista normal.

## Notificação ao morador

Ao publicar, para cada `BoletoDocument` publicado, enfileira uma notificação
via `IPushNotificationService.EnqueueAsync` (mesmo mecanismo já usado por
outras áreas do app) para o morador dono da unidade:

- `SubjectId` = `ResidentId`, `SubjectType` = `"resident"`
- Categoria nova `MobileNotificationCategory.Financial = 8` (as 7 categorias
  atuais — `Access`, `Visitor`, `Delivery`, `Booking`, `Security`,
  `Operational`, `System` — não cobrem este caso)
- Título/corpo: ex. `"Seu boleto de Agosto/2026 já está disponível"`
- `Route`: deep link para a tela "Boletos" do app
- `DeduplicationKey`: escopado por `BoletoDocument.Id`, para não duplicar
  em caso de retry

## Visão do morador (portal web + app mobile)

Tela nova "Boletos" (mobile: segue o padrão de `Cameras.razor` —
`PageHeader`, `PageState`, injeta `CondotifyApiClient`; web: página
equivalente na área do morador):

- Lista todos os `BoletoDocument` publicados das unidades em que o morador
  tem `ResidentUnitLink` ativo (`IsActive`, sem `EndsAt` vencido) naquela
  licença — se tiver mais de uma unidade, cada item mostra o rótulo da
  unidade (ex.: "Bloco A / 101").
- Ordenado do mais recente para o mais antigo (por `BoletoBatch.Reference`/
  `PublishedAt`).
- Cada item: referência, vencimento, ação de abrir/baixar o PDF.
- Download é autenticado, streaming do PDF descriptografado — mesma
  checagem de posse usada em `ResidentCftvController`/
  `ResidentProfileController` (compara o vínculo do morador autenticado com
  a unidade do documento, não por permissão — morador não tem permissão,
  tem posse).

## Casos de borda

- Vínculo de unidade encerrado (`EndsAt` no passado, ou `IsActive = false`)
  → o boleto daquela unidade some da lista do morador automaticamente (é
  filtro de query, não estado gravado no documento).
- Página de capa/lixo → marcada `Ignored` na revisão, nunca vira boleto
  visível para ninguém.
- Múltiplos CPFs candidatos ambíguos na mesma página → nunca escolhe por
  adivinhação, cai para revisão manual (ver "Fluxo de upload").
- Exclusão pós-publicação de um documento → some imediatamente da lista do
  morador; não afeta os demais documentos do lote.
- Histórico: morador enxerga lotes publicados de meses anteriores, não só
  o mais recente.

## Testes

- **Função de matching** (extrair CPF/texto → decidir unidade): pura,
  sem I/O, testável com xUnit real (não depende de banco nem de PDF de
  verdade — recebe texto já extraído). Casos: CPF com/sem pontuação, CPF
  ambíguo (múltiplos candidatos), texto de unidade em formatos variados
  ("Apto 101", "Unidade 101", "Ap. 101"), nenhum match, texto vazio.
- **Controllers e autorização** (upload, revisão, publicar, excluir,
  download do morador): segue o padrão já estabelecido no projeto — testes
  por reflexão de atributos/rotas, já que não há provider de banco
  disponível em teste (Postgres/Npgsql only, sem InMemory/Sqlite).
- **Notificação**: verificar que `EnqueueAsync` é chamado com
  `DeduplicationKey` correto por documento publicado (mock de
  `IPushNotificationService`, sem tocar Firebase de verdade).
