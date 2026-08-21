# Deploy com Docker Compose e Caddy

O arquivo `docker-compose.yml` da raiz e o unico ponto de entrada. Ele inclui:

- `deploy/proxy/compose.yaml`: Caddy, dono exclusivo das portas 80 e 443 e
  responsavel por HTTPS automatico;
- `deploy/condotify/compose.yaml`: portal, API, PostgreSQL, MediaMTX e OCR.

As variaveis compartilhadas ficam no `.env` da raiz. Certificados e chaves do
Caddy ficam no volume nomeado `condotify_caddy-data` e nao devem ser removidos.

## Primeira instalacao

Com Docker e Docker Compose ja instalados:

```bash
cd /opt/condotify
./deploy/bootstrap-vps.sh fefaccess.grupoff.net.br <porta-ssh>
```

## Atualizacao

```bash
cd /opt/condotify
./deploy/deploy-vps.sh
```

O script interrompe a atualizacao se o checkout estiver em outro branch, se
existirem alteracoes nao comitadas ou se o remoto exigir merge/rebase. Assim, o
codigo executado na VPS sempre corresponde a um commit identificavel no Git. O
comando tambem aguarda os health checks da API, portal, banco e proxy; uma
atualizacao que nao ficar pronta retorna erro em vez de aparentar sucesso.

Para implantar outro branch de forma explicita:

```bash
CONDOTIFY_DEPLOY_BRANCH=main ./deploy/deploy-vps.sh
```

Para validar ou recarregar apenas o proxy:

```bash
docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile
docker compose exec -w /etc/caddy caddy caddy reload --config Caddyfile
```

Somente `caddy` publica TCP 80/443. O MediaMTX publica UDP 8189 para o ICE do
WebRTC. Banco, portal, API, HLS e sinalizacao WebRTC nao publicam portas no host.

O Nginx e o Certbot legados podem permanecer instalados durante a janela de
rollback, mas seus servicos devem ficar desabilitados e sem sockets abertos.
