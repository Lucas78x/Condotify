# SP-4 - Plano de implementacao do app MAUI Blazor Hybrid

Data: 2026-08-01

Spec: [SP-4 Design](../specs/2026-08-01-sp4-maui-blazor-hybrid-design.md)

## Task 1: Estrutura e fundacao

Status: concluida.

- Criar `Condotify.Mobile.Core`, `Condotify.Mobile` e `Condotify.Mobile.Tests`.
- Referenciar contratos, cliente HTTP e tema compartilhados.
- Configurar DI, HttpClient, MudBlazor e configuracao de URL por plataforma.

## Task 2: Autenticacao e sessao

Status: concluida.

- Implementar cofre abstrato, SecureStorage, login de equipe/morador, MFA de equipe, refresh e logout.
- Implementar estado observavel de sessao e recuperacao no inicio.
- Testar persistencia, isolamento de perfil, renovacao unica e limpeza local.

## Task 3: Shell adaptativo e linguagem visual

Status: concluida.

- Criar layouts publico/autenticado, navegacao por perfil, selecao de licenca e tema.
- Criar componentes reutilizaveis para cabecalho, estados de pagina, metricas e listas.
- Garantir alvos de toque, safe areas, responsividade e ausencia de sobreposicoes.

## Task 4: Fluxos operacionais

Status: concluida.

- Implementar inicio, condominios, portaria/visitantes, equipamentos/acionamentos e CFTV.
- Implementar encomendas, reservas e alertas com comandos permitidos pela API.
- Exibir feedback de sucesso/erro e confirmacao em operacoes sensiveis.

## Task 5: Fluxos do morador

Status: concluida. Encomendas e CFTV usam endpoints residentes isolados; cameras exigem liberacao explicita e nao expoem dados de rede.

- Implementar perfil/unidades, inicio e navegacao com escopo residente.
- Implementar visitantes, reservas, encomendas e cameras apenas pelos endpoints resident-aware.
- Reagir a revogacao de vinculo sem exigir novo login.

## Task 6: Push, inbox e deep links

Status: concluida no codigo. A entrega externa depende dos arquivos Firebase/APNs e das identidades reais de assinatura documentadas em `docs/mobile-setup.md`.

- Registrar/desativar instalacao e preferencias.
- Implementar inbox, leitura, contador e roteamento por allowlist.
- Configurar intent filters/associated domains sem identidades ficticias.

## Task 7: Verificacao final

Status: concluida em 2026-08-01. Foram aprovados 475 testes da API, 34 do cliente compartilhado, 8 do nucleo mobile e 4 da web (521 no total). Builds Android e Windows terminaram com zero avisos; a migracao aditiva foi compilada e teve o SQL revisado; `docker compose config` foi validado com segredos efemeros. Nenhum servidor ou emulador permaneceu ativo. Build/assinatura iOS, entrega FCM/APNs e compatibilidade de snapshot/stream por modelo fisico dependem de credenciais, Mac e equipamentos externos.

- Executar testes do nucleo e suites existentes impactadas.
- Compilar Android e Windows; documentar limites de assinatura iOS/FCM/APNs.
- Confirmar que nenhum processo iniciado para verificacao ficou ativo.
