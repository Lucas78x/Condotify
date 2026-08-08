# Visão geral do condomínio — redesign (portal web)

## Contexto

Disparado por um print real do portal (pós-redesign do menu de módulos e de
Documentos) mostrando a aba "Visão geral" de um condomínio: 4 cards de
estatística, uma tabela de estrutura com 1 linha, e quase metade da altura
da tela em branco depois disso. Aprovado via mockup interativo (artifact).

Diferente da spec anterior, aqui não existe estado vazio genérico nem
categoria/cor a resolver — o problema é puramente de **densidade e uso do
espaço**: a tela tem menos conteúdo visual do que a área disponível, e três
campos que a licença já carrega (`Type`, `State`, `CreatedAt` em
`LicenseFullViewModel`) nunca aparecem em nenhuma tela do portal.

**Fora de escopo**: qualquer mudança de API, DTO ou dado novo (atividade
recente, ocorrências, saúde de equipamentos) — `LicenseFullViewModel` hoje
só expõe contadores (`TotalBlocks`/`TotalUnits`/`TotalResidents`), datas e a
lista de blocos (`Blocks: List<BlockFrontViewModel>`); nada disso muda
nesta entrega. Um dashboard mais rico (com dados operacionais) fica para
uma spec futura separada, se o usuário quiser.

## Componente afetado

`Condotify/Components/LicenseModules/OverviewModule.razor` — hoje 27
linhas: grid de 4 `StatTile` + um `MudPaper.content-panel` com
`MudTable`/`MudAlert` para os blocos. `StatTile.razor` (componente
compartilhado) **não muda**.

## 1. Estrutura do condomínio: tabela → cards reaproveitados

`StructureModule.razor:70-80` já lista blocos exatamente como um grid de
cards clicáveis, usando classes já existentes em `portal.css:390-404`:
`.hierarchy-grid.group-card-grid` (grid responsivo,
`repeat(auto-fill, minmax(280px, 360px))`) contendo botões
`.hierarchy-card.group-card`, cada um com `.hierarchy-card-icon`,
`.hierarchy-card-main` (nome + rótulo), `.hierarchy-card-stats` (unidades +
pessoas) e `.hierarchy-card-action` (link "Acessar").

A Visão Geral passa a usar o mesmo padrão visual, mas **sem navegação** —
aqui é um resumo, não uma lista clicável para entrar no bloco (a navegação
por bloco já existe na aba Estrutura; duplicar esse fluxo aqui seria
redundante). Diferença de implementação: `<div>` em vez de `<button>`, e o
rótulo de ação muda de "Acessar" para "Ver estrutura" com link para a aba
Estrutura (`/licencas/{LicenseId}/estrutura`) em vez de navegação
por-bloco.

```razor
@if (License.Blocks.Count == 0)
{
    <div class="panel-body"><MudAlert Severity="Severity.Info" Variant="Variant.Outlined">A estrutura ainda não foi cadastrada. Comece criando o primeiro bloco.</MudAlert></div>
}
else
{
    <div class="panel-body">
        <div class="hierarchy-grid group-card-grid">
            @foreach (var block in License.Blocks)
            {
                <div class="hierarchy-card group-card">
                    <span class="hierarchy-card-icon"><MudIcon Icon="@Icons.Material.Outlined.Apartment" /></span>
                    <span class="hierarchy-card-main"><strong>@block.Name</strong><small>Bloco</small></span>
                    <span class="hierarchy-card-stats"><span><strong>@block.TotalUnits</strong>unidade@(block.TotalUnits == 1 ? "" : "s")</span><span><strong>@block.TotalResidents</strong>pessoas</span></span>
                    <a href="/licencas/@LicenseId/estrutura" class="hierarchy-card-action"><span>Ver estrutura</span><MudIcon Icon="@Icons.Material.Outlined.ArrowForward" /></a>
                </div>
            }
        </div>
    </div>
}
```

Note que o card inteiro não é clicável (é uma `<div>`, sem `@onclick`) — só
a ação "Ver estrutura" linka, e é um `<a>` puro (mesmo padrão de link
simples já usado em outros pontos do portal), não `MudButton`, para não
herdar estilo de botão dentro do card.

O `MudTable` e o `using` implícito de `MudTd`/`MudTh` que ele trazia saem
deste arquivo — nenhuma outra parte do componente os usa.

## 2. Coluna lateral nova: Detalhes da licença + Ações rápidas

Layout passa de 1 coluna para 2, usando uma grid nova (não existe hoje uma
grid de proporção 1.65fr/1fr no CSS do projeto — as mais próximas,
`.module-grid` e `.security-layout`, têm a coluna estreita **primeiro**,
ordem invertida da que faz sentido aqui):

```css
.overview-grid { display: grid; grid-template-columns: minmax(0, 1.65fr) minmax(280px, 1fr); gap: 16px; align-items: start; }
@media (max-width: 980px) { .overview-grid { grid-template-columns: 1fr; } }
```

(breakpoint 980px escolhido por ser o mesmo já usado em `portal.css:667`
para outros colapsos de layout de 2 colunas neste arquivo).

Coluna direita: dois `MudPaper.content-panel` empilhados (`display:flex;
flex-direction:column; gap:14px`), reaproveitando `panel-toolbar`/
`panel-title`/`panel-body` já existentes.

**Painel "Detalhes da licença"** — só os 3 campos de `LicenseFullViewModel`
que hoje não aparecem em nenhuma tela (`Type`, `State`, `CreatedAt`;
`Code`/`City`/`ExpireDate` já aparecem no `PageHeader` da página pai e não
são repetidos aqui):

```razor
<MudPaper Class="content-panel">
    <div class="panel-toolbar"><MudText Typo="Typo.subtitle1" Class="panel-title">Detalhes da licença</MudText></div>
    <div class="panel-body">
        <div class="info-row"><span>Tipo</span><strong>@License.Type</strong></div>
        <div class="info-row"><span>Cidade / UF</span><strong>@License.City / @License.State</strong></div>
        <div class="info-row"><span>Criada em</span><strong>@License.CreatedAt.ToString("dd/MM/yyyy")</strong></div>
    </div>
</MudPaper>
```

`License.Type` hoje é `string` cru vindo da API (ex.: `"Demo"`) — sem
tradução/label especial nesta entrega (mesmo tratamento que `License.Status`
já recebe no chip do cabeçalho da página, que também exibe o valor cru).

**Painel "Ações rápidas"** — 4 atalhos fixos, cada um um link simples para
uma aba que **já existe** no menu de módulos (não são ações novas, são
atalhos para as mesmas rotas que `LicenseWorkspace.razor` já define):

```razor
<MudPaper Class="content-panel">
    <div class="panel-toolbar"><MudText Typo="Typo.subtitle1" Class="panel-title">Ações rápidas</MudText></div>
    <div class="panel-body quick-actions">
        <a class="qa-item" href="/licencas/@LicenseId/estrutura"><MudIcon Icon="@Icons.Material.Outlined.AddHomeWork" />Novo bloco<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
        <a class="qa-item" href="/licencas/@LicenseId/estrutura"><MudIcon Icon="@Icons.Material.Outlined.Groups" />Ver moradores<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
        <a class="qa-item" href="/licencas/@LicenseId/credenciais"><MudIcon Icon="@Icons.Material.Outlined.Badge" />Configurar acessos<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
        <a class="qa-item" href="/licencas/@LicenseId/documentos"><MudIcon Icon="@Icons.Material.Outlined.Description" />Ver documentos<MudIcon Icon="@Icons.Material.Outlined.ChevronRight" Class="qa-arrow" /></a>
    </div>
</MudPaper>
```

`OverviewModule` hoje recebe só `[Parameter] License` — precisa ganhar um
segundo parâmetro `[Parameter, EditorRequired] public Guid LicenseId { get; set; }`
(os links de "Ações rápidas" precisam do id da licença para montar a rota;
`License` não carrega o id de volta em forma utilizável para URL de forma
mais direta que isso). `LicenseWorkspace.razor` já tem `LicenseId` em mãos
no `@switch` que instancia `OverviewModule` — só passar `LicenseId="LicenseId"`
junto de `License="_license"`.

Note que "Novo bloco" e "Ver moradores" apontam para a mesma rota
(`/estrutura`) — não existe hoje uma rota separada para "moradores" fora da
árvore de Estrutura (confirmado em `SectionAllowed`/`DefaultSection` de
`LicenseWorkspace.razor`, que não têm uma seção "moradores"). Isso é
esperado, não um bug: os dois atalhos levam ao mesmo lugar por hoje ser o
único caminho para ambas as ações; ficam como 2 itens porque comunicam
intenções diferentes ao usuário, mesmo compartilhando destino.

### CSS novo (`portal.css`)

```css
.info-row { display: flex; align-items: center; justify-content: space-between; padding: 10px 0; border-bottom: 1px solid #eef1f5; font-size: 12.5px; }
.info-row:last-child { border-bottom: none; }
.info-row span { color: #697386; }
.info-row strong { color: #202532; font-weight: 600; }
.quick-actions { display: flex; flex-direction: column; gap: 2px; }
.qa-item { display: flex; align-items: center; gap: 11px; padding: 11px 4px; border-radius: 7px; font-size: 12.5px; font-weight: 600; color: #3f4a5c; text-decoration: none; }
.qa-item:hover { background: #f8fafc; }
.qa-item .mud-icon-root:first-child { color: #3156d3; font-size: 1.1rem; }
.qa-arrow { margin-left: auto; color: #8a94a5; font-size: 1rem; }
```

(cores em hex direto, não `var(--ds-*)`, porque `portal.css` já mistura os
dois estilos — `.info-row`/`.stat-hint`/etc. vizinhos também usam hex cru;
seguindo a convenção local do arquivo, não a de `design-system.css`.)

## Casos de borda

- Licença sem nenhum bloco cadastrado: mantém o `MudAlert` de "estrutura
  ainda não cadastrada" que já existe hoje — só a ramificação com blocos
  vira cards, a vazia não muda.
- Licença com muitos blocos (ex. 20+): `group-card-grid` já é
  `repeat(auto-fill, minmax(280px, 360px))` — mesmo comportamento de quebra
  de linha que `StructureModule.razor` já usa em produção para o mesmo
  cenário, nada novo a testar aqui.
- Viewport estreito (< 980px): `.overview-grid` colapsa para 1 coluna,
  ordem no DOM já é estrutura-primeiro/lateral-depois, então o colapso não
  precisa de `order` CSS — a leitura em mobile já fica: stats → estrutura →
  detalhes → ações, ordem que já faz sentido.

## Testes

Mesma categoria da spec anterior — presentation-only, sem lógica de
negócio nova (o único "cálculo" é o plural de "unidade/unidades", trivial).
Verificação manual: licença sem blocos (alerta inalterado), licença com 1
bloco (card único, sem esticar estranho), licença com vários blocos (grid
quebra linha certo), os 4 links de "Ações rápidas" batem com as rotas
corretas, viewport estreito colapsa pra 1 coluna.
