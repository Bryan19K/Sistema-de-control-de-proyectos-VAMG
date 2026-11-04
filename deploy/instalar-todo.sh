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

# Este Codespace no trae la herramienta gh de GitHub: se baja (sin permisos de administrador) a ~/.local/bin.
if ! command -v gh >/dev/null; then
  paso "Instalando la herramienta gh de GitHub"
  GH_VERSION=2.63.2
  case "$(uname -m)" in
    x86_64) GH_ARCH=amd64 ;;
    aarch64|arm64) GH_ARCH=arm64 ;;
    *) fallo "Arquitectura no soportada: $(uname -m)" ;;
  esac
  GH_TMP="$(mktemp -d)"
  curl -fsSL "https://github.com/cli/cli/releases/download/v${GH_VERSION}/gh_${GH_VERSION}_linux_${GH_ARCH}.tar.gz" -o "$GH_TMP/gh.tar.gz" \
    || fallo "No se pudo descargar gh."
  tar -xzf "$GH_TMP/gh.tar.gz" -C "$GH_TMP"
  mkdir -p "$HOME/.local/bin"
  cp "$GH_TMP/gh_${GH_VERSION}_linux_${GH_ARCH}/bin/gh" "$HOME/.local/bin/gh"
  rm -rf "$GH_TMP"
  export PATH="$HOME/.local/bin:$PATH"
  gh --version | head -1
fi

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
SSH_USER=deploy
PROBAR=(ssh -i "$WORK/deploy_key" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o IdentitiesOnly=yes "deploy@$HOST" true)
if ! "${PROBAR[@]}" 2>/dev/null; then
  aviso "La llave no fue aceptada a la primera. Reescribiendo la llave en el servidor y reintentando..."
  "${SSH_ROOT[@]}" "install -d -m 700 -o deploy -g deploy /home/deploy/.ssh \
    && printf '%s\n' '$PUBKEY' > /home/deploy/.ssh/authorized_keys \
    && chown -R deploy:deploy /home/deploy/.ssh && chmod 600 /home/deploy/.ssh/authorized_keys && chmod 755 /home/deploy \
    && (passwd -S deploy | awk '{exit !(\$2==\"L\" || \$2==\"LK\")}' && echo \"deploy:\$(openssl rand -hex 24)\" | chpasswd || true) \
    && (systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || true)"
  sleep 2
  if ! "${PROBAR[@]}" 2>/dev/null; then
    aviso "El servidor no deja entrar al usuario deploy por SSH. Plan B: se usará root con la misma llave."
    echo "--- Nota técnica (por si hace falta): registro del servidor sobre deploy ---"
    "${SSH_ROOT[@]}" 'journalctl -u ssh -u sshd -n 400 --no-pager 2>/dev/null | grep -i deploy | tail -4 | cut -c1-200; echo "cuenta: $(passwd -S deploy 2>&1)"; chage -l deploy 2>&1 | head -3' || true
    echo "-----------------------------------------------------------------------"
    "${SSH_ROOT[@]}" "install -d -m 700 /root/.ssh && (grep -qxF '$PUBKEY' /root/.ssh/authorized_keys 2>/dev/null || printf '%s\n' '$PUBKEY' >> /root/.ssh/authorized_keys) && chmod 600 /root/.ssh/authorized_keys"
    SSH_USER=root
    PROBAR=(ssh -i "$WORK/deploy_key" -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o IdentitiesOnly=yes "root@$HOST" true)
    if ! "${PROBAR[@]}" 2>/dev/null; then
      aviso "Tampoco entró root con la llave. Levantando un SSH de diagnóstico en el servidor..."
      "${SSH_ROOT[@]}" 'ufw allow 2222/tcp >/dev/null 2>&1; (timeout 25 /usr/sbin/sshd -D -d -p 2222 -f /etc/ssh/sshd_config > /tmp/sshd-debug.log 2>&1 &); sleep 2' || true
      echo "--- Lado cliente ---"
      ssh -v -p 2222 -i "$WORK/deploy_key" -o BatchMode=yes -o IdentitiesOnly=yes -o StrictHostKeyChecking=accept-new "root@$HOST" true 2>&1 \
        | grep -Ei "Load key|sign_and_send|signing failed|Offering|accepts key|Authentications that can|no more auth|Permission denied|identity file" | cut -c1-170 | head -8 || true
      sleep 2
      echo "--- Lado servidor ---"
      "${SSH_ROOT[@]}" 'grep -Ei "key|auth|fail|error|refus|denied|not allowed|pam|match|sig" /tmp/sshd-debug.log | grep -vi "debug3\|kex\|cipher\|compress\|mac:" | tail -14 | cut -c1-170; ufw delete allow 2222/tcp >/dev/null 2>&1; true' || true
      echo "--------------------"
      fallo "No se pudo autorizar la llave en el servidor. Mande captura de lo de arriba (cliente y servidor)."
    fi
  fi
fi
echo "Conexión por SSH con la llave correcta (usuario: $SSH_USER)"

paso "4/5 Cargando los secretos en GitHub"
# En Codespaces gh usa un token sin permiso para secretos; si falla, se pide autorizar una vez desde el navegador.
cargar_secretos() {
  gh secret set VPS_SSH_KEY -R "$REPO" < "$WORK/deploy_key" \
    && gh secret set VPS_HOST -R "$REPO" --body "$HOST" \
    && gh secret set VPS_USER -R "$REPO" --body "$SSH_USER"
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
    aviso "VPS_HOST = $HOST    VPS_USER = $SSH_USER    VPS_SSH_KEY = (la llave de abajo, completa)"
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
