# SP-3 — Push notifications e deep links

Data: 2026-08-01

## Objetivo

Entregar uma infraestrutura durável de notificações push para equipe e moradores, com registro seguro de instalações, preferências, entrega assíncrona, observabilidade e navegação por deep links que será consumida pelo aplicativo MAUI da SP-4.

## Decisões

### Transporte

O servidor usa **FCM HTTP v1** para Android e iOS. A autenticação ocorre no backend com service account/ADC e token OAuth curto; nenhuma credencial Firebase chega ao cliente. Sem configuração válida, o outbox permanece observável e reagendado, sem fingir entrega.

Referências primárias consultadas:

- Firebase HTTP v1: https://firebase.google.com/docs/cloud-messaging/send/v1-api
- Gestão e frescor de instalações: https://firebase.google.com/docs/cloud-messaging/manage-tokens
- Android App Links no MAUI: https://learn.microsoft.com/dotnet/maui/android/app-links
- Apple Universal Links no MAUI: https://learn.microsoft.com/dotnet/maui/macios/universal-links

### Instalações

Cada instalação é identificada por `(SubjectId, SubjectType, InstallationId)`. O token de entrega é cifrado em repouso e também possui hash SHA-256 único para detectar transferência/duplicação sem consultar texto claro. Reenviar o registro atualiza token, versão, idioma, fuso e `LastSeenAt`.

Uma instalação só pode pertencer a um sujeito por vez. Login em outra conta transfere a instalação de forma explícita; logout a desativa. Tokens inválidos ou sem atualização por 60 dias são desativados.

### Preferências

Categorias: `Access`, `Visitor`, `Delivery`, `Booking`, `Security`, `Operational` e `System`. Preferências são por sujeito, com defaults seguros. Alertas de segurança e sistema podem ser marcados como essenciais; o backend continua sendo autoridade.

### Outbox

Produtores gravam uma notificação lógica no mesmo banco da operação. Um worker:

1. reivindica a mensagem com lease;
2. resolve destinatários e preferências no momento do envio;
3. envia uma vez por instalação;
4. registra resultado individual;
5. aplica backoff exponencial e dead-letter;
6. desativa tokens definitivamente inválidos.

Uma chave de deduplicação única evita notificações duplicadas em retries de domínio.

### Deep links

Formato canônico: `https://app.condotify.com.br/app/{route}`. O payload também leva `route`, uma rota interna relativa e validada. Hosts externos, `javascript:`, caminhos absolutos arbitrários e traversal são recusados.

Rotas iniciais:

- `/home`
- `/visitors/{id}`
- `/deliveries/{id}`
- `/bookings/{id}`
- `/access/events/{id}`
- `/alerts/{id}`
- `/cameras/{id}`
- `/profile`

O domínio publica `/.well-known/assetlinks.json` e `/.well-known/apple-app-site-association` somente quando os identificadores/fingerprints forem configurados.

## API

- `PUT /api/mobile/installations/{installationId}` — cria/atualiza a instalação do sujeito autenticado.
- `DELETE /api/mobile/installations/{installationId}` — desativa a instalação do próprio sujeito.
- `GET /api/mobile/notification-preferences` — lista preferências efetivas.
- `PUT /api/mobile/notification-preferences` — atualiza preferências permitidas.
- `GET /api/mobile/notifications` — caixa de entrada paginada do próprio sujeito.
- `POST /api/mobile/notifications/{id}/read` — marca como lida.
- `POST /api/mobile/notifications/test` — somente desenvolvimento/admin, envia teste à própria instalação.

Todas as rotas aceitam equipe ou morador pela política nomeada aditiva da SP-2 e derivam `SubjectId`/`SubjectType` exclusivamente dos claims.

## Segurança

- Token push nunca aparece em logs, respostas de listagem ou auditorias.
- Registro exige access token e o sujeito vem dos claims.
- Payload contém identificadores e rotas, nunca dados sensíveis completos.
- Deep links são validados por allowlist.
- Credencial Firebase vem de arquivo/ADC ou variável protegida, nunca do banco/web.
- O endpoint de teste não permite escolher outro destinatário.

## Critérios de aceite

- Registro idempotente e transferência segura de instalação.
- Token cifrado em repouso; hash único.
- Preferência desabilitada impede criação de entrega para aquela categoria.
- Concorrência de workers não duplica envio.
- `UNREGISTERED` desativa instalação; falha transitória reagenda; limite vira dead-letter.
- Morador nunca lista notificações de equipe/outro morador.
- Parser recusa deep links fora da allowlist.
- Build e suíte completos sem exigir credencial Firebase real.

## Limites externos

Entrega física em aparelho depende de projeto Firebase, `google-services.json`, `GoogleService-Info.plist`, credencial de serviço, certificados APNs e domínio HTTPS controlado. A SP-3 entrega código, endpoints e testes com transporte fake; a validação física será registrada como pendência configuracional caso esses ativos não estejam disponíveis.
