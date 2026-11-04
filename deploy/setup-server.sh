#!/usr/bin/env bash
# Prepara un servidor Ubuntu/Debian recién creado para TEK-COM Gestión. Se corre UNA vez, como root.
#
#   Desde el Codespace:
#     ssh root@IP 'bash -s' < deploy/setup-server.sh
#   o directamente en el servidor:
#     curl -fsSL https://raw.githubusercontent.com/Bryan19K/Sistema-de-control-de-proyectos-VAMG/main/deploy/setup-server.sh | bash
#
# Variables opcionales (se anteponen al comando):
#   DOMAIN=vps3795068.trouble-free.net   dominio que apunta al servidor (HTTPS automático)
#   SEED_ADMIN_EMAIL=correo@empresa.com  correo del primer administrador
#   DEPLOY_PUBKEY="ssh-ed25519 AAAA..."  llave pública para el usuario deploy (si se omite, se crea una contraseña temporal)
#
# Es seguro volver a correrlo: no cambia contraseñas ni sobrescribe el archivo .env existente.
set -euo pipefail

DOMAIN="${DOMAIN:-vps3795068.trouble-free.net}"
SEED_ADMIN_EMAIL="${SEED_ADMIN_EMAIL:-admin@tekcom.com}"
APP_DIR=/opt/tekcom

[ "$(id -u)" -eq 0 ] || { echo "Este script debe correr como root."; exit 1; }
command -v apt-get >/dev/null || { echo "Solo se soporta Ubuntu/Debian (no se encontró apt-get)."; exit 1; }
export DEBIAN_FRONTEND=noninteractive

echo "==> 1/6 Paquetes base"
apt-get update -y
apt-get install -y ca-certificates curl ufw openssl cron unattended-upgrades

echo "==> 2/6 Docker"
if ! command -v docker >/dev/null; then
  curl -fsSL https://get.docker.com | sh
fi
systemctl enable --now docker
docker compose version >/dev/null

echo "==> 3/6 Firewall (SSH, HTTP y HTTPS)"
SSH_PORT="$(sshd -T 2>/dev/null | awk '/^port /{print $2; exit}')"
SSH_PORT="${SSH_PORT:-22}"
ufw allow "${SSH_PORT}/tcp"
ufw allow 80/tcp
ufw allow 443/tcp
ufw allow 443/udp
ufw --force enable

echo "==> 4/6 Usuario deploy y carpeta de la aplicación"
TEMP_PASSWORD=""
if ! id deploy >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash deploy
  if [ -z "${DEPLOY_PUBKEY:-}" ]; then
    TEMP_PASSWORD="$(openssl rand -hex 8)"
    echo "deploy:${TEMP_PASSWORD}" | chpasswd
  fi
fi
usermod -aG docker deploy
if [ -n "${DEPLOY_PUBKEY:-}" ]; then
  install -d -m 700 -o deploy -g deploy /home/deploy/.ssh
  grep -qxF "$DEPLOY_PUBKEY" /home/deploy/.ssh/authorized_keys 2>/dev/null || echo "$DEPLOY_PUBKEY" >> /home/deploy/.ssh/authorized_keys
  chown deploy:deploy /home/deploy/.ssh/authorized_keys
  chmod 600 /home/deploy/.ssh/authorized_keys
fi
install -d -m 755 -o deploy -g deploy "$APP_DIR" "$APP_DIR/backups"

echo "==> 5/6 Configuración (.env)"
CREDENTIALS=""
if [ ! -f "$APP_DIR/.env" ]; then
  DB_PASSWORD="$(openssl rand -hex 24)"
  ADMIN_PASSWORD="Tk-$(openssl rand -hex 6)-Aa1!"
  cat > "$APP_DIR/.env" <<ENV
# Configuración del servidor. NO se sube al repositorio. Editar y luego: cd /opt/tekcom && docker compose up -d
DOMAIN=${DOMAIN}
POSTGRES_PASSWORD=${DB_PASSWORD}
SEED_ADMIN_EMAIL=${SEED_ADMIN_EMAIL}
SEED_ADMIN_FULLNAME=Administrador
SEED_ADMIN_PASSWORD=${ADMIN_PASSWORD}
TIMEZONE=America/El_Salvador

# Correo saliente (opcional): completar para activar los avisos por correo.
EMAIL_HOST=
EMAIL_PORT=587
EMAIL_USER=
EMAIL_PASSWORD=
EMAIL_FROM=
EMAIL_USE_SSL=false

# WhatsApp Business Cloud API (opcional).
WHATSAPP_PHONE_NUMBER_ID=
WHATSAPP_ACCESS_TOKEN=
WHATSAPP_TEMPLATE_NAME=
ENV
  chown deploy:deploy "$APP_DIR/.env"
  chmod 600 "$APP_DIR/.env"
  CREDENTIALS="yes"
else
  echo "Ya existe $APP_DIR/.env: no se modifica."
fi

echo "==> 6/6 Respaldo diario a las 3:00"
cat > /etc/cron.d/tekcom-backup <<'CRON'
0 3 * * * deploy /opt/tekcom/backup.sh >> /opt/tekcom/backups/backup.log 2>&1
CRON
chmod 644 /etc/cron.d/tekcom-backup

echo
echo "=============================================================="
echo " Servidor listo."
echo "   Dominio:            https://${DOMAIN}"
if [ -n "$TEMP_PASSWORD" ]; then
  echo "   Usuario deploy:     deploy  (contraseña temporal: ${TEMP_PASSWORD})"
  echo "   Úsela una sola vez con ssh-copy-id (docs/DESPLIEGUE.md, paso 4)."
fi
if [ -n "$CREDENTIALS" ]; then
  echo "   Administrador:      ${SEED_ADMIN_EMAIL}"
  echo "   Contraseña inicial: ${ADMIN_PASSWORD}"
  echo "   Guárdela ahora y cámbiela al entrar. Queda también en ${APP_DIR}/.env"
fi
echo "=============================================================="
