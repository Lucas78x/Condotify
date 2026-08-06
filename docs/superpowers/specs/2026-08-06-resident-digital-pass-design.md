# Morador emitir/revogar o passe digital pelo app

## Contexto

Hoje só o porteiro (portal web, `ConciergeVisitDetailDialog.razor`) consegue
emitir/revogar um passe digital com Google/Apple Wallet, via
`DigitalPassesController` (`api/access/licenses/{licenseId}/visits/{visitId}/pass`),
protegido por `LicensePermissionEnum.ManagePeople` — uma permissão que só
existe no modelo de autorização de **staff** (`LicenseUserAccesses`).

O morador no app (`Condotify.Mobile`) autentica por um esquema **totalmente
separado** (`Authorize(Policy = "Resident")`, `ResidentAccessGrant`), sem
nenhuma permissão em comum com o staff. Ele já vê a visita e o QR Code
(`VisitorPassDialog.razor`), mas não tem como adicionar à carteira.

## Bug pré-requisito (bloqueia a feature, corrigir de qualquer forma)

`ResidentProfileController.ToVisitOut` (as duas sobrecargas, e a montagem
inline em `GET api/resident/visits`) nunca preenche `ConciergeVisitOut.LicenseId`
— fica sempre `Guid.Empty`. Qualquer chamada de Wallet feita pelo app usando
esse `LicenseId` falharia. Corrigir adicionando `LicenseId = visit.LicenseId`
nas duas sobrecargas de `ToVisitOut`, e trocar a montagem inline do `GET
/visits` para reusar `ToVisitOut(x)` em vez de duplicar os campos (mesmo bug,
dois lugares — reduzir para um único ponto de verdade).

## Decisão de arquitetura

**Endpoint novo com escopo de morador, não expandir o endpoint do porteiro.**
Segue o padrão já estabelecido em `ResidentCftvController`/`ResidentProfileController`:
`[Authorize(Policy = "Resident")]`, obtém o `ResidentAccessGrant` e autoriza
comparando `visit.HostResidentId == grant.ResidentId` (não por permissão, já
que morador não tem permissão — tem posse da visita).

**Lógica de emissão/revogação extraída para um serviço compartilhado.**
Hoje o corpo de `DigitalPassesController.Issue`/`Revoke` (upsert do
`DigitalPassDTO`, hash do token, grava auditoria, monta `PublicUrl` e chama
`DigitalPassProviderService.Build`) vive só dentro do controller do porteiro.
Extrair para um novo `IDigitalPassIssuanceService` com:

```csharp
Task<DigitalPassViewModel> IssueAsync(Guid licenseId, Guid visitId, string publicUrlRoot, Guid? actorUserId, string actorName, CancellationToken ct);
Task<bool> RevokeAsync(Guid licenseId, Guid visitId, Guid? actorUserId, string actorName, CancellationToken ct);
```

`IssueAsync` lança `InvalidOperationException` com a mensagem de conflito
atual quando a visita não está válida ou não tem credencial (o controller
converte pra `Conflict(...)`) e retorna `null`/lança quando a visita não
existe (o controller converte pra `NotFound()`). Os dois controllers
(`DigitalPassesController` e o novo endpoint do morador) ficam finos: só
autorização + tradução de exceção pra status HTTP. Isso evita que os 3 bugs
que acabamos de corrigir na emissão (`??` com string vazia, RSA descartado,
URL pública errada) precisem ser corrigidos duas vezes no futuro.

## Escopo — backend

- Novo `IDigitalPassIssuanceService`/`DigitalPassIssuanceService` em
  `CondotifyAPI/Services/Operations/`, registrado no DI.
- `DigitalPassesController.Issue`/`Revoke` refatorados para chamar o serviço
  novo (comportamento observável idêntico ao atual).
- Novos endpoints em `ResidentProfileController.cs` (mesmo arquivo dos
  outros endpoints de morador, mesmo padrão de DI):
  - `POST api/resident/visits/{visitId}/pass`
  - `DELETE api/resident/visits/{visitId}/pass`
  - Ambos: `grant = await _authorization.GetGrantAsync(User, ct)` → `Forbid()`
    se nulo; carrega a visita por `visitId` + `grant.LicenseId`; `Forbid()`
    (não `NotFound`, pra não revelar existência) se `visit.HostResidentId != grant.ResidentId`.
- Fix do `LicenseId` ausente em `ToVisitOut` (ambas sobrecargas) +
  simplificação do `GET /visits` pra reusar `ToVisitOut`.

## Escopo — mobile

- `Condotify.ApiClient/CondotifyApiClient.cs`: novos métodos
  `IssueResidentDigitalPassAsync(Guid visitId, ct)` e
  `RevokeResidentDigitalPassAsync(Guid visitId, ct)`, mesmo padrão de
  `IssueDigitalPassAsync`/`RevokeDigitalPassAsync` mas sem `licenseId` na URL
  (o servidor pega da sessão do morador, não do caller).
- `VisitorPassDialog.razor`: adiciona um bloco "Passe digital Condotify"
  abaixo do QR Code (mesmo texto/padrão visual do
  `ConciergeVisitDetailDialog.razor`), com botão "Adicionar à carteira"
  (chama Issue — idempotente, funciona tanto pra emitir quanto reemitir) e,
  uma vez emitido, mostra os botões "Google Wallet"/"Apple Wallet" (só os
  que estiverem configurados) e um botão "Revogar passe".
- Estado do diálogo: sem round-trip de "consultar status" ao abrir — só
  emite quando o morador clicar. Depois de emitir, guarda o
  `DigitalPassViewModel` retornado em memória local do componente pra
  mostrar os botões de carteira.

## Autorização — resumo

| Quem | Como autoriza |
|---|---|
| Porteiro (existente) | `LicensePermissionEnum.ManagePeople` via `LicenseUserAccesses` |
| Morador (novo) | `Authorize(Policy = "Resident")` + `visit.HostResidentId == grant.ResidentId` + flag da licença abaixo |

Nenhuma mudança nas regras de quem hoje já pode emitir/revogar — só adiciona
um caminho novo, mais restrito (só a própria visita), pro morador.

## Flag por licença: "moradores podem emitir passe digital"

Escopo pontual, **não** o sistema geral de toggle de funcionalidades por
licença (isso fica pra uma spec própria, decidido separadamente). Reusa a
tabela/tela de política de credencial que já existe por licença, em vez de
criar uma tabela nova só pra um booleano:

- Novo campo `AllowResidentDigitalPass` (bool, default `true`) em
  `LicenseCredentialPolicyDTO` (`CondotifyAPI.Domain/DTO/License/LicenseCredentialPolicyDTO.cs`)
  e no DTO espelho `CredentialPolicyOut`/`UpdateCredentialPolicyIn`
  (`CondotifyAPI/Data/Administration/LicenseAdministrationDtos.cs`) — já
  expostos por `LicenseAdministrationController.GetPolicyAsync`/`UpdatePolicy`.
- Migration simples: `ALTER TABLE "LicenseCredentialPolicies" ADD "AllowResidentDigitalPass" boolean NOT NULL DEFAULT TRUE`.
- Novo toggle em `Condotify/Components/LicenseModules/AdministrationModule.razor`,
  ao lado dos toggles de política de credencial que já existem lá
  (`AllowQrCodeRenewal`, `RequireFacePhoto`, etc.) — mesmo padrão visual,
  mesmo formulário, sem tela nova.
- Os dois novos endpoints de morador (`POST`/`DELETE
  api/resident/visits/{visitId}/pass`) carregam a política da licença antes
  de chamar `IDigitalPassIssuanceService` e retornam `Forbid()` se
  `!policy.AllowResidentDigitalPass`. O endpoint do porteiro **não** verifica
  essa flag — ela é só sobre o morador emitir por conta própria; o porteiro
  continua podendo emitir manualmente mesmo com a flag desligada.

## Testes

- Unitário: `DigitalPassIssuanceService` — emitir com sucesso, conflito de
  visita inválida/sem credencial, revogar existente, revogar inexistente.
- Unitário: `ToVisitOut` agora preenche `LicenseId` corretamente (regressão
  do bug encontrado).
- Integração de autorização: morador tentando emitir passe de uma visita que
  **não** é dele → `Forbid`.
- Integração de autorização: `AllowResidentDigitalPass = false` na licença →
  morador recebe `Forbid` ao tentar emitir; porteiro continua emitindo
  normalmente pelo portal.

## Fora de escopo

- Mudar quem pode emitir passe pelo portal (porteiro continua igual).
- Qualquer mudança no fluxo de Apple Wallet além de reusar o mesmo serviço.
- Notificar o morador quando o passe expira (feature separada).
- **Sistema geral de toggle de funcionalidades por licença** ("quase tudo no
  app pode ser desabilitado por permissão"). Decisão explícita: essa spec
  cobre só a única flag pontual acima; o sistema geral fica pra uma spec
  própria, com seu próprio levantamento de quais recursos, modelo de dados e
  tela de administração.
