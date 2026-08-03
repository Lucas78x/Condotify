# SP-4 - App MAUI Blazor Hybrid

Data: 2026-08-01

## Objetivo

Entregar um aplicativo Android, iOS, macOS e Windows para equipe operacional e moradores, consumindo exclusivamente a API existente. O aplicativo deve aproveitar `Condotify.Contracts`, `Condotify.ApiClient` e `Condotify.UI`, sem duplicar regras de autorizacao do servidor.

## Arquitetura

- `Condotify.Mobile.Core`: estado de sessao, autenticacao, renovacao, roteamento seguro de deep links e regras de navegacao testaveis em `net9.0`.
- `Condotify.Mobile`: host .NET MAUI Blazor Hybrid, SecureStorage, conectividade, ciclo de vida, push nativo e interface MudBlazor.
- `Condotify.Mobile.Tests`: testes do nucleo, sem emulador e sem rede externa.
- `Condotify.ApiClient`: cliente HTTP tipado compartilhado; o token vem do contexto de sessao mobile.
- `Condotify.UI`: tema visual compartilhado, incluindo modo claro e escuro.

## Sessao e seguranca

- Login permite escolher Equipe ou Morador e nunca tenta um perfil implicitamente depois da falha do outro.
- Access token, refresh token, perfil e expiracao ficam no SecureStorage; senha nunca e persistida.
- Renovacao e serializada para evitar duas rotacoes concorrentes do mesmo refresh token.
- Logout remoto e local e idempotente; falha de rede nao impede a limpeza local.
- O app nao interpreta permissoes como autoridade. Respostas 401 encerram/renovam sessao e 403 sao exibidas sem contornar a API.
- Deep links passam pela allowlist de `MobileDeepLinks` antes de navegar.

## Experiencia

### Comum

- Tela de entrada simples, objetiva e acessivel.
- Shell adaptativo: navegacao inferior em telefone e trilho lateral em larguras maiores.
- Inbox com contador, preferencias e marcacao de leitura.
- Estados de carregamento, vazio, offline e erro com acao de tentar novamente.
- Tema claro/escuro seguindo o sistema, com opcao manual.

### Equipe operacional

- Selecao de condominio e resumo operacional.
- Visitantes e aprovacoes, portaria, encomendas, reservas e alertas.
- Equipamentos com acionamento protegido por confirmacao e chave idempotente.
- CFTV com snapshot, player HLS/WebRTC fornecido pelo gateway e fallback offline.

### Morador

- Inicio com unidade, acessos recentes e atalhos.
- Perfil e unidades vinculadas, revogadas dinamicamente pela API.
- Visitantes, reservas, encomendas e notificacoes limitados ao proprio escopo.
- Nenhum menu administrativo e exibido para o principal residente.

## Push e links

- Instalacao registrada apos login e atualizada quando o token do provedor muda.
- Instalacao desativada no logout.
- Toque em notificacao abre somente rotas permitidas.
- Android App Links e Apple Universal Links usam `https://app.condotify.com.br/app/*`.
- A entrega externa depende das credenciais reais FCM/APNs e dos arquivos de associacao publicados no dominio.

## Qualidade

- Testes unitarios para sessao, renovacao, isolamento de perfis e deep links.
- Build do nucleo, testes, build Android e build Windows quando o ambiente permitir.
- Nenhum servidor ou emulador permanece aberto apos a verificacao.

