# Refinamento visual das telas restantes do Condotify Mobile

## Contexto

Login, Início (Home) e o shell compartilhado (barra superior, menu lateral
desktop, navegação inferior) já passaram por um redesenho visual completo em
sessões anteriores. Restam 13 telas que herdam as classes CSS compartilhadas
(`.content-panel`, `.list-row`, `.metric-grid`, `.row-status`) mas nunca
foram revisadas individualmente:

Pessoas, Unidade, Cadastro da pessoa, Visitantes, Portaria, Câmeras,
Acionamentos, Encomendas, Reservas, Alertas, Notificações, Meu perfil, Mais.

Durante a sessão anterior, ao corrigir bugs pontuais em Pessoas/Unidade
(foto do morador não carregava, rótulos de categoria/relacionamento vazando
o enum cru em inglês, ícone de "Reservas pendentes" desproporcional),
ficou claro que o mesmo padrão de bug provavelmente se repete em outras
telas que ainda não foram auditadas visualmente.

## Objetivo

Levar as 13 telas restantes ao mesmo nível de acabamento do Início/Login,
sem inventar uma linguagem visual nova — propagando os padrões que já
existem e funcionam.

## Padrões já estabelecidos (reaproveitar, não recriar)

- `.metric-grid` — faixa de números-resumo no topo da página (hoje em
  Início, Visitantes, Unidade).
- `.row-status` — selo colorido de status ("Ativo"/"Pendente"/etc, classes
  `success`/`warning`/`error`/`info`/`neutral`) (hoje em Visitantes,
  Unidade).
- `.content-panel` + `.list-row` — cards de lista (já usado na maioria das
  telas, mas de forma mais crua em Encomendas/Reservas/Alertas/Notificações,
  sem selo de status).
- `MudAvatar` com ícone colorido por status, como âncora visual de cada
  linha de lista.
- `PageHeader` + `PageState` (loading/empty/error) — já universal, não
  precisa mudar.

## Bugs a caçar sistematicamente em cada tela

Confirmados por leitura de código nesta sessão (ainda não corrigidos):

- **Status cru em inglês exibido ao usuário**: `Deliveries.razor` mostra
  `@row.Status` diretamente (`"Received"`, `"Delivered"`, `"Canceled"`);
  `Bookings.razor` mostra `@row.Status` diretamente (`"Pending"`,
  `"Confirmed"`, `"Rejected"`, `"Cancelled"`). Ambas devem ganhar um mapa de
  rótulo em português + `.row-status` colorido, no mesmo padrão de
  `UnitDetails.razor`/`Visitors.razor` (`StatusLabel`/`StatusClass`
  privados por página — é o padrão já usado no projeto, não criar um
  helper compartilhado novo).
- Qualquer outra foto (`<img src="...">`) apontando para uma referência
  `/private-media/{licenseId}/{mediaId}` sem passar pelo fluxo autenticado
  (`CondotifyApiClient.GetPersonPhotoAsync` + `TryParseMediaReference`,
  já implementado). Grep por `ImageUrl`/`PhotoUrl` em cada tela antes de
  mexer.
- Ícones de `.metric-icon`/`.action-tile` que pareçam menores ou
  desproporcionais aos vizinhos (comparar visualmente por print, não só
  por código — foi assim que achamos o ícone de "Reservas pendentes").
- Faixas de resumo (`.metric-grid`) ausentes em telas onde fariam sentido:
  Encomendas (ex: pendentes/entregues), Reservas (ex: pendentes/
  confirmadas), Alertas, Notificações.

## Restrição obrigatória: nada de "selo de confiança" genérico

Removidos nesta sessão dois textos que soavam como enchimento típico de IA,
sem estar amarrados a nenhuma funcionalidade real:

- Rodapé do login: ícone de cadeado + "Conexão protegida".
- Rodapé do menu lateral: ícone de escudo + "Ambiente protegido" /
  "Sessão monitorada".

**Nenhuma tela nova ou refinada pode introduzir esse tipo de rótulo**
("100% seguro", "conexão criptografada", "ambiente monitorado", "dados
protegidos" etc.) a menos que exista uma feature real por trás do texto
(ex: "Sessão protegida até HH:mm" em `Cameras.razor` é válido porque mostra
o horário real de expiração da sessão de streaming). Selo decorativo sem
dado real por trás = não escrever.

O rodapé do menu lateral (`rail-footer`) foi substituído por um chip real:
avatar com iniciais + nome do usuário logado + papel (Morador/Operação),
clicável para abrir "Minha conta" — informação de verdade, no lugar que
antes tinha só decoração.

## Execução em lotes

Conforme preferência do usuário: agrupar as 13 telas em lotes por
semelhança estrutural, refinar um lote inteiro, buildar uma vez por lote,
e enviar prints de todas as telas do lote juntas para validação. Lotes
sugeridos (a confirmar no plano de implementação):

1. Listas simples sem abas: Encomendas, Reservas, Alertas, Notificações.
2. Telas com abas/detalhe: Pessoas, Unidade, Cadastro da pessoa (as duas
   últimas já parcialmente corrigidas — revisão fina apenas).
3. Operacionais: Portaria, Câmeras, Acionamentos.
4. Conta/navegação: Meu perfil, Mais, Visitantes (já com bom nível, ajuste
   fino).

## Fora de escopo

- Redesenho estrutural (mudar rotas, remover/adicionar funcionalidades).
- Novo componente de design system — usar o que já existe em `app.css`.
- Testes automatizados de UI (não há harness de screenshot automatizado no
  projeto; validação é manual via print no dispositivo físico).
