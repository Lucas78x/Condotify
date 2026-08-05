# Operacao integrada, CFTV, passes e emergencia

## Objetivo

Adicionar cinco capacidades conectadas ao portal Condotify: central de ocorrencias, regras operacionais, CFTV ao vivo, passes digitais de visita e modo de emergencia. O desenho reaproveita eventos, visitas, equipamentos, alertas, notificacoes e auditoria existentes.

## Fluxo integrado

- Regras observam equipamentos offline, recusas de acesso, permanencia de visitantes e encomendas atrasadas.
- Cada regra pode abrir uma ocorrencia, gerar um alerta ou executar as duas acoes.
- A ocorrencia e o registro duravel da operacao: prioridade, responsavel, comentarios, evidencias, status e passagem de turno.
- O monitor CFTV usa sessoes WebRTC protegidas e permite correlacionar camera, canal e horario a uma ocorrencia.
- O modo de emergencia abre uma ocorrencia critica, gera alerta notificavel e mantem orientacoes visiveis ate o encerramento confirmado.
- A autorizacao de visita pode emitir um passe Condotify publico, temporario e revogavel; Google Wallet usa JWT assinado e Apple Wallet recebe um pacote `.pkpass` assinado quando os respectivos certificados estao configurados.

## Seguranca

- Novas permissoes separam consulta de gestao para ocorrencias, automacoes e emergencia.
- Ativacao e encerramento de emergencia exigem frases com o codigo da licenca.
- A ativacao nao aciona portas automaticamente. Comandos fisicos continuam exigindo uma acao humana autorizada.
- Tokens de passes tem 256 bits de entropia e somente SHA-256 e persistido.
- CFTV nao devolve IP, usuario ou senha ao navegador; o player recebe somente uma URL curta de gateway.
- Toda mutacao relevante cria `AccessOperationAuditDTO`.

## Persistencia

- `Incidents` e `IncidentTimelineEntries` guardam a vida completa da ocorrencia.
- `AutomationRules` e `AutomationExecutions` guardam configuracao, cooldown e execucoes idempotentes.
- `EmergencySessions` liga o protocolo a uma ocorrencia critica.
- `DigitalPasses` liga um token revogavel a uma `AccessVisit`.

## Provedores de carteira

O passe web funciona sem fornecedor externo. Google Wallet requer issuer, service account, chave RSA e classe Generic Pass existente. Apple Wallet e gerado nativamente e requer Pass Type Identifier, Team Identifier, certificado Pass Type com chave privada e o certificado intermediario WWDR. Todos podem ser fornecidos por configuracao/segredo de deploy; uma URL de emissor externo continua disponivel como fallback opcional.
