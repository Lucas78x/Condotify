# SP-1 — Gateway de Mídia CFTV — Design

Data: 2026-07-31

Parte de: [Condotify Mobile — Roadmap](2026-07-31-mobile-roadmap-design.md)
Depende de: [SP-0 — Contratos Compartilhados](2026-07-31-sp0-contratos-compartilhados-design.md)

## Contexto

Hoje o módulo de CFTV do Condotify é apenas um cadastro. `ICFTVService` (`CondotifyAPI/Services/CFTV/ICFTVService.cs:8`) expõe um único método, `TestAsync`, que verifica conectividade RTSP. `CamerasModule.razor` lista nome, IP e número de canais. **Não existe visualização de vídeo em lugar nenhum da plataforma** — nem HLS, nem WebRTC, nem MJPEG, nem snapshot, nem proxy.

O aplicativo mobile precisa exibir câmeras. Um WebView não consome RTSP, e entregar a URL RTSP ao cliente exporia usuário e senha do equipamento. Portanto o SP-1 constrói a peça que falta: um gateway que converte RTSP em algo que um navegador consome, sem que credencial alguma saia do servidor.

### O que já existe e será reaproveitado

**`CFTVService` sabe montar URLs RTSP por fabricante — mas só para gravadores.** Corrigido em 2026-08-01, durante a execução da Task 2, que descobriu que a afirmação original deste spec estava errada:

| | Situação real |
|---|---|
| **Gravadores** | `GetDvrTemplates` consulta `RtspPathTemplatesByBrand`, que cobre Intelbras, Dahua, Hikvision, Hilook, Uniview e Axis, com substituição de canal e distinção principal/secundário. **Código vivo.** |
| **Câmeras** | `GetCameraTemplates` devolve `/axis-media/media.amp` para Axis e, **para todas as demais marcas**, apenas `/live`, `/stream1`, `/h264`. Sem caminho por fabricante, sem distinção de stream. |
| `RtspPathsByBrand` (linha 15) e `GenericRtspPaths` (linha 65) | Dicionários ricos, com `subtype=0/1` e `Channels/101` vs `/102` — **código morto, nunca referenciados.** |

Ou seja: alguém escreveu a tabela por fabricante para câmeras com a intenção certa e nunca a ligou. O requisito de escolher stream principal ou secundário, portanto, **não** está atendido hoje para câmeras.

Consequência para o desenho: o resolvedor extraído expõe duas famílias de método, deliberadamente separadas.

- `ConnectivityProbePaths(...)` reproduz **exatamente** o comportamento vivo de hoje, incluindo a pobreza dos caminhos de câmera. É o que o teste de conexão continua usando, para que a extração não altere o que acontece contra hardware real.
- `PreferredPath(...)` usa a tabela rica, hoje morta, e serve **apenas ao gateway**. É comportamento novo, aditivo, e nenhum caminho existente passa a usá-lo.

A separação é intencional: mudar o que o teste de conexão tenta contra uma câmera real é uma alteração que nenhum teste desta base consegue validar, e não há câmera no ambiente para verificar. Melhorar a descoberta de caminhos de câmera fica registrado como trabalho futuro, com hardware disponível.

**Senhas de equipamento já são cifradas em repouso.** `CFTVDeviceConfiguration` aplica `EquipmentSecretConverter` à coluna `Password`, que usa AES-GCM com chave derivada de `CONDOTIFY_EQUIPMENT_SECRET`. O texto claro existe apenas em memória, depois que o EF decifra.

**`PrivateMediaStore` já estabelece o padrão de mídia privada.** AES-GCM, chave derivada de `CONDOTIFY_MEDIA_SECRET`, conteúdo servido por endpoint autenticado (`PrivateMediaController`). Os tokens efêmeros deste sub-projeto seguem o mesmo padrão em vez de introduzir um segundo mecanismo.

**Autorização já é resolvida no servidor.** `RequireLicensePermissionAttribute` + `ILicenseAuthorizationService` continuam sendo a autoridade. Nenhuma regra migra para o cliente.

## Escopo

Dentro do escopo:
- Serviço MediaMTX no `docker-compose.yml`, acessível apenas pela rede interna.
- Plano de controle na `CondotifyAPI`: abertura de sessão de vídeo, emissão de token efêmero, registro do caminho no MediaMTX.
- Endpoint de callback que autoriza cada leitura do MediaMTX.
- Endpoint de snapshot autenticado, para miniatura de lista, fallback de câmera offline e economia de dados.
- Extração do resolvedor de caminhos RTSP de `CFTVService`.
- Encerramento de sessão e expiração automática.
- Auditoria de quem assistiu o quê e quando.

Fora do escopo:
- Gravação e histórico de gravações. A plataforma não armazena vídeo hoje; isso é um sub-projeto próprio, com decisões de retenção e custo de disco.
- PTZ. Nenhum driver da plataforma expõe controle de movimento.
- Áudio. Nenhum requisito atual depende disso e a maioria das instalações não tem microfone.
- Qualquer alteração no `Condotify` (web). O SP-1 é backend puro; o consumo acontece no SP-4.
- Descoberta automática de câmeras.

### Justificativa das exclusões

Gravação, PTZ e áudio aparecem na lista de desejos do aplicativo, mas nenhum tem contrapartida na plataforma atual. Construí-los aqui triplicaria o sub-projeto e atrasaria tudo o que depende dele. Ficam registrados como trabalho futuro, não como esquecimento.

## Arquitetura

Dois planos separados, que é o que mantém a credencial fora do cliente.

```
                    plano de controle (HTTPS autenticado)
  App/Web  ──────────────────────────────────────────────>  CondotifyAPI
     │        POST .../cftv/{deviceId}/sessions                   │
     │        <── { playbackUrl, token, expiresAt }               │
     │                                                            │ registra caminho
     │                                                            │ (Control API :9997)
     │                                                            v
     │            plano de dados (HLS / WebRTC)              MediaMTX
     └──────────────────────────────────────────────────────>    │
                  GET {playbackUrl}?token=...                     │ RTSP com credencial
                                                                  │ (rede interna)
                            autoriza cada leitura                 v
              CondotifyAPI  <───────────────────────────────    Câmera
              POST /api/internal/media-auth
```

**Plano de controle.** O cliente pede uma sessão à API. A API valida a permissão `ViewDevices` na licença, decifra a senha da câmera, monta a URL RTSP, garante que o caminho existe no MediaMTX e devolve ao cliente apenas uma URL de playback mais um token de curta duração. A credencial nunca atravessa essa fronteira.

**Plano de dados.** O cliente pede o stream ao MediaMTX. O MediaMTX está configurado com `authMethod: http` e chama de volta a API a cada leitura, passando caminho, ação e token. A API valida o token e responde 200 ou 401. Assim a autorização continua sendo do servidor mesmo no caminho de mídia.

### Por que MediaMTX

Considerei três abordagens. FFmpeg gerenciado pela própria API significaria implementar ciclo de vida de processos, limpeza de segmentos, limite de sessões e transcodificação dentro do processo web — reconstruir o MediaMTX com menos qualidade e latência HLS de 6 a 15 segundos. Proxy de snapshot puro é simples mas não é vídeo ao vivo. MediaMTX é um binário único, open source, que faz RTSP→WebRTC e RTSP→HLS nativamente, entra como serviço no `docker-compose.yml` já existente e oferece autenticação HTTP externa, que é exatamente o gancho de que precisamos.

Snapshot **não** é alternativa ao MediaMTX: é parte da solução. O requisito de "exibir a última imagem quando o stream estiver indisponível" e a necessidade de miniatura em lista exigem JPEG de qualquer forma.

## Token efêmero

Formato: AES-GCM sobre um payload compacto, mesmo esquema de `PrivateMediaStore`, com chave derivada de `CONDOTIFY_MEDIA_SECRET`.

Payload:

| Campo | Motivo |
|---|---|
| `LicenseId` | escopo de tenant |
| `DeviceId` | o token serve a uma câmera, não a todas |
| `Channel` | gravadores têm vários canais com permissões iguais mas identidades distintas |
| `UserId` | auditoria e revogação |
| `ExpiresAt` | validade curta |
| `Nonce` | unicidade |

TTL padrão de **120 segundos**, renovável pelo cliente enquanto a tela estiver aberta. Curto o bastante para que um token vazado seja inútil, longo o bastante para tolerar rede móvel ruim.

O token autoriza **leitura de um caminho específico**. Não é um bearer de sessão e não substitui o JWT: o plano de controle continua exigindo o JWT normal.

## Endpoints novos

| Método | Rota | Permissão | Função |
|---|---|---|---|
| `POST` | `/api/access/licenses/{licenseId}/cftv/{deviceId}/sessions` | `ViewDevices` | Abre sessão, devolve `playbackUrl`, `token`, `expiresAt`, `protocol` |
| `DELETE` | `/api/access/licenses/{licenseId}/cftv/{deviceId}/sessions/{sessionId}` | `ViewDevices` | Encerra a sessão e libera o caminho |
| `GET` | `/api/access/licenses/{licenseId}/cftv/{deviceId}/snapshot` | `ViewDevices` | JPEG autenticado, para miniatura e fallback |
| `GET` | `/api/access/licenses/{licenseId}/cftv/status` | `ViewDevices` | Estado online/offline por câmera |
| `POST` | `/api/internal/media-auth` | interna | Callback do MediaMTX; valida o token |

`/api/internal/media-auth` é chamado apenas pelo MediaMTX pela rede interna do Docker. Além de validar o token, exige o `X-API-Key` já usado em outras rotas internas, para que não seja invocável de fora mesmo que a porta escape.

## Contrato do MediaMTX, medido e não presumido

Versão fixada: **`bluenviron/mediamtx:1.9.3`**. Os fatos abaixo foram obtidos rodando a imagem e observando o comportamento real, em 2026-08-01, não da documentação.

**Payload do callback de autenticação.** Com `authMethod: http` e `authHTTPAddress`, o MediaMTX faz `POST` com `Content-Type: application/json` e este corpo:

```json
{
  "action": "read",
  "id": null,
  "ip": "172.17.0.1",
  "password": "",
  "path": "probecam",
  "protocol": "hls",
  "query": "token=MEU_TOKEN_AQUI",
  "user": ""
}
```

`200` autoriza; qualquer outro status nega. Consequências para o desenho:

- **Não existe campo `token` dedicado nesta versão.** O token viaja na `query`, e o endpoint precisa extraí-lo de uma query string crua.
- `path` traz o nome do caminho, que é como o token fica atrelado a uma câmera específica.
- `ip` e `protocol` alimentam a auditoria sem esforço adicional.

**A Control API fica sem autenticação quando `authMethod: http`.** Este é o achado que mais afeta o desenho. Medido: `POST /v3/config/paths/add/...` e `GET /v3/config/paths/list` respondem `200` sem credencial alguma e **sem disparar o callback** — apenas o pedido de HLS gerou callback. Pior, `GET /v3/config/paths/list` devolve o campo `source` em texto claro:

```
- l1234_d5678_c1 | source: rtsp://user:pass@10.0.0.9:554/live | onDemand: True
```

Ou seja, **quem alcança a porta 9997 lê a senha de todas as câmeras da instalação e registra origens arbitrárias.** Não é higiene: é a superfície de ataque mais séria deste sub-projeto.

Regra decorrente, sem exceção: a porta `9997` **nunca** é publicada no host, nem em desenvolvimento. O MediaMTX participa de uma rede Docker interna, e apenas o contêiner da API alcança essa porta. Um teste de fumaça na Task 8 deve tentar alcançar `9997` a partir do host e **exigir falha de conexão**; se responder, o deploy está errado.

## Segurança

Não negociável:

- Nem `UserName`, nem `Password`, nem a URL RTSP montada, nem o endereço IP interno da câmera podem aparecer em qualquer resposta ao cliente. O DTO de sessão carrega apenas `playbackUrl`, `token`, `expiresAt`, `protocol` e `sessionId`.
- As portas do MediaMTX (`8888` HLS, `8889` WebRTC, `9997` Control API) **não** são publicadas para a internet. A Control API fica exclusivamente na rede interna do Docker; HLS e WebRTC só são expostos atrás do mesmo proxy que serve a API, e sempre com o callback de autenticação ativo.
- Logs nunca registram token nem credencial. As URLs RTSP montadas são mascaradas antes de qualquer log, incluindo os de erro — hoje `TestCftvConnection` devolve as URLs tentadas em `Attempts`, e essas URLs contêm a senha. **Isso é um vazamento existente que o SP-1 corrige**, já que o mesmo construtor de URL passa a ser compartilhado.
- Todo acesso a vídeo gera auditoria: usuário, licença, câmera, canal, início, fim, IP de origem e resultado.
- Limite de sessões simultâneas por licença, configurável, para que uma licença não consuma a banda de todas.

### Vazamento existente que este sub-projeto corrige

`CftvDeviceController.TestCftvConnection` devolve, no corpo do erro, `Attempts = c.Attempts.Take(5)`. Esses valores vêm de `ChannelTestResultOut.Attempts`, populado com as URLs RTSP completas — que incluem `usuario:senha@`. Qualquer usuário com permissão `ManageDevices` que provoque uma falha de teste recebe a senha da câmera em texto claro na resposta HTTP. A extração do construtor de URL inclui mascaramento obrigatório, e o endpoint passa a devolver URLs sem credencial.

## Estado online e offline

A tabela `CFTVDevices` não guarda estado operacional, ao contrário de `AccessControlDevices`, que tem `LastSeenAt`, `IsActive` e `HealthMessage`. O SP-1 acrescenta as três colunas equivalentes a `CFTVDevices` e um serviço de verificação periódica que reaproveita o `PingAsync`/`TcpPortOpenAsync` já existentes em `CFTVService`.

Isso alimenta o indicador online/offline do aplicativo e permite o fallback para última imagem conhecida sem esperar o timeout de um stream.

## Tratamento de falha

O gateway precisa distinguir e reportar, cada um com código próprio:

| Situação | Resposta |
|---|---|
| Câmera offline | `409` com `CameraOffline` |
| Credencial da câmera rejeitada | `502` com `CameraAuthFailed` |
| MediaMTX indisponível | `503` com `GatewayUnavailable` |
| Limite de sessões atingido | `429` com `SessionLimitReached` |
| Token expirado no callback | `401` |
| Permissão ausente | `403` |

O aplicativo depende dessa distinção para dar mensagem útil em vez de "erro ao carregar".

## Verificação

O SP-1 não produz tela. Sua verificação usa três níveis:

1. **Testes de unidade** — resolvedor de caminhos RTSP por fabricante, emissão e validação de token (válido, expirado, adulterado, câmera trocada, licença trocada), e o mascaramento de credencial em URL.
2. **Teste de integração com fonte RTSP sintética** — o próprio MediaMTX publica um stream de teste, ou o `ffmpeg` publica um padrão de cores. Prova o caminho RTSP→HLS→autorização fim a fim sem depender de hardware.
3. **Harness manual** — página HTML estática com player HLS, servida localmente, apontando para o `playbackUrl` devolvido pela API.

**Limite ambiental registrado:** a tabela `CFTVDevices` está vazia no ambiente de desenvolvimento atual e não há câmera real cadastrada. A verificação contra hardware de fabricante fica pendente e será explicitamente reportada como tal — nível 2 prova a arquitetura, não a compatibilidade com cada modelo.

## Riscos

| Risco | Grau | Mitigação |
|---|---|---|
| Credencial de câmera vazar ao cliente | **Alto** | DTO de sessão sem campo algum de credencial; teste que falha se o serializado contiver `Password`, `UserName` ou `rtsp://` |
| Portas do MediaMTX expostas sem autenticação | **Alto** | Callback de autenticação obrigatório; Control API sem publicação de porta; documentado no compose |
| Stream não encerrado consumindo banda e bateria | Médio | TTL curto, `sourceOnDemand`, encerramento explícito e limpeza de caminhos ociosos |
| Latência inaceitável em rede móvel | Médio | WebRTC como padrão, HLS como alternativa; seleção de stream secundário quando disponível |
| Incompatibilidade com modelo de câmera não testado | Médio | Resolvedor por fabricante já cobre 4 famílias; falha reportada com código próprio em vez de erro genérico |
| MediaMTX como nova dependência de deploy | Baixo | Serviço no compose já existente, imagem oficial com versão fixada |

## Compatibilidade

Nada em `Condotify` (web) muda. Nenhum endpoint existente muda de contrato — os novos são aditivos, exceto pela correção do vazamento em `test-connection`, que **remove** credencial da resposta sem alterar a forma do JSON.

A migração acrescenta três colunas a `CFTVDevices`, todas anuláveis, sem alteração de dados existentes.
