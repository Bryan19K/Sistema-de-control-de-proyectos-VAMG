#!/usr/bin/env bash
# Deja TODO listo con un solo comando, desde la terminal del Codespace:
#   1. prepara el VPS (Docker, firewall, usuario deploy, configuración),
#   2. crea la llave SSH y la autoriza en el servidor,
#   3. carga los secretos en GitHub,
#   4. lanza la primera publicación.
#
#   bash <(curl -fsSL https://raw.githubusercontent.com/Bryan19K/Sistema-de-control-de-proyectos-VAMG/claude/desarrollo-inicial-5m8ya1/deploy/instalar-todo.sh)
#
# Solo pregunta el correo del administrador y la contraseña de root del VPS (una sola vez).
set -euo pipefail

REPO="Bryan19K/Sistema-de-control-de-proyectos-VAMG"
REF="${REF:-claude/desarrollo-inicial-5m8ya1}"
# SSH_HOST: a dónde se conecta (la IP no depende del DNS). DOMAIN: nombre para el HTTPS de la aplicación.
HOST="${SSH_HOST:-162.35.14.225}"
DOMAIN="${DOMAIN:-vps3795068.trouble-free.net}"
RAW="https://raw.githubusercontent.com/${REPO}/${REF}/deploy"

paso() { printf '\n\033[1;34m==> %s\033[0m\n' "$*"; }
aviso() { printf '\033[1;33m%s\033[0m\n' "$*"; }
fallo() { printf '\033[1;31m%s\033[0m\n' "$*"; exit 1; }

command -v ssh >/dev/null || fallo "No se encontró ssh."
command -v gh >/dev/null || fallo "No se encontró gh (la terminal del Codespace lo trae)."

WORK="$(mktemp -d)"
trap 'ssh -O exit -o ControlPath="$WORK/cm" "root@$HOST" >/dev/null 2>&1 || true; rm -rf "$WORK"' EXIT

paso "Datos"
read -r -p "Correo del administrador de la aplicación: " ADMIN_EMAIL < /dev/tty
[ -n "$ADMIN_EMAIL" ] || fallo "Hace falta un correo."
echo "Servidor: $HOST  ·  Dirección de la aplicación: https://$DOMAIN"

paso "1/5 Llave SSH para las publicaciones automáticas"
ssh-keygen -q -t ed25519 -N "" -C "deploy-tekcom" -f "$WORK/deploy_key"
PUBKEY="$(cat "$WORK/deploy_key.pub")"

# Una sola conexión reutilizada: la contraseña de root se pide una vez.
SSH_ROOT=(ssh -o StrictHostKeyChecking=accept-new -o ControlMaster=auto -o ControlPath="$WORK/cm" -o ControlPersist=15m "root@$HOST")

paso "2/5 Preparando el servidor (pedirá la contraseña de root una vez; no se ve al escribir)"
curl -fsSL "$RAW/setup-server.sh" -o "$WORK/setup-server.sh"
"${SSH_ROOT[@]}" "DOMAIN='$DOMAIN' SEED_ADMIN_EMAIL='$ADMIN_EMAIL' DEPLOY_PUBKEY='$PUBKEY' bash -s" \
  < "$WORK/setup-server.sh" | tee "$WORK/setup.log"

paso "3/5 Probando la llave en el servidor"
ssh -i "$WORK/deploy_key" -o StrictHostKeyChecking=accept-new -o IdentitiesOnly=yes "deploy@$HOST" 'echo "Conexión como deploy correcta"' \
  || fallo "No se pudo entrar como deploy con la llave."

paso "4/5 Cargando los secretos en GitHub"
# En Codespaces gh usa un token sin permiso para secretos; si falla, se pide autorizar una vez desde el navegador.
cargar_secretos() {
  gh secret set VPS_SSH_KEY -R "$REPO" < "$WORK/deploy_key" \
    && gh secret set VPS_HOST -R "$REPO" --body "$HOST" \
    && gh secret set VPS_USER -R "$REPO" --body "deploy"
}
if ! cargar_secretos 2>/dev/null; then
  aviso "gh no tiene permiso para guardar secretos. Autorice con su cuenta de GitHub:"
  aviso "se mostrará un código y una dirección; ábrala en el celular, escriba el código y acepte."
  env -u GITHUB_TOKEN -u GH_TOKEN gh auth login -h github.com -p https -s repo,workflow -w
  export GH_TOKEN
  GH_TOKEN="$(env -u GITHUB_TOKEN -u GH_TOKEN gh auth token)"
  unset GITHUB_TOKEN
  cargar_secretos || {
    aviso "No se pudieron cargar los secretos automáticamente. Cárguelos a mano en:"
    aviso "https://github.com/$REPO/settings/secrets/actions"
    aviso "VPS_HOST = $HOST    VPS_USER = deploy    VPS_SSH_KEY = (la llave de abajo, completa)"
    cat "$WORK/deploy_key"
    read -r -p "Cuando termine de cargarlos, presione Enter..." _ < /dev/tty
  }
fi
echo "Secretos cargados."

paso "5/5 Lanzando la primera publicación"
if gh workflow run deploy-vps.yml -R "$REPO" --ref main 2>/dev/null; then
  echo "Publicación iniciada. Siga el avance en: https://github.com/$REPO/actions"
else
  aviso "No se pudo lanzar automáticamente. Entre a https://github.com/$REPO/actions,"
  aviso "elija 'Publicar en el VPS' y presione 'Run workflow' (rama main)."
fi

ADMIN_PASSWORD="$(grep 'Contraseña inicial:' "$WORK/setup.log" | sed 's/.*Contraseña inicial: *//' | head -1)"
echo
echo "=============================================================="
echo " Listo. Cuando la publicación termine en verde (unos 5 a 10 minutos),"
echo " abra:  https://$DOMAIN"
echo " Usuario:    $ADMIN_EMAIL"
if [ -n "$ADMIN_PASSWORD" ]; then
  echo " Contraseña: $ADMIN_PASSWORD   (cámbiela al entrar)"
else
  echo " Contraseña: la ya existente; está en /opt/tekcom/.env del servidor."
fi
echo "=============================================================="
