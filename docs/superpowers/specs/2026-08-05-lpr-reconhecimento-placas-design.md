# LPR — Reconhecimento de Placas na Cancela — Design

Data: 2026-08-05

Depende de: [SP-1 — Gateway de Mídia CFTV](2026-07-31-sp1-gateway-midia-cftv-design.md)

## Contexto

O Condotify já resolve as duas pontas que a leitura automática de placa (LPR) precisa costurar, mas elas nunca foram ligadas uma à outra:

- **Câmeras** (`CFTVDevice`/`CFTVDeviceDTO`) já sabem tirar um snapshot JPEG por HTTP, por fabricante, via `CftvSnapshotService.FetchAsync(CFTVDeviceDTO device, int channel, ...)` (`CondotifyAPI/Services/CFTV/CftvSnapshotService.cs:17`), cobrindo Hikvision/Hilook (ISAPI), Dahua/Intelbras (cgi-bin), Axis e Uniview.
- **Cancelas** (`AccessControlDevice`) já podem ser abertas programaticamente sem credencial física: `AcessControlService.OpenDoorAsync(AccessControlDevice device, int channel)` (`CondotifyAPI/Services/AccessControl/AcessControlService.cs:22`) delega ao driver do fabricante (`IntelbrasUHFAccessControlDriver`, `ControlIdAccessControlDriver`, etc.) via `IAccessControlDriver.OpenDoorAsync`. É exatamente o método que o botão manual de abertura já chama — o LPR só precisa ser mais um chamador.
- **Veículos** (`VehicleDTO`, mapeado diretamente como entidade EF em `VehicleConfiguration.cs`) já existe na tabela `Vehicles`, com `Plate`, `UnitId`, `ResidentId` opcional e `TagIdentifier`. Índice único em `(UnitId, Plate)`. **Não há controller nem serviço que exponha CRUD sobre ele** — é uma tabela populável só por acesso direto ao banco hoje.
- **Credenciais** (`AccessCredentialTypeEnum`) já são polimórficas — `Face`, `QrCode`, `Card`, `Tag`, `VehicleTag`, `Password` — mas `VehicleTag` é uma tag RFID/UHF física associada ao veículo, não uma placa lida por câmera. Não confundir os dois: LPR não usa esse enum, é um caminho de decisão paralelo.

### O que falta e não existe hoje

- **Nenhum vínculo entre `AccessControlDevice` (a cancela) e `CFTVDevice` (a câmera que a filma).** São duas tabelas independentes, sem FK entre si.
- **Nenhum controller para `VehicleDTO`.** Sem cadastro de veículo por unidade, o LPR não tem contra o que comparar.
- **Nenhum serviço de OCR/ALPR em lugar nenhum da base.**

Este spec cobre as três lacunas, reaproveitando tudo o que já existe nas pontas.

## Escopo

Dentro do escopo:
- Vínculo opcional entre cancela e câmera (`AccessControlDevice` ganha referência a um `CFTVDevice` + canal).
- Toggle por dispositivo: LPR desligado / `DetectionOnly` / `AutoOpen`.
- `VehicleController` (CRUD básico de veículo por unidade — pré-requisito, não add-on).
- Microsserviço de OCR/ALPR próprio (self-hosted, sem dependência de API paga de terceiro).
- `LprPollingService`: orquestração em .NET que puxa snapshot, chama o OCR, decide a ação.
- Nova auditoria de leitura de placa (`VehicleAccessAudit`), independente de `DeviceAudit`.
- Integração com alertas existentes (Concierge/Alerts) quando `AutoOpen` não encontra correspondência.

Fora do escopo:
- Câmera ANPR dedicada com leitura nativa em firmware — decidido explicitamente contra isso: o objetivo é funcionar com o parque de câmeras genéricas já cadastrado, sem exigir troca de hardware por condomínio.
- API de OCR em nuvem paga — descartada por custo recorrente por leitura e por tirar a imagem do veículo da infraestrutura do cliente (relevante para LGPD).
- Gatilho por sensor físico (laço magnético) ou por evento de movimento da câmera — o disparo é por polling periódico simples; os outros dois ficam registrados como evolução futura caso o custo de CPU do polling se mostre um problema real.
- Qualquer UI de mobile/web para configurar o toggle ou cadastrar veículo — este spec é backend puro, a tela fica para um sub-projeto de consumo, no mesmo padrão do SP-1 (gateway de mídia) que também foi backend-only.

## Arquitetura

```
                    (a cada N segundos, por cancela com LPR ativo)
  LprPollingService ──────────────> CftvSnapshotService.FetchAsync(camera, channel)
        │                                     │
        │                                     v
        │                              snapshot JPEG
        │                                     │
        v                                     │
  POST /recognize  <──────────────────────────┘
  (microsserviço OCR, rede interna)
        │
        v
  { plate, confidence }
        │
        v
  normaliza + debounce + busca VehicleDTO ativo na mesma licença
        │
        ├── achou + modo AutoOpen ────> AcessControlService.OpenDoorAsync(device, channel)
        ├── achou + modo DetectionOnly ─> só grava VehicleAccessAudit
        ├── não achou + modo AutoOpen ──> cancela fechada + alerta pro Concierge (com snapshot)
        └── não achou + modo DetectionOnly ─> grava VehicleAccessAudit "não identificado", sem alerta
```

O microsserviço de OCR é deliberadamente burro e sem estado: recebe bytes de imagem, devolve texto de placa e confiança. Toda regra de negócio (a quem essa placa pertence, o que fazer com o resultado) fica em C#, no mesmo processo que já entende licença, permissão e driver de cancela — o OCR não precisa saber nada disso.

### Por que um microsserviço separado, e não tudo em .NET

O ecossistema de reconhecimento de placa maduro e open source (OpenALPR, pipelines YOLO+OCR) é overwhelmingly Python. Bindings .NET para OCR/visão computacional (Tesseract .NET, OpenCvSharp) existem mas exigem montar a etapa de localização de placa na mão, com muito mais tuning nosso pra chegar numa precisão aceitável. Isolar o OCR num container próprio, chamado por HTTP interno, segue o mesmo padrão que a base já usa para o MediaMTX (SP-1): serviço externo especializado, orquestração e autorização continuam em C#.

## Novos campos e entidades

**`AccessControlDevice`** ganha (colunas anuláveis, sem afetar registros existentes):
| Campo | Tipo | Motivo |
|---|---|---|
| `LprCameraId` | `Guid?` | Qual `CFTVDevice` filma esta cancela. Nulo = LPR não configurado. |
| `LprCameraChannel` | `int?` | Canal do DVR/NVR, quando a câmera não for standalone. |
| `LprMode` | `enum? (DetectionOnly, AutoOpen)` | Nulo/ausente = LPR desativado nesta cancela. Granularidade por dispositivo, não por licença — decidido para permitir, por exemplo, abertura automática só na cancela de moradores e detecção simples na de visitantes. |

**`VehicleAccessAudit`** (nova tabela, mesmo espírito de `DeviceAudit` mas com campos próprios — não reaproveita `ActionTypeEnum`, que é genérico demais para as ações específicas de LPR):
| Campo | Motivo |
|---|---|
| `AccessControlDeviceId` | qual cancela |
| `PlateRead` (nullable) | o que o OCR devolveu, nulo se confiança abaixo do limiar |
| `Confidence` | valor bruto devolvido pelo OCR |
| `MatchedVehicleId` (nullable) | `VehicleDTO.Id` se encontrou correspondência ativa |
| `Action` (enum: `NoRead`, `Opened`, `DetectedOnly`, `AlertRaised`) | o que de fato aconteceu |
| `SnapshotReference` | referência à imagem capturada (mesmo padrão de mídia privada do SP-1, não a imagem crua em texto claro no banco) |
| `Timestamp` | quando |

O snapshot é persistido pelo próprio `LprPollingService` (via o mesmo mecanismo de mídia privada do SP-1) antes de gravar a auditoria — o microsserviço de OCR recebe a imagem por parâmetro de chamada e não a armazena.

**`VehicleController`** (novo): CRUD de `VehicleDTO` escopado por unidade/licença, seguindo o padrão de autorização (`RequireLicensePermissionAttribute`) já usado nos demais controllers de estrutura. Pré-requisito de dados para o LPR ter contra o que comparar — sem cadastro de veículo, a feature não tem o que fazer.

## Fluxo de dados

1. `LprPollingService` (novo `IHostedService`) acorda a cada N segundos (configurável) para cada `AccessControlDevice` com `LprMode` não nulo.
2. Busca o `CFTVDeviceDTO` referenciado por `LprCameraId` e chama `CftvSnapshotService.FetchAsync` (já existente) para obter o JPEG mais recente.
3. Envia a imagem por `POST /recognize` ao microsserviço de OCR; recebe `{ plate, confidence }`.
4. Se `confidence` abaixo do limiar configurado → grava `VehicleAccessAudit` com `Action = NoRead` e encerra o ciclo.
5. Normaliza a placa lida (maiúsculas, remove caracteres não alfanuméricos, aceita formato Mercosul e formato antigo).
6. **Debounce**: se a mesma placa já gerou uma ação nesta cancela nos últimos X segundos, ignora o ciclo — evita reabrir a cancela ou disparar alerta repetido para o mesmo veículo parado em frente à câmera.
7. Busca `VehicleDTO` com `Plate` igual, `IsActive = true`, escopado à mesma licença da cancela (via a licença que contém o dispositivo, `License.Devices`).
8. Decide a ação conforme a tabela da seção de arquitetura, grava `VehicleAccessAudit` e, quando aplicável, chama `AcessControlService.OpenDoorAsync` ou dispara alerta para o Concierge com o `SnapshotReference` anexado.

## Tratamento de falha

| Situação | Comportamento |
|---|---|
| Câmera não responde ao snapshot | Log de falha com backoff; não tenta a cada ciclo se falhou recentemente. Alerta operacional (equipe de suporte, não morador) se ficar offline por tempo prolongado. |
| Microsserviço de OCR indisponível | Ciclo daquela cancela é pulado; se persistir, gera alerta operacional. |
| **Cancela em modo `AutoOpen` nunca abre "no escuro"** | Se OCR ou câmera falharem, é tratado como leitura sem confiança: cancela permanece fechada, morador cai no fallback de QR/tag/cartão que já existe hoje. O LPR nunca é a única forma de decidir uma abertura sem confirmação. |
| Falso positivo (placa lida errado bate com outra cadastrada) | Mitigado pelo limiar de confiança mínimo. Se se mostrar um problema recorrente em produção, fica registrado como evolução futura exigir confirmação visual no alerta antes de contar como "aberto" — não construído agora sem evidência de necessidade. |

## Segurança e LGPD

- Placa de veículo e a imagem capturada na cancela são dado pessoal sob a LGPD (identificam o veículo e, indiretamente, o morador vinculado). `VehicleAccessAudit.SnapshotReference` segue o mesmo padrão de mídia privada autenticada do SP-1 (`PrivateMediaStore`/`PrivateMediaController`) — nunca uma URL pública ou caminho de arquivo em texto claro.
- O microsserviço de OCR fica exclusivamente na rede interna do Docker, sem porta publicada — mesmo padrão de isolamento já aplicado ao MediaMTX no SP-1. Ele não persiste nada: recebe bytes, devolve texto, esquece.
- Necessária política de retenção para `VehicleAccessAudit` e para os snapshots referenciados (ex: purga após N dias), a ser definida com o time de produto — registrado aqui como pendência explícita, não como decisão tomada.
- Autorização de configuração do LPR (ligar/desligar por dispositivo, definir modo) usa o mesmo `RequireLicensePermissionAttribute` já usado para `OperateDevices`/`ManageDevices`.

## Testes

- Unitários (xUnit, padrão já usado no projeto): normalização de placa (Mercosul vs. formato antigo), lógica de debounce, lógica de decisão por modo (`DetectionOnly` vs `AutoOpen`) × (achou vs não achou).
- Teste de contrato do cliente HTTP para o microsserviço de OCR, com resposta mockada (`{ plate, confidence }` em variações de confiança).
- Teste de integração do fluxo completo usando o driver fake que os testes de `AccessControlServiceTests` já usam, cobrindo: leitura com match e modo `AutoOpen` → chama `OpenDoorAsync`; leitura sem match e modo `AutoOpen` → alerta sem abrir; qualquer resultado em modo `DetectionOnly` → nunca chama `OpenDoorAsync`.

**Limite ambiental a registrar na verificação:** não há câmera real nem cancela real no ambiente de desenvolvimento hoje (o mesmo limite já registrado no spec do SP-1). A verificação do reconhecimento em si depende de um corpus de imagens de placa reais ou sintéticas para validar a precisão do OCR escolhido — isso é trabalho de calibração, não de arquitetura, e fica para a fase de implementação.

## Riscos

| Risco | Grau | Mitigação |
|---|---|---|
| Precisão do OCR self-hosted abaixo do aceitável em câmeras de baixa resolução/iluminação ruim | Alto | Limiar de confiança configurável; modo `AutoOpen` nunca abre sem confiança suficiente; fallback de QR/tag sempre disponível |
| Abertura indevida por falso positivo | Médio | Debounce; limiar de confiança; auditoria completa por leitura, permitindo detectar e ajustar após incidente |
| Custo de CPU do polling contínuo crescendo com número de cancelas/condomínios | Médio | Intervalo de polling configurável; caminho de evolução já identificado (fila de mensagens) se o volume justificar, mas não construído agora |
| Dado pessoal (placa/imagem) sem política de retenção definida | Médio | Registrado como pendência explícita nesta spec; não é decisão tomada, é lacuna aberta pro time de produto fechar antes do rollout |
| Ausência de vínculo cancela↔câmera hoje | Baixo (mitigado neste spec) | `LprCameraId`/`LprCameraChannel` novos em `AccessControlDevice`, nulos por padrão, sem afetar cadastros existentes |

## Compatibilidade

Nenhum endpoint existente muda de contrato. As colunas novas em `AccessControlDevice` são todas anuláveis/com LPR desligado por padrão — cancelas já cadastradas continuam funcionando exatamente como hoje até que alguém configure o vínculo com uma câmera. `VehicleController` é inteiramente novo, sem sobrepor rota existente. `VehicleAccessAudit` é tabela nova, sem relação com `DeviceAudit`.
