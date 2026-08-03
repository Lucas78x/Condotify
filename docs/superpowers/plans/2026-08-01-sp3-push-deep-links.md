# SP-3 — Plano de implementação de push notifications e deep links

Data: 2026-08-01

Spec: [SP-3 Design](../specs/2026-08-01-sp3-push-deep-links-design.md)

## Task 1: Contratos e deep links

- Criar enums/modelos compartilháveis de plataforma, categoria, preferências, inbox e registro.
- Criar parser/normalizador de rotas com allowlist e testes de URLs maliciosas.
- Adicionar métodos ao `CondotifyApiClient`.

## Task 2: Persistência

- Criar `PushInstallation`, `PushPreference`, `PushNotification` e `PushDelivery`.
- Configurar índices, FKs lógicas de sujeito, JSONB, timestamps UTC e migração aditiva.
- Proteger token em repouso e manter apenas hash pesquisável.
- Testar modelo e migração.

## Task 3: Registro e preferências

- Implementar endpoints principal-aware usando `AuthenticatedAnyPrincipal`.
- Upsert idempotente, transferência da instalação, desativação no logout e isolamento de consultas.
- Implementar inbox e marcação de leitura.
- Testar autorização e regras puras.

## Task 4: FCM HTTP v1

- Implementar obtenção/cache de OAuth por service account/ADC.
- Implementar payload FCM, classificação de erros e redaction.
- Testes com `HttpMessageHandler` fake; nenhuma chamada externa na suíte.

## Task 5: Outbox e worker

- Implementar enqueue com deduplicação.
- Claim por lease, fan-out por instalação, retry exponencial, dead-letter e invalidação de token.
- Testar concorrência, preferências, retry e destinatários.

## Task 6: Eventos da plataforma

- Enfileirar eventos de visitante, encomenda, reserva e alerta operacional nos pontos de transição existentes.
- Garantir que falha de push não reverta a operação principal.
- Testar chaves de deduplicação e deep links gerados.

## Task 7: Associações e verificação

- Publicar arquivos `/.well-known` por configuração, sem valores fictícios em produção.
- Build, migração lida, suíte completa e integração real de registro/inbox.
- Registrar o que depende de Firebase/APNs/domínio externo.
