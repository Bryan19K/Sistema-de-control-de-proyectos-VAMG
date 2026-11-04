#!/usr/bin/env bash
# Lo ejecuta GitHub Actions por SSH en el servidor (como usuario deploy) en cada publicación.
#   Uso:  echo "$TOKEN" | bash /opt/tekcom/deploy.sh <imagen> <usuario-ghcr>
# El token de GitHub llega por la entrada estándar para que no quede en la lista de procesos.
set -euo pipefail

IMAGE="${1:?falta la imagen}"
GHCR_USER="${2:?falta el usuario de ghcr.io}"
cd /opt/tekcom

[ -f .env ] || { echo "Falta /opt/tekcom/.env. Corra primero deploy/setup-server.sh (ver docs/DESPLIEGUE.md)."; exit 1; }

# Registra la imagen a publicar en .env (conserva el resto de valores).
if grep -q '^WEB_IMAGE=' .env; then
  sed -i "s|^WEB_IMAGE=.*|WEB_IMAGE=${IMAGE}|" .env
else
  echo "WEB_IMAGE=${IMAGE}" >> .env
fi

echo "==> Descargando ${IMAGE}"
docker login ghcr.io -u "$GHCR_USER" --password-stdin
trap 'docker logout ghcr.io >/dev/null 2>&1 || true' EXIT
docker compose pull web
docker compose up -d --remove-orphans

DOMAIN="$(grep '^DOMAIN=' .env | cut -d= -f2-)"
echo "==> Esperando a que la aplicación responda (${DOMAIN})"
for i in $(seq 1 40); do
  if curl -fsSk --max-time 5 --resolve "${DOMAIN}:443:127.0.0.1" "https://${DOMAIN}/healthz" >/dev/null 2>&1; then
    echo "La aplicación respondió correctamente."
    docker image prune -f >/dev/null 2>&1 || true
    exit 0
  fi
  sleep 3
done

echo "La aplicación no respondió a tiempo. Últimos registros:"
docker compose logs --tail 80 web caddy || true
exit 1
