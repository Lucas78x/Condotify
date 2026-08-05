# Evolução visual completa do Condotify Mobile

Data: 2026-08-03

## Contexto

O aplicativo mobile já possui todos os fluxos principais, navegação por perfil,
componentes compartilhados e uma primeira camada de acabamento. A validação em
dispositivo mostrou, porém, uma experiência ainda próxima de um painel web
comprimido: excesso de cartões com o mesmo peso, atalhos repetidos, cabeçalho
dominante e pouca diferenciação entre informação, ação e navegação.

Esta evolução segue o mockup aprovado na conversa e preserva a arquitetura
MAUI Blazor Hybrid, MudBlazor, rotas, permissões e contratos existentes.

## Direção visual

- Azul `#3156D3` e marinho `#1C2F7A` concentram marca, contexto e ações
  principais.
- Superfícies operacionais usam fundo claro, raios entre 12 e 24 px, bordas
  discretas e sombra curta.
- A tela inicial deixa de repetir todos os módulos e passa a priorizar uma ação
  principal, informações do momento e atividade recente.
- Métricas continuam disponíveis nas páginas analíticas, mas viram uma faixa
  horizontal no celular para reduzir altura e preservar legibilidade.
- Listas importantes viram cartões tocáveis independentes no celular e mantêm
  painéis agrupados em telas maiores.
- A navegação inferior continua dinâmica por perfil, com seleção em formato de
  superfície arredondada e área de toque mínima adequada.
- Estados de carregamento, vazio e erro, formulários, abas e diálogos seguem o
  mesmo vocabulário visual.

## Nova marca

O símbolo aprovado combina:

- a letra `C`;
- uma fachada condominial mínima;
- uma abertura central que sugere acesso.

A marca é implementada como SVG vetorial e aplicada no shell, login, boot,
splash e ícone do aplicativo. O desenho evita escudos, cadeados e selos de
segurança genéricos.

## Aplicação por grupo de telas

1. Shell: barra superior, menu lateral, navegação inferior e conectividade.
2. Início: hero contextual, ação principal, destaques do momento e atividade.
3. Listas: Visitantes, Portaria, Encomendas, Reservas, Alertas, Notificações e
   seleção de condomínio.
4. Operação: Câmeras e Equipamentos, mantendo confirmações e auditoria.
5. Estrutura e detalhes: Pessoas, Unidade e Cadastro da pessoa.
6. Conta: Mais, Minha conta, preferências e encerramento de sessão.
7. Fluxos modais: autorização de visitante e nova reserva.

## Restrições

- Não alterar regras de negócio, endpoints, autorização ou navegação por perfil.
- Não remover ações existentes.
- Não introduzir mensagens de confiança sem dado real por trás.
- Não alterar o tema compartilhado do portal web; o refinamento específico fica
  no CSS e nos componentes de `Condotify.Mobile`.
- Respeitar `prefers-reduced-motion`, safe areas e larguras a partir de 320 px.

## Verificação

- Testes de `Condotify.Mobile.Tests`.
- Build Android e Windows quando os workloads MAUI do SDK ativo estiverem
  instalados.
- Validação visual em dispositivo dos perfis Equipe e Morador.
