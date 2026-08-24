#!/usr/bin/env bash
set -Eeuo pipefail

DOMAIN="${1:-fefaccess.grupoff.net.br}"
SSH_PORT="${2:-22}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB_NETWORK="${CONDOTIFY_WEB_NETWORK:-condotify-web}"
MEDIA_PROXY_NETWORK="${CONDOTIFY_MEDIA_PROXY_NETWORK:-condotify-media-proxy}"

if [[ "$DOMAIN" != "fefaccess.grupoff.net.br" ]]; then
    echo "Dominio inesperado: $DOMAIN" >&2
    exit 2
fi

cd "$ROOT_DIR"
umask 077

if [[ ! -f .env ]]; then
    random_hex() { openssl rand -hex "$1"; }

    cat > .env <<EOF
POSTGRES_PASSWORD=$(random_hex 32)
PGADMIN_PASSWORD=$(random_hex 32)
JWT_SECRET=$(random_hex 64)
EQUIPMENT_SECRET=$(random_hex 32)
MEDIA_SECRET=$(random_hex 32)
CONDOTIFY_WALLET_SECRET=$(random_hex 32)
BACKUP_EXPORT_SECRET=$(random_hex 32)
CONDOTIFY_API_KEY=$(random_hex 32)
BACKUP_EXPORT_PATH=$ROOT_DIR/backups
CONDOTIFY_TIME_ZONE=America/Bahia
CONDOTIFY_PUBLIC_HOST=$DOMAIN
CONDOTIFY_PUBLIC_PORTAL_URL=https://$DOMAIN
CONDOTIFY_PUBLIC_APP_URL=https://$DOMAIN
MOBILE_LINKS_ANDROID_PACKAGE_NAME=br.com.condotify.app
MOBILE_LINKS_ANDROID_SHA256_FINGERPRINT_0=
MOBILE_LINKS_APPLE_TEAM_ID=
MOBILE_LINKS_APPLE_BUNDLE_ID=br.com.condotify.app
CONDOTIFY_MEDIA_HLS_BASEURL=https://$DOMAIN/media/hls
CONDOTIFY_MEDIA_WEBRTC_BASEURL=https://$DOMAIN/media/webrtc
CONDOTIFY_MEDIA_WEBRTC_HOSTS=$DOMAIN
CONDOTIFY_MEDIA_MAX_VIEWERS_PER_LICENSE=24
CONDOTIFY_MEDIA_TRANSCODE_AUDIO=true
CONDOTIFY_WEB_NETWORK=$WEB_NETWORK
CONDOTIFY_MEDIA_PROXY_NETWORK=$MEDIA_PROXY_NETWORK
CONDOTIFY_SMTP_HOST=
CONDOTIFY_SMTP_PORT=587
CONDOTIFY_SMTP_USERNAME=
CONDOTIFY_SMTP_PASSWORD=
CONDOTIFY_SMTP_FROM_EMAIL=
CONDOTIFY_SMTP_FROM_NAME=F&F Access
CONDOTIFY_SMTP_ENABLE_SSL=true
EOF
fi

sed -i 's/fefacess\.grupoff\.com/fefaccess.grupoff.net.br/g' .env
chmod 600 .env

sudo install -d -m 0770 -o 1654 -g "$(id -g)" "$ROOT_DIR/backups"
sudo chown 1654:"$(id -g)" "$ROOT_DIR/backups"
sudo chmod 0770 "$ROOT_DIR/backups"

sudo apt-get update
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y ufw curl ca-certificates openssl
sudo apt-get clean

docker network inspect "$WEB_NETWORK" >/dev/null 2>&1 \
    || docker network create "$WEB_NETWORK" >/dev/null
docker network inspect "$MEDIA_PROXY_NETWORK" >/dev/null 2>&1 \
    || docker network create "$MEDIA_PROXY_NETWORK" >/dev/null

docker compose config --quiet
docker compose pull caddy postgres mediamtx
docker run --rm \
    -e "CONDOTIFY_PUBLIC_HOST=$DOMAIN" \
    -v "$ROOT_DIR/deploy/proxy/config:/etc/caddy:ro" \
    caddy:2.11.4-alpine \
    caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile

sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow "$SSH_PORT/tcp" comment 'SSH custom'
sudo ufw allow 80/tcp comment 'Caddy HTTP and ACME'
sudo ufw allow 443/tcp comment 'Caddy HTTPS'
sudo ufw allow 443/udp comment 'Caddy HTTP/3'
sudo ufw allow 8189/udp comment 'Media WebRTC ICE'
sudo ufw --force enable

# O proxy web deve ser exclusivamente um container. O Nginx e o Certbot
# antigos ficam apenas parados para permitir rollback manual de uma migracao.
if systemctl list-unit-files nginx.service >/dev/null 2>&1; then
    sudo systemctl disable --now nginx || true
fi
if systemctl list-unit-files snap.certbot.renew.timer >/dev/null 2>&1; then
    sudo systemctl disable --now snap.certbot.renew.timer || true
fi

docker compose build api portal lpr-ocr
docker compose up -d postgres
docker compose --profile ops run --rm --no-deps migration
docker compose up -d --no-build --remove-orphans

api_ready=false
for _ in $(seq 1 60); do
    if curl -fsS --max-time 15 "https://$DOMAIN/health/ready" >/dev/null; then
        api_ready=true
        break
    fi
    sleep 3
done

if [[ "$api_ready" != true ]]; then
    docker compose ps
    docker compose logs --tail=150 caddy api
    echo 'A aplicacao nao ficou pronta via HTTPS dentro do tempo esperado.' >&2
    exit 3
fi

curl -fsS --max-time 15 "https://$DOMAIN/" >/dev/null
docker compose ps
df -h /
echo 'deployment_status=caddy_tls_enabled'
