# SP-1 Task 8 — Verificação integrada do gateway de mídia CFTV

Registro honesto do que foi provado, ponta a ponta, com uma fonte RTSP
sintética, e do que permanece **não verificado**. Não é uma feature; é a
evidência final do sub-projeto SP-1 (gateway de mídia CFTV).

## Ambiente

- `condotify-postgres` já rodava (45h de uptime, saudável) e não foi
  reiniciado nem tocado durante a verificação — confirmado por
  `docker inspect` antes/depois (mesmo `StartedAt`).
- `mediamtx` e `api` foram subidos via `docker compose up -d --no-deps` para
  esta verificação e removidos ao final (não rodavam antes; ver "Desvio do
  Step 1" abaixo).
- `CONDOTIFY_API_KEY` e `BACKUP_EXPORT_SECRET` foram passados inline na
  invocação do `docker compose`, nunca gravados em `.env`.

### Desvio do Step 1: `api` rodou em container Docker, não via `dotnet run`

O brief pedia `dotnet run --project CondotifyAPI/CondotifyAPI.csproj`. Não foi
o que foi feito, e o motivo é técnico, não preferência: `mediamtx.yml` tem
`authHTTPAddress: http://api:8081/api/internal/media-auth` — um nome DNS que
só existe *dentro* da rede Docker `condotify_default`, resolvido para o
container chamado `api`. Um processo `dotnet run` rodando no host não tem
esse nome; o container MediaMTX nunca conseguiria chamar o callback de volta.
Rodar a própria API como o serviço `api` do `docker-compose.yml` (com
`--no-deps --build`, sem tocar `postgres`) preserva o `authHTTPAddress`
exatamente como está commitado, em vez de exigir mais uma alteração temporária
nesse arquivo. É a mesma abordagem que a Task 6b já havia validado.

Isso só foi possível porque o bug de precedência de connection string que a
Task 6b registrou como pendência (`appsettings.json` sempre vencia
`CONDOTIFY_DB_CONNECTION`) já não existe: `Program.cs` hoje resolve
`CONDOTIFY_DB_CONNECTION` **antes** de `DefaultConnection`. O container `api`
subiu direto contra o `postgres` real, aplicou migrações (idempotentes) e
funcionou sem nenhum workaround.

## Step 2 — Control API isolada (portão de segurança)

```
curl -s -o /dev/null -w "%{http_code}" --max-time 3 http://localhost:9997/v3/config/paths/list ; echo
000
```

`curl -v` confirma que é falha de conexão genuína, não timeout silencioso:

```
* connect to ::1 port 9997 from :: port 56730 failed: Connection refused
* connect to 127.0.0.1 port 9997 from 0.0.0.0 port 56731 failed: Connection refused
* Failed to connect to localhost port 9997 after 2219 ms: Could not connect to server
curl: (7) Failed to connect to localhost port 9997 after 2219 ms: Could not connect to server
```

`docker ps` confirma que `condotify-mediamtx` só publica `8888`, `8889` e
`8189/udp` — `9997` nunca aparece na lista de portas do container. **Passou.**
Se tivesse respondido `200`, a instrução era parar tudo; não foi necessário.

## Step 3 — Fonte RTSP sintética

`ffmpeg` (imagem `linuxserver/ffmpeg`, comando exatamente como o brief sugere,
sem precisar de `--entrypoint`) gerou um padrão de cores + tom de 1kHz e
publicou via RTSP no MediaMTX, na rede `condotify_default` (nome confirmado
com `docker network ls` — bate com o que o brief assumia).

### Três alterações temporárias em `mediamtx/mediamtx.yml`, todas revertidas

A publicação exigiu mais do que o brief antecipava. Documentando as três,
porque todas foram feitas e todas foram desfeitas:

1. **`rtsp: yes`** — exatamente o que o brief pedia: sem isso o MediaMTX não
   aceita conexão de publisher nenhuma.
2. **`paths:` deixou de ser `{}`** — com `paths: {}`, o MediaMTX rejeita
   qualquer publish com `"path 'X' is not configured"`, mesmo com `rtsp: yes`.
   Foi adicionada uma entrada nomeada e específica (não um `all_others`
   coringa) para o único caminho sintético usado no teste.
3. **`authHTTPExclude` ganhou uma entrada para `action: publish` restrita a
   esse mesmo caminho.** Esta foi a descoberta não antecipada pelo brief:
   `MediaAuthController` só autoriza `action == "read"` — publish é sempre
   `401`, por desenho ("Publicação e API nunca", comentário no próprio
   controller). Sem essa exclusão, o `ffmpeg` sintético seria recusado pelo
   próprio callback de autorização ao tentar publicar. A exclusão foi
   escopada a um único `path:` literal, não a `publish` em geral — o
   `authHTTPExclude` do MediaMTX aceita filtro de caminho no mesmo formato de
   `authInternalUsers`, confirmado extraindo o `mediamtx.yml` de exemplo de
   dentro da própria imagem `bluenviron/mediamtx:1.9.3`.

As três foram revertidas ao final. `git diff mediamtx/mediamtx.yml` depois de
reverter não mostra nenhuma diferença em relação ao committed — arquivo
idêntico ao original. O container `mediamtx` foi recriado com o config
revertido e o log de partida confirma: sem linha `[RTSP] listener opened`,
só `[HLS]`, `[WebRTC]` e `[API]` — o servidor RTSP de entrada voltou a ficar
desligado.

```
2026/08/01 12:58:31 INF MediaMTX v1.9.3
2026/08/01 12:58:31 INF configuration loaded from /mediamtx.yml
2026/08/01 12:58:31 INF [HLS] listener opened on :8888
2026/08/01 12:58:31 INF [WebRTC] listener opened on :8889 (HTTP), :8189 (ICE/UDP)
2026/08/01 12:58:31 INF [API] listener opened on :9997
```

O 9997-check do Step 2 foi repetido depois do revert e continua `000`.

Log do MediaMTX confirmando a publicação bem-sucedida (durante a janela de
teste, com as três exceções temporárias ativas):

```
2026/08/01 12:55:50 INF [RTSP] [conn 172.19.0.5:50682] opened
2026/08/01 12:55:50 INF [RTSP] [session a0d14b2f] created by 172.19.0.5:50682
2026/08/01 12:55:50 INF [RTSP] [session a0d14b2f] is publishing to path
  'l11111111111111111111111111111111_d22222222222222222222222222222222_c1',
  2 tracks (H264, MPEG-4 Audio)
```

O caminho publicado **não** foi `camera-sintetica` (o nome literal do brief).
Foi `l11111111111111111111111111111111_d22222222222222222222222222222222_c1`
— exatamente o que `MediaAccessTokenService.PathFor(licenseId, deviceId,
channel)` produz para os GUIDs fixos usados no harness de token (Step 4). Ver
"Como o token foi obtido" abaixo para o porquê.

## Step 4 — Autorização (a evidência central)

### Como o token foi obtido — Opção (b), sem tocar no banco

`CFTVDevices` está vazia; emitir um token pela API de verdade exigiria criar
um usuário, uma licença e uma linha de equipamento. Em vez disso, foi
construído um pequeno harness de console (`token-harness`, fora do repo, em
`scratchpad/token-harness/`) que:

- referencia o **arquivo real** `CondotifyAPI/Services/CFTV/MediaAccessTokenService.cs`
  via `<Compile Include>` no `.csproj` — não é uma reimplementação do esquema
  de token, é a classe de produção compilada dentro de um console app;
- lê `CONDOTIFY_MEDIA_SECRET` do ambiente — o mesmo valor que
  `docker-compose.yml` usa por padrão para o serviço `api`
  (`condotify-local-media-secret-change-before-production-2026`, o placeholder
  já commitado, não um segredo real);
- usa GUIDs fixos e reproduzíveis (`licenseId=1111...1`, `deviceId=2222...2`,
  `channel=1`, `userId=3333...3`) e `MediaAccessGrant.ExpiresAt = UtcNow + 60s`
  (dentro do TTL de 120s);
- imprime o caminho vinculado (`PathFor`), um token válido, um token
  adulterado (um caractere invertido no meio da string base64url) e, como
  bônus, um token válido para um `channel` diferente (99) do mesmo
  license/device.

**Nenhuma linha foi inserida em `CFTVDevices` nem em nenhuma outra tabela.**
Nenhum `INSERT`/`UPDATE`/`DELETE` foi emitido contra `condotify-postgres`
durante a preparação do token — é por isso que a Opção (b) foi preferida:
zero necessidade de limpeza porque zero dado foi tocado.

### Os três resultados centrais

```
=== 1. Valid token (expect 200) ===
200

=== 2. Tampered token (expect 401) ===
401

=== 3. No token (expect 401) ===
401
```

Log da API correspondente à checagem 1 e às checagens 2/3 (mesma janela):

```
[12:56:17 INF] Leitura de video autorizada. Licenca 11111111-1111-1111-1111-111111111111,
  equipamento 22222222-2222-2222-2222-222222222222, canal 1,
  usuario 33333333-3333-3333-3333-333333333333, protocolo hls, origem 172.19.0.1.
[12:56:42 WRN] Leitura de video recusada. Caminho
  l11111111111111111111111111111111_d22222222222222222222222222222222_c1,
  protocolo hls, origem 172.19.0.1.
[12:56:42 WRN] Leitura de video recusada. Caminho
  l11111111111111111111111111111111_d22222222222222222222222222222222_c1,
  protocolo hls, origem 172.19.0.1.
```

Nenhuma credencial de câmera, URL RTSP construída ou IP de equipamento
aparece nesses logs — só GUIDs de licença/equipamento/usuário e o IP de quem
fez a requisição de leitura (o gateway Docker, `172.19.0.1`, não uma câmera).

Prova de que a resposta `200` é mídia de verdade, não um `200` vazio — o
corpo devolvido por `index.m3u8` com o token válido:

```
#EXTM3U
#EXT-X-VERSION:9
#EXT-X-INDEPENDENT-SEGMENTS

#EXT-X-MEDIA:TYPE="AUDIO",GROUP-ID="audio",NAME="audio2",AUTOSELECT=YES,DEFAULT=YES,URI="audio2_stream.m3u8?token=..."

#EXT-X-STREAM-INF:BANDWIDTH=194025,AVERAGE-BANDWIDTH=194025,CODECS="avc1.f40016,mp4a.40.2",RESOLUTION=640x480,FRAME-RATE=15.000,AUDIO="audio"
video1_stream.m3u8?token=...
```

Playlist HLS real, com resolução (640x480) e framerate (15fps) batendo
exatamente com os parâmetros do `ffmpeg` — a cadeia completa (API emite →
MediaMTX consulta → API valida → mídia flui) está de fato entregando os
segmentos do vídeo sintético, não só devolvendo um código HTTP.

### Bônus (não exigido pelo brief, mas relevante): vínculo por caminho, não por token genérico

```
=== Token válido emitido para outro canal (99) do mesmo device, contra o caminho real (expect 401) ===
401
=== Reconfirmação do token válido para o canal correto (expect 200) ===
200
```

Mostra que `MediaAccessTokenService.Validate` recusa por caminho incompatível
mesmo com um token genuíno e não expirado — o vínculo criptográfico
`l{licenseId}_d{deviceId}_c{channel}` está de fato sendo verificado, não só
"token existe e não expirou".

## Step 5 — Harness visual

`tools/media-gateway-harness.html` foi criado: página autocontida com
`<video>`, hls.js carregado via CDN (`cdn.jsdelivr.net/npm/hls.js@1.5.17`,
com fallback documentado para reprodução via HLS nativo no Safari), e dois
campos de entrada (Playback URL + token) que montam a query string e chamam
`hls.loadSource`.

**Limite honesto sobre este passo:** o arquivo foi criado e seu JavaScript
foi revisado manualmente, mas **não foi aberto num navegador real** durante
esta sessão — este ambiente de execução não tem uma ferramenta de navegador
automatizado (Playwright/Puppeteer) disponível para capturar uma screenshot
comprovando pixel a pixel que o vídeo aparece. A evidência de que a fonte HLS
é genuína e reproduzível vem do Step 4: o `.m3u8` buscado via `curl` é uma
playlist HLS válida referenciando faixas de vídeo e áudio com os parâmetros
exatos do `ffmpeg` (640x480, 15fps, H264+AAC), o que é fortemente indicativo
de que qualquer player HLS padrão (hls.js incluído) reproduziria a imagem.
Mas "fortemente indicativo" não é "visto renderizar" — por isso este passo
fica listado abaixo como **não verificado visualmente**, apesar do arquivo
existir e estar correto por inspeção.

## O que foi provado

- **Isolamento da Control API**: porta `9997` não escapa do host, confirmado
  por falha de conexão real (`curl` exit `7`) e pela lista de portas
  publicadas do container.
- **Emissão e validação de token**: o mesmo código de produção
  (`MediaAccessTokenService`) emite um token que o `MediaAuthController`
  real, rodando dentro do container `api`, aceita.
- **Autorização por callback**: o MediaMTX de fato chama
  `http://api:8081/api/internal/media-auth` a cada leitura — confirmado pelos
  logs `INF`/`WRN` da API batendo exatamente com os três `curl`s.
- **Vínculo do token ao caminho exato** (`l{licenseId:N}_d{deviceId:N}_c{channel}`),
  não apenas "token emitido por esta API" — confirmado pelo teste bônus com
  canal incompatível.
- **Recusa sem token e com token adulterado**: ambos `401`, confirmando que
  nem a ausência nem a corrupção do token abrem uma brecha.
- **Entrega HLS real**: o corpo do `index.m3u8` é uma playlist válida
  referenciando as faixas de vídeo/áudio do stream sintético.
- **Nenhum vazamento de credencial, URL RTSP montada ou IP de equipamento**
  nos logs da API durante toda a verificação.

## O que NÃO foi provado

- **Compatibilidade com qualquer modelo real de câmera.** `CFTVDevices`
  está vazia neste ambiente; nenhuma linha foi inserida como parte desta
  verificação (Opção (b) foi usada exatamente para evitar isso). Os caminhos
  RTSP por fabricante em `CftvStreamPathResolver.PreferredPath` (Intelbras,
  Dahua, Hikvision, Hilook, Uniview, Axis) **nunca foram exercitados contra
  hardware real** — só existem como strings de template, testadas
  unitariamente contra valores esperados, nunca contra um equipamento que
  realmente responda naquele caminho.
- **WebRTC.** O harness (`tools/media-gateway-harness.html`) e todo o Step 4
  usam exclusivamente HLS (porta `8888`). O caminho WebRTC (`/whep`, porta
  `8889`) nunca foi chamado nesta verificação — ele exige um navegador real
  negociando ICE/SDP, o que este ambiente de linha de comando não reproduz.
  `webrtc: yes` está ativo no `mediamtx.yml` e a porta `8889` está publicada,
  mas nada nesta verificação provou que uma sessão WebRTC completa (oferta,
  resposta, ICE, mídia) de fato funciona.
- **Renderização visual real do harness HTML.** O arquivo existe e foi
  revisado, mas não foi aberto num navegador nesta sessão (sem ferramenta de
  automação de navegador disponível) — ver a ressalva no Step 5.
- **Comportamento sob carga, múltiplas sessões simultâneas, ou reconexão.**
  Só uma sessão de leitura HLS foi exercitada por vez.
- **O fluxo real de ponta a ponta via `CftvStreamingController`**
  (`POST/DELETE .../cftv/{deviceId}/sessions`) **não foi chamado nesta
  verificação.** O token foi emitido diretamente pelo harness de console
  (Opção (b)), não pelo endpoint HTTP real que um cliente chamaria — porque
  esse endpoint exige uma linha em `CFTVDevices`, que este ambiente não tem.
  A validação do token (o lado que importa para a superfície de ataque) é a
  mesma classe em produção; a emissão via o controller HTTP real, com
  autenticação de usuário e checagem de licença, não foi exercitada aqui.

## Arquivos alterados

- `tools/media-gateway-harness.html` (novo)
- `docs/superpowers/plans/2026-07-31-sp1-verificacao.md` (novo, este arquivo)

`mediamtx/mediamtx.yml` foi alterado três vezes durante a verificação e
revertido às três vezes — `git diff` contra o committed mostra zero
diferença; não faz parte do commit desta task.

## Concerns

1. O brief não previu a exclusão de `authHTTPExclude` para `publish`
   necessária no Step 3 — sem ela, o próprio `ffmpeg` sintético seria negado
   pelo callback (`MediaAuthController` só libera `action == "read"`, por
   desenho). Isso não é um bug: é o comportamento correto para produção (o
   MediaMTX nunca deveria aceitar um publisher externo). Mas confirma que o
   modelo de "publish sintético via RTSP-server embutido" é inerentemente
   artificial — não existe um caminho de publish legítimo neste desenho; a
   forma fiel de simular uma câmera real teria sido fazer o `ffmpeg` atuar
   como servidor RTSP passivo (`-rtsp_flags listen`) e configurar o MediaMTX
   para *puxar* dele como cliente (o mesmo modelo usado para câmeras de
   verdade), evitando `rtsp: yes` e `authHTTPExclude` por completo. Não foi
   o que foi feito porque o brief pedia explicitamente o modelo de publish;
   registrando aqui como uma alternativa mais fiel para uma verificação
   futura.
2. O Step 1 do brief (`dotnet run`) foi substituído por `docker compose up
   --no-deps --build api`, pelo motivo técnico já explicado (resolução de
   nome `api` dentro da rede Docker). Nenhuma linha de código de produção foi
   alterada para isso — só a forma de subir o processo nesta verificação.
3. Nenhum teste automatizado novo foi adicionado por esta task — ela não
   escreve funcionalidade, conforme o próprio brief define. A suíte
   pré-existente (246 + 11) não foi executada como parte desta verificação
   porque nenhum código de produção foi alterado; não havia motivo para
   rodá-la.


## Adendo de 2026-08-01 — lacunas que a revisão final encontrou

A lista original de "não provado" estava honesta mas **incompleta**. Estes pontos também não foram verificados, e três deles esconderam defeitos reais:

- **`EnsurePathAsync` e `RemovePathAsync` nunca foram chamados contra o gateway em execução.** O caminho sintético foi publicado por `ffmpeg`, não registrado pela API. Foi aqui que se escondeu o defeito de acúmulo de caminhos: `ActiveViewerCountAsync` contava caminhos já registrados em vez de espectadores, e depois de 24 combinações câmera/canal a funcionalidade travava em 429 de forma permanente.
- **A URL de playback que o controller realmente emite nunca foi usada.** O `.m3u8` foi montado à mão contra a porta 8888, o que escondeu que o controller emitia HLS apontando para 8889, a porta do WebRTC.
- **`GET .../cftv/status` nunca foi chamado**, e o caminho de escrita do `CftvHealthMonitoringWorker` nunca rodou, porque `CFTVDevices` está vazia. A migração foi verificada; o código que grava aquelas três colunas, não.
- **O ramo de limite (429) nunca foi exercitado.**
- **A guarda de rota interna num deploy de porta única.** Com `CONDOTIFY_INTERNAL_PORT` em 8081 e a aplicação escutando só na 5000, toda rota `api/internal/*` devolve 404 sem log algum. Isso é configuração perigosa e não estava listada.
- **O isolamento intra-rede nunca foi verificado**, só o isolamento a partir do host. Não existia: os cinco serviços compartilhavam a bridge padrão, e `portal` ou `pgadmin` alcançavam `mediamtx:9997`, que devolve a credencial de cada câmera em texto claro.

Todos foram corrigidos. O que continua **não provado** segue sendo: compatibilidade com qualquer modelo real de câmera, reprodução por WebRTC, e a renderização do harness num navegador.
