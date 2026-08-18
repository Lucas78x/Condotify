#!/usr/bin/env bash
set -Eeuo pipefail

DOMAIN="${1:-fefaccess.grupoff.net.br}"
SSH_PORT="${2:-22}"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

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
CONDOTIFY_MEDIA_HLS_BASEURL=https://$DOMAIN:8888
CONDOTIFY_MEDIA_WEBRTC_BASEURL=https://$DOMAIN:8889
CONDOTIFY_MEDIA_WEBRTC_HOSTS=$DOMAIN
CONDOTIFY_MEDIA_MAX_VIEWERS_PER_LICENSE=24
CONDOTIFY_MEDIA_TRANSCODE_AUDIO=true
CONDOTIFY_SMTP_HOST=
CONDOTIFY_SMTP_PORT=587
CONDOTIFY_SMTP_USERNAME=
CONDOTIFY_SMTP_PASSWORD=
CONDOTIFY_SMTP_FROM_EMAIL=
CONDOTIFY_SMTP_FROM_NAME=F&F Access
CONDOTIFY_SMTP_ENABLE_SSL=true
EOF
fi

# Corrige a configuracao criada por uma execucao interrompida com o host antigo.
sed -i 's/fefacess\.grupoff\.com/fefaccess.grupoff.net.br/g' .env

chmod 600 .env
sudo install -d -m 0750 -o 1654 -g 1654 "$ROOT_DIR/backups"
sudo install -d -m 0755 /var/www/certbot
sudo install -d -m 0755 /var/www/certbot/.well-known/acme-challenge

sudo apt-get update
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y nginx ufw curl ca-certificates openssl snapd
sudo apt-get clean
sudo systemctl enable --now snapd.socket

if ! snap list certbot >/dev/null 2>&1; then
    sudo snap install --classic certbot
fi
sudo ln -sf /snap/bin/certbot /usr/local/bin/certbot

sudo rm -f /etc/nginx/sites-enabled/default
sudo rm -f /etc/nginx/sites-enabled/fefacess.grupoff.com
sudo rm -f /etc/nginx/sites-available/fefacess.grupoff.com
sudo install -m 0644 deploy/nginx/fefaccess.bootstrap.conf /etc/nginx/sites-available/fefaccess.grupoff.net.br
sudo ln -sfn /etc/nginx/sites-available/fefaccess.grupoff.net.br /etc/nginx/sites-enabled/fefaccess.grupoff.net.br
sudo nginx -t
sudo systemctl enable --now nginx
sudo systemctl reload nginx

sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow "$SSH_PORT/tcp" comment 'SSH custom'
sudo ufw allow 80/tcp comment 'HTTP and ACME'
sudo ufw allow 443/tcp comment 'HTTPS portal and API'
sudo ufw allow 8888/tcp comment 'Media HLS TLS'
sudo ufw allow 8889/tcp comment 'Media WebRTC signaling TLS'
sudo ufw allow 8189/udp comment 'Media WebRTC ICE'
sudo ufw --force enable

docker compose config --quiet
docker compose up -d --build --remove-orphans

api_ready=false
for _ in $(seq 1 60); do
    if curl -fsS \
        -H "Host: $DOMAIN" \
        -H 'X-Forwarded-Proto: https' \
        -H "X-Forwarded-Host: $DOMAIN" \
        http://127.0.0.1:7118/health/ready >/dev/null; then
        api_ready=true
        break
    fi
    sleep 3
done

if [[ "$api_ready" != true ]]; then
    docker compose ps
    docker compose logs --tail=100 api
    echo 'A API nao ficou pronta dentro do tempo esperado.' >&2
    exit 3
fi

curl -fsS \
    -H "Host: $DOMAIN" \
    -H 'X-Forwarded-Proto: https' \
    -H "X-Forwarded-Host: $DOMAIN" \
    http://127.0.0.1:5035/ >/dev/null

challenge_token="condotify-$(openssl rand -hex 12)"
challenge_path="/var/www/certbot/.well-known/acme-challenge/$challenge_token"
printf '%s' "$challenge_token" | sudo tee "$challenge_path" >/dev/null
public_challenge="$(curl -fsS --max-time 15 "http://$DOMAIN/.well-known/acme-challenge/$challenge_token" 2>/dev/null || true)"
sudo rm -f "$challenge_path"

if [[ "$public_challenge" == "$challenge_token" ]]; then
    sudo certbot certonly \
        --webroot \
        --webroot-path /var/www/certbot \
        --domain "$DOMAIN" \
        --non-interactive \
        --agree-tos \
        --register-unsafely-without-email \
        --deploy-hook 'systemctl reload nginx'

    sudo install -m 0644 deploy/nginx/fefaccess.grupoff.net.br.conf /etc/nginx/sites-available/fefaccess.grupoff.net.br
    sudo nginx -t
    sudo systemctl reload nginx
    sudo certbot renew --dry-run
    echo 'deployment_status=tls_enabled'
else
    echo 'deployment_status=waiting_for_dns'
fi

docker compose ps
df -h /
