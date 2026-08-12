# Operação offline segura — Design

**Status:** aprovado pelo usuário para implementação.

## Objetivo

Manter a portaria operacional durante interrupções de internet sem transformar o aplicativo em uma fonte de autorização irrestrita. O aplicativo da equipe deve conseguir consultar as visitas previamente sincronizadas, validar QR Codes temporários, registrar a entrada localmente e reconciliar as operações automaticamente quando a conexão voltar.

## Estado atual

O mobile já observa a conectividade e possui `MobileOfflineCache`, mas esse cache serve somente para reapresentar resumos de tela. O leitor em `QrScannerDialog.razor` sempre chama `ScanConciergeVisitAsync`; portanto, sem API, nenhuma autorização é validada e nenhuma entrada é registrada. Não existe outbox persistente, identidade confiável do aparelho, política de janela offline ou supervisão web.

## Decisões de segurança

- Somente usuário de equipe com `ManagePeople` pode registrar/sincronizar um aparelho.
- O aparelho nasce **Pendente** e somente alguém com `ManageSettings` pode aprová-lo.
- Cada instalação recebe uma chave aleatória própria. Ela é criptografada no banco pelo conversor já usado para segredos de equipamentos e protegida pelo `SecureStorage` no aparelho.
- Cada pacote operacional é serializado pelo servidor e autenticado com HMAC-SHA256 por aparelho. O app valida a assinatura antes de substituir a base local.
- O arquivo local usa AES-256-GCM, escrita atômica e chave guardada no `SecureStorage`.
- O QR atual (`VIS-` + 128 bits aleatórios) permanece compatível. O pacote não armazena o segredo do convite: guarda somente SHA-256 do código normalizado.
- Não são armazenados localmente CPF, RG, telefone, foto ou biometria. Apenas dados mínimos para a decisão da portaria.
- O pacote tem validade curta configurável por aparelho (padrão: 8 horas; limites: 15 minutos a 12 horas).
- QR de utilização única somente pode ser aceito offline pelo **validador principal** da licença. Isso evita prometer unicidade global quando vários celulares estão simultaneamente isolados.
- Alteração suspeita de relógio, pacote vencido, assinatura inválida, aparelho revogado, rota fora do horário ou limite de usos atingido resultam em negação segura.
- Acionamento remoto de porta continua exigindo conexão com API/equipamento. O modo offline autoriza e registra a decisão; não simula comando físico confirmado.

## Backend

Nova área `api/access/licenses/{licenseId}/offline`:

- `POST devices/register`: registra/atualiza a instalação do usuário atual e devolve seu estado.
- `POST sync`: recebe a outbox idempotente, reconcilia cada item e devolve um pacote operacional completo autenticado.
- `GET devices`: supervisão dos aparelhos da licença.
- `PATCH devices/{deviceId}`: aprova, revoga, define janela offline e validador principal.
- `GET operations`: histórico paginado das operações recebidas.

Entidades:

- `OfflineAccessDeviceDTO`: vínculo licença/usuário/instalação, estado, segredo criptografado, política e telemetria de sincronização.
- `OfflineAccessOperationDTO`: operação idempotente, visita, pacote, horário confiável, resultado e mensagem de reconciliação.

O pacote contém somente visitas QR agendadas e ativas que cruzem a janela offline, seus hashes de código, validade, consumo, morador/unidade de destino, placa/purpose e janelas de rota efetivamente resolvidas.

## Aplicativo

`MobileOfflineOperationsService` concentra:

- registro do aparelho;
- sincronização ao iniciar, trocar de licença, voltar a ficar online e após operação online;
- verificação e armazenamento autenticado do pacote;
- avaliação local de QR;
- atualização otimista do consumo local;
- outbox persistente e reenvio idempotente;
- estado observável para a UI: dispositivo, última sincronização, expiração, fila e conflitos.

UX:

- banner global informa `Modo offline`, horário da última sincronização e quantidade pendente;
- portaria exibe os visitantes do pacote quando a API não responde;
- leitor diferencia validação online e `Entrada autorizada offline — sincronização pendente`;
- página de portaria mostra ação para sincronizar e um resumo da fila;
- logout apaga pacote, segredo e fila do usuário no aparelho.

## Plataforma

Nova aba `Operação offline` dentro de Administração:

- cartões com aprovados, pendentes, revogados e operações em conflito;
- lista responsiva dos aparelhos, última sincronização, usuário, plataforma, versão e validade;
- aprovação/revogação, ajuste de janela e escolha do validador principal;
- histórico recente da reconciliação, sem expor o QR.

## Reconciliação

Cada item possui `ClientOperationId` único por aparelho. Reenvios devolvem o resultado já persistido. Uma entrada é aplicada somente se:

- aparelho e usuário continuam autorizados;
- o pacote corresponde ao último pacote emitido para o aparelho;
- o horário estimado está dentro do pacote, da visita e de uma rota permitida;
- a credencial continua ativa e não atingiu o limite;
- a visita ainda permite a transição para entrada.

Se outra fonte já registrou a entrada, a operação vira **Conflito**, fica visível no portal e não duplica consumo. Rejeições mantêm trilha de auditoria.

## Verificação

- unidade: normalização/hash, assinatura, janela de rota, expiração, relógio regressivo, uso único e limite;
- backend: isolamento por licença, aprovação, segredo criptografado, idempotência, conflito e revogação;
- mobile: arquivo adulterado, outbox reaberta, sincronização parcial e limpeza no logout;
- manual Android/Windows: modo avião, retorno da rede, QR vencido, QR repetido e aparelho revogado.
