# Redesign da tela de login do app mobile + "Esqueci minha senha"

Data: 2026-08-03

## Objetivo

A tela de login atual (`Condotify.Mobile/Components/Pages/Login.razor`) foi construída como um layout de duas colunas para desktop (formulário + ilustração animada "mapa de acesso"), depois comprimida para telas de celular via media queries. O resultado é pesado e "achatado" no formato onde o app realmente roda a maior parte do tempo: um telefone. Este documento cobre (1) o redesign visual da tela para uma linguagem mais minimalista e (2) a adição do fluxo "Esqueci minha senha", que hoje não existe na UI apesar de o backend já suportá-lo para moradores.

Escopo: só o app mobile (`Condotify.Mobile` / `Condotify.Mobile.Core`). A tela de login web da equipe (`Condotify/Views/Login/Login.cshtml`) não é tocada.

## Direção visual

Layout escolhido: **hero de marca + folha** (opção B avaliada com o usuário via mockups).

- Tela de credenciais (estado inicial): cabeçalho cheio com gradiente na cor primária (`#3156D3` → `#1C2F7A`), marca "C", nome do app e tagline. Abaixo, uma folha branca com cantos arredondados sobrepondo o hero, contendo o alternador Equipe/Morador e o formulário.
- Todas as telas subsequentes (MFA, recuperação de senha, suporte) trocam o hero grande por uma barra de navegação compacta (~52px) com seta de voltar e título — repetir um hero de 190px em toda tela de formulário pesa visualmente e não agrega nada numa tela secundária.
- Sem a ilustração "mapa de acesso" (`.login-access-map` e as ~15 regras de `.access-*` associadas) — é o maior fator do "feio" relatado, e não sobrevive bem a telas estreitas.
- Card centralizado com `max-width` (~420px) em qualquer largura de janela, inclusive nos alvos desktop do MAUI (Windows/Mac Catalyst). Não recriamos um layout de duas colunas para janelas largas: o app é primariamente um app de celular, então a mesma composição centralizada serve para todos os tamanhos.
- Paleta e tipografia continuam vindas de `Condotify.UI.CondotifyTheme` (Inter, primário `#3156D3`, terciário `#007C69`). Sem modo escuro: `MainLayout.razor` já força `IsDarkMode="false"` hoje; não é alterado aqui.

## Estados da tela (máquina de estados única em `Login.razor`)

O componente já alterna entre "credenciais" e "MFA" internamente (campo `_mfaRequired`); o redesign estende o mesmo padrão em vez de introduzir novas rotas/páginas:

```
Credentials ──(Equipe + credenciais ok, MFA exigida)──> Mfa ──(voltar)──> Credentials
Credentials ──("Esqueci minha senha", principal = Morador)──> ForgotEmail
Credentials ──("Esqueci minha senha", principal = Equipe)───> StaffSupport
ForgotEmail ──(enviar)──> ForgotReset ──(voltar)──> ForgotEmail
ForgotReset ──(sucesso)──> ForgotDone ──("Ir para o login")──> Credentials
StaffSupport ──(voltar)──> Credentials
```

- **Credentials**: redesenhada, mas mesmo comportamento de hoje (alternador Equipe/Morador, e-mail, senha, botão Entrar). Adiciona o link "Esqueci minha senha" (visível só neste estado, roteando por `_principal` como acima).
- **Mfa**: mesmo fluxo atual, com o novo cabeçalho compacto no lugar do hero.
- **ForgotEmail** *(só Morador)*: campo de e-mail + botão "Enviar código". Como o backend (`POST /api/auth/resident/password/forgot`) sempre responde 202 independentemente de o e-mail existir (anti-enumeração, decisão já tomada na SP-2), a UI não espera um sinal de sucesso/falha distinto: qualquer resposta sem erro de rede avança direto para `ForgotReset`, com o texto deixando claro que o código só chega "se esse e-mail existir".
- **ForgotReset** *(só Morador)*: campo "Código de recuperação", "Nova senha", "Confirmar senha", botão "Redefinir senha". Chama `POST /api/auth/resident/password/reset`. Erros (`InvalidToken`, `InvalidPassword`) aparecem no mesmo `MudAlert` já usado em Credentials/Mfa.
- **ForgotDone**: confirmação estática ("Senha redefinida") com botão para voltar a Credentials.
- **StaffSupport** *(só Equipe)*: tela informativa, sem chamada de API. Texto explicando que contas de equipe não se autoatendem por segurança, mais dois cartões de contato (e-mail e telefone/WhatsApp). Valores de contato ficam como constantes no componente — `suporte@condotify.com.br` / `(11) 90000-0000` — placeholders fornecidos pelo usuário, fáceis de trocar depois; não há mecanismo de configuração dinâmica para isso hoje e criar um seria escopo além do pedido.

## Detalhe técnico: formato do código de recuperação

O token que `POST /password/forgot` gera (via `RefreshTokenService.GenerateTokenPair`) é uma string opaca de ~43 caracteres (base64url de 32 bytes aleatórios), não um código curto. Um design com "caixinhas" de dígito individual (como OTP de 6 dígitos) não é praticável para esse token. Em vez de mudar a lógica de segurança da SP-2 (fora de escopo aqui), o campo "Código de recuperação" em `ForgotReset` é um `MudTextField` normal (2 linhas, `word-break: anywhere` para o token não estourar a tela), preenchido por colar do e-mail — sem UI de caixinhas, sem botão de colar customizado (o menu nativo de colar do input já resolve).

## Mudanças de código

- **`Condotify.Mobile/Components/Pages/Login.razor`**: novo layout (hero/folha + cabeçalho compacto) e os quatro novos estados acima, como extensão do enum/campo de estado que já existe para MFA.
- **`Condotify.Mobile/wwwroot/css/app.css`**: substitui o bloco `.login-*` / `.access-*` (linhas ~146–280 hoje) pelas novas regras de hero, folha, cabeçalho compacto e cartões de contato. Remove o grid de duas colunas e as animações do mapa de acesso.
- **`Condotify.Mobile.Core/MobileSessionCoordinator.cs`**: dois métodos novos, seguindo o mesmo padrão de `LoginResidentAsync`/`VerifyStaffMfaAsync` (HTTP client nomeado `CondotifyAuth`, sem necessidade de sessão):
  - `ForgotPasswordAsync(string email)` → `POST api/auth/resident/password/forgot`.
  - `ResetPasswordAsync(string token, string newPassword)` → `POST api/auth/resident/password/reset`, mapeando `Result`/`Error` do corpo de resposta.
- Nenhuma mudança em `CondotifyAPI` (backend) ou em `Condotify.ApiClient` (esse cliente é usado para chamadas autenticadas pós-login; recuperação de senha é anônima e já segue o padrão específico de auth do `MobileSessionCoordinator`).

## Testes

- `Condotify.Mobile.Tests`: casos novos para `ForgotPasswordAsync` e `ResetPasswordAsync` em `MobileSessionCoordinatorTests.cs`, cobrindo sucesso, token inválido e falha de rede — mesmo estilo dos testes existentes de login/MFA.
- Sem testes de UI automatizados (o projeto não tem infraestrutura de teste de UI Blazor/MAUI hoje); validação visual manual do fluxo completo antes de finalizar.

## Fora de escopo

- Tela de login web da equipe (`Condotify/Views/Login/Login.cshtml`).
- Alterar o formato/tamanho do token de recuperação no backend.
- Modo escuro.
- Fluxo de "esqueci minha senha" para Equipe além do redirecionamento para suporte (sem alteração de senha via app).
