# Operação offline segura — Implementation Plan

**Goal:** manter a portaria capaz de validar visitas QR e registrar entradas durante interrupções, com armazenamento protegido, sincronização idempotente e supervisão web.

## Task 1 — Contratos e política pura

- Criar contratos compartilhados de dispositivo, pacote, permissão, sincronização e operação.
- Centralizar normalização e hash do código.
- Criar avaliador puro de pacote/rota/uso/relógio em `Condotify.Mobile.Core`.
- Cobrir regras com testes unitários.

## Task 2 — Persistência e endpoints backend

- Criar entidades/configurações/DbSets de dispositivos e operações offline.
- Criar serviço de construção/assinatura do pacote.
- Criar controller de registro, sync idempotente, gestão e histórico.
- Registrar auditoria de aprovação, revogação, aplicação, conflito e rejeição.
- Gerar migration e testes de backend.

## Task 3 — Armazenamento e orquestração mobile

- Criar arquivo AES-GCM com chave no SecureStorage e substituição atômica.
- Criar serviço singleton com registro, sync, outbox e estado observável.
- Sincronizar no retorno da rede e apagar tudo no logout.
- Integrar o ApiClient.

## Task 4 — Operação mobile

- Integrar o leitor QR com fallback offline seguro.
- Mostrar confirmação específica e fila pendente.
- Usar o pacote como fallback da Portaria.
- Melhorar banner global com última sincronização/expiração/fila.

## Task 5 — Supervisão web

- Criar `OfflineOperationsPanel.razor` em Administração.
- Implementar aprovação, revogação, validador principal, janela e histórico.
- Adicionar estilos responsivos e estados vazios/carregamento/erro.

## Task 6 — Migration e validação

- Gerar migration EF Core.
- Executar testes direcionados, API, portal, Mobile.Core e Android/Windows.
- Registrar qualquer falha basal de toolchain separadamente da feature.
