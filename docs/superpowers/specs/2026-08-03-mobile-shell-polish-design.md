# Polish do "shell" do app mobile — loading, transições e telas gerais

Data: 2026-08-03

## Objetivo

O redesign do login (spec `2026-08-03-mobile-login-redesign-design.md`) deixou o resto do app mobile em contraste: a inicialização mostra uma tela quase branca com texto sem estilo, não há nenhuma transição entre telas, e as demais páginas (Início e as outras 14 que reusam os mesmos componentes compartilhados) ainda usam a linguagem visual antiga — bordas retas, cantos de 7px, esqueleto de carregamento pulsante. Este documento cobre três frentes, decididas com o usuário via mockups:

1. Tela de carregamento inicial (boot).
2. Transições de navegação entre páginas.
3. Refino visual dos componentes compartilhados (`PageHeader`, `PageState`/esqueleto, cartões de métrica, painel de conteúdo) para a mesma linguagem "cantos macios, respiro, sombra leve" do novo login — que se propaga automaticamente pras 15 páginas que os usam.

Escopo: só `Condotify.Mobile` (mesmo projeto do redesign do login). Não toca no tema compartilhado (`Condotify.UI/CondotifyTheme.cs`, usado também pelo app web) nem no conteúdo específico de cada página além dos componentes/classes CSS compartilhados listados abaixo.

## 1. Tela de carregamento inicial

Hoje: `wwwroot/index.html` tem `<div id="app">Carregando...</div>` sem nenhum estilo, herdando só o `body { background: #f3f5f8 }` global — um cinza quase branco que contrasta com o azul do splash nativo (`Resources/Splash/splash.svg`, cor `#3156D3`), criando um "pulo" de cor visível entre o splash do sistema operacional e o primeiro frame do Blazor.

Decisão: o placeholder de boot continua o azul do splash (gradiente igual ao hero do login, `#3156d3` → `#1c2f7a`), com a marca "C" e um spinner discreto por baixo. Como esse HTML é substituído assim que o componente raiz (`Routes`) monta, ele só precisa existir em `index.html` + CSS — não é um componente Blazor.

## 2. Transições de navegação

Hoje não existe nenhuma infraestrutura de transição — `Routes.razor` renderiza `<RouteView>` diretamente dentro de `<Found>`, sem wrapper, sem animação. Decisão: fade suave (~220ms, opacidade + leve deslocamento vertical), aplicado a toda navegação (login→início incluído, já que é só mais uma troca de rota). Implementação: envolver o `<RouteView>` num `<div class="page-transition" @key="...">`; o `@key` amarrado à URL força o Blazor a tratar o wrapper como um elemento novo a cada navegação, o que reinicia a animação CSS — mesmo truque de reciclagem de elemento que já é usado implicitamente pelo `@key` do Blazor, sem precisar de JS interop. Gated por `prefers-reduced-motion: no-preference`, mesmo padrão já usado em `login-enter`/`skeleton-pulse`.

## 3. Refino visual dos componentes compartilhados

Classes envolvidas (todas em `Condotify.Mobile/wwwroot/css/app.css`, nenhuma mudança de markup nos componentes Razor exceto a troca de animação do esqueleto):

- `.page-header` (usado por `PageHeader.razor`, 15/15 páginas): mais respiro, título com leve `letter-spacing` negativo como no login.
- `.metric-grid` / `.metric` (grade de métricas do Início e outras páginas de detalhe): de uma grade com bordas internas retas (7px) para cartões individuais com fundo cinza claro, cantos de 14px, sem bordas internas.
- `.content-panel` / `.panel-heading` / `.list-row` (painel de lista, usado por praticamente todas as páginas): cantos de 14px, sombra leve em vez de borda quadrada.
- `.action-grid` / `.action-tile` (atalhos rápidos do Início): cantos de 14px pra ficar consistente com os cartões de métrica ao lado.
- `.page-skeleton` / `.skeleton-metrics` / `.skeleton-panel` / `.skeleton-heading` / `.skeleton-row` (estado de carregamento do `PageState.razor`): troca a animação de pulso (opacidade subindo/descendo) por um efeito "shimmer" (gradiente varrendo da esquerda pra direita), acompanhando os novos cantos de 14px dos cartões/painéis reais para o esqueleto não "saltar" quando o conteúdo chega.

Fora de escopo: qualquer classe específica de uma página só (`.directory-*`, `.person-*`, `.detail-grid`, `.section-tabs`, etc.) e o tema compartilhado com o app web.

## Testes

Sem infraestrutura de teste de UI automatizado neste projeto (mesma situação do login). Verificação: build (`dotnet build Condotify.Mobile -f net9.0-android` e `-f net9.0-windows10.0.19041.0`) + instalação real no dispositivo Android já conectado, com prints de antes/depois comparados ao mockup aprovado.

## Fora de escopo

- Conteúdo específico de cada página além dos componentes/classes compartilhados acima.
- Tema compartilhado `Condotify.UI/CondotifyTheme.cs` (usado também pelo app web).
- Splash nativo do sistema operacional (`Resources/Splash/splash.svg`) — já está consistente com o novo boot, não precisa mudar.
