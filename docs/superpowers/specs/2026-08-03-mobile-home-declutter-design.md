# Início sem redundância de navegação

Data: 2026-08-03

## Objetivo

Depois do polish de "shell" (spec `2026-08-03-mobile-shell-polish-design.md`), a tela de Início continuava "feia" na visão do usuário — não por causa dos cantos/sombras (já refinados), mas por excesso de conteúdo: a seção "Acesso rápido" (6 a 8 blocos) repete quase inteiramente os caminhos que já existem na barra de navegação inferior e na página "Mais". Este documento remove essa redundância, decidido com o usuário via mockup.

Escopo: só `Condotify.Mobile/Components/Pages/Home.razor` e `Condotify.Mobile/Components/Pages/More.razor`, mais o CSS novo necessário em `Condotify.Mobile/wwwroot/css/app.css`. Não altera as outras 13 páginas nem os componentes compartilhados já refinados (`PageHeader`, `PageState`, `.content-panel`, `.metric-grid`).

## Levantamento da redundância

**Equipe** (barra inferior: Início, Portaria, Câmeras, Mais): dos 8 blocos de "Acesso rápido" (Pessoas, Visitantes, Portaria, Acionamentos, Câmeras, Alertas, Encomendas, Reservas) — Portaria e Câmeras já estão na barra inferior; Pessoas, Visitantes, Acionamentos, Alertas e Encomendas já estão na página "Mais". Só **Reservas** não existe em nenhum outro caminho.

**Morador** (barra inferior: Início, Visitantes, Reservas, Mais): dos 6 blocos (Visitantes, Reservas, Encomendas, Câmeras, Notificações, Meu cadastro) — Visitantes e Reservas já estão na barra inferior; Encomendas, Câmeras, Notificações e Meu cadastro já estão em "Mais". **Nenhum** item fica sem caminho alternativo.

## Decisão

- Remover a seção "Acesso rápido" (título + grade) inteira do `Home.razor`, para os dois perfis.
- Adicionar "Reservas" à lista de `More.razor` (só no ramo Equipe — no ramo Morador já não existe essa lacuna).
- Adicionar um botão de ação flutuante (FAB) "+" no canto inferior direito do Início, acima da barra de navegação, levando para `/visitors` (registrar/consultar visitante) — ação de criação rápida coexistindo com a aba "Visitantes" da navegação, mesmo padrão de apps que têm aba de listagem + atalho de criação (ex.: caixa de entrada + botão de novo e-mail). Mesmo destino para os dois perfis.
- Simplificar o cabeçalho da página: título passa a ser o nome do condomínio/contexto (o que já aparecia como subtítulo repetido também na barra do app), com "Olá, {nome}" como subtítulo — elimina a repetição do nome do condomínio aparecendo duas vezes na tela (uma na barra do app, outra na saudação).

O resultado, por perfil: barra do app + cabeçalho simplificado + métricas (inalteradas) + um painel de conteúdo (`Minhas unidades` para morador, `Atividade recente` para equipe) + FAB — a mesma composição do mockup aprovado.

## Testes

Sem infraestrutura de teste de UI automatizado (mesma situação dos polimentos anteriores). Verificação: build + instalação real no dispositivo Android conectado, print comparado ao mockup aprovado, para os dois perfis (equipe e morador).

## Fora de escopo

- As outras 13 páginas do app — já herdaram o refino visual compartilhado do polish anterior (`PageHeader`, `PageState`, `.content-panel`, `.metric-grid`) e não têm o problema específico de redundância de navegação que motivou esta mudança. Se o usuário apontar uma página específica que ainda pareça "cheia", ela entra numa spec própria.
- Mudar o comportamento da página `/visitors` em si (o FAB só navega até ela).
