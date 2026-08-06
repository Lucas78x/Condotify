# Convite de cadastro — layout compacto no mobile

## Problema

Em telas de mobile (≤640px), a página pública `/cadastro/convite/{token}`
(`Condotify/Components/Pages/RegistrationInvite.razor`) mostra o formulário de
cadastro só depois de quase uma tela inteira de conteúdo decorativo: cabeçalho,
texto de boas-vindas, a animação do "celular dentro do celular" com os 3
passos, e — redundante — a mesma lista de 3 passos repetida logo abaixo em
formato de lista simples (`<ol class="invite-guide-steps">`).

Confirmado visualmente com screenshot real em viewport 390×844 (iPhone-ish):
o formulário só aparece após ~890px de rolagem numa tela de 844px de altura.

## Decisão

- A animação do celular (`.invite-phone-demo`) explica bem os passos e deve
  ser mantida — validado com o usuário.
- A lista simples repetida (`.invite-guide-steps`) é redundante no mobile e
  deve desaparecer só nessa largura.
- **Desktop e tablet (>640px) não mudam em nada** — nenhuma alteração fora do
  bloco `@media (max-width: 640px)` já existente em `design-system.css`.
- Nenhuma mudança de texto, lógica ou markup Razor é necessária: todo o ganho
  vem de CSS (esconder um elemento existente + apertar espaçamentos de
  elementos existentes).

## Escopo

Arquivo único: `Condotify/wwwroot/css/design-system.css`, dentro do bloco
`@media (max-width: 640px) { ... }` que já existe para `.invite-public-layout`
(linhas ~1518-1531 antes desta mudança).

1. `.invite-guide-steps { display: none; }` — some só no mobile; o elemento
   continua no DOM (nenhuma mudança em `RegistrationInvite.razor`) e continua
   visível acima de 640px.
2. Reduzir padding/gap de `.invite-guide` (o cartão azul) e dos elementos
   internos de `.invite-phone-demo` (notificação, track, steps, nota de
   segurança) nesse mesmo breakpoint, para compactar a animação sem alterar
   sua estrutura ou conteúdo.
3. Reduzir padding/margin de `.invite-guide-expiry` (nota de validade) no
   mesmo breakpoint.

## Validação

- Recapturar screenshot real (Playwright, 390×844) da mesma URL de convite
  usada no diagnóstico e confirmar visualmente que o formulário fica
  visível bem mais cedo, sem cortar nenhum conteúdo do cartão azul.
- Conferir visualmente (ou via diff de screenshot) que nada mudou acima de
  640px.

## Fora de escopo

- Qualquer mudança de texto ou copy.
- Qualquer mudança na versão desktop/tablet.
- Qualquer mudança de lógica em `RegistrationInvite.razor` ou no back-end.
