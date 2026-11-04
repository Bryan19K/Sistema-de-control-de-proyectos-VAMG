# Despliegue en el VPS (InterServer)

La aplicación corre en un VPS con **Docker**: PostgreSQL, la aplicación y **Caddy** (que consigue solo el
certificado HTTPS). GitHub Actions publica cada cambio que llegue a `main`.

- Dirección: `https://vps3795068.trouble-free.net`
- Todo se puede hacer desde el celular usando la terminal del Codespace.

## Paso 1 · Aprobar el pull request

Abra el pull request en GitHub, revíselo y apruébelo. **Todavía no haga el merge**: primero se prepara el servidor.

## Paso 2 · Conectarse al VPS (desde la terminal del Codespace)

```bash
ssh root@TU_IP
```

Escriba `yes` y la contraseña de root (no se ve mientras se escribe). Si funciona, salga con `exit`.
Si el SSH no responde, el panel de InterServer tiene una consola web (VNC) que sirve igual.

## Paso 3 · Preparar el servidor (una sola vez)

En la terminal del Codespace, ya dentro de la carpeta del repositorio y en la rama del pull request:

```bash
ssh root@TU_IP 'bash -s' < deploy/setup-server.sh
```

Para poner su correo como administrador, anteponga la variable:

```bash
ssh root@TU_IP 'SEED_ADMIN_EMAIL=su@correo.com bash -s' < deploy/setup-server.sh
```

El script instala Docker, activa el firewall (SSH, 80 y 443), crea el usuario `deploy`, deja preparado
`/opt/tekcom/.env` y programa un respaldo diario. Al final **muestra una sola vez**:

- la contraseña temporal del usuario `deploy` (se usa en el paso 4), y
- el correo y la contraseña inicial del administrador de la aplicación. **Guárdelos.**

Es seguro volver a correrlo: no cambia contraseñas ni sobrescribe el `.env`.
(Una vez que el código esté en `main` también sirve:
`curl -fsSL https://raw.githubusercontent.com/Bryan19K/Sistema-de-control-de-proyectos-VAMG/main/deploy/setup-server.sh | bash` desde el propio servidor.)

## Paso 4 · Llave para que GitHub publique solo

En la terminal del Codespace (mejor fuera de la carpeta del repositorio, por ejemplo en `/tmp`):

```bash
cd /tmp
ssh-keygen -t ed25519 -f deploy_key -N ""
ssh-copy-id -i deploy_key.pub deploy@TU_IP      # pide la contraseña temporal del paso 3
gh secret set VPS_SSH_KEY -R Bryan19K/Sistema-de-control-de-proyectos-VAMG < deploy_key
gh secret set VPS_HOST    -R Bryan19K/Sistema-de-control-de-proyectos-VAMG --body "TU_IP"
gh secret set VPS_USER    -R Bryan19K/Sistema-de-control-de-proyectos-VAMG --body "deploy"
rm deploy_key deploy_key.pub
```

Si `gh secret set` da error de permisos, cree los tres secretos en el navegador:
repositorio → **Settings → Secrets and variables → Actions → New repository secret**
(`VPS_SSH_KEY` con el contenido de `deploy_key`, `VPS_HOST` con la IP y `VPS_USER` con `deploy`).
Para ver esa pantalla completa en el celular, active "Sitio de escritorio" en Chrome.

Opcional, cuando la llave ya funcione: bloquee la contraseña temporal con
`ssh root@TU_IP 'passwd -l deploy'`.

## Paso 5 · Publicar

Haga el **merge del pull request a `main`**. En la pestaña **Actions** verá el despliegue:
compila, prueba, construye la imagen, la sube a `ghcr.io` y la instala en el servidor.
Cuando termine en verde, abra `https://vps3795068.trouble-free.net` e inicie sesión con el administrador del paso 3.

La primera vez Caddy tarda unos segundos en conseguir el certificado. Desde entonces, cada merge a `main` se publica solo.

## Cómo ajustar la configuración

Se edita en el servidor, en `/opt/tekcom/.env` (`ssh deploy@TU_IP`, luego `nano /opt/tekcom/.env`), y se aplica con:

```bash
cd /opt/tekcom && docker compose up -d
```

- **Correo:** complete `EMAIL_HOST`, `EMAIL_PORT`, `EMAIL_USER`, `EMAIL_PASSWORD`, `EMAIL_FROM`.
- **WhatsApp:** complete `WHATSAPP_PHONE_NUMBER_ID`, `WHATSAPP_ACCESS_TOKEN` y, si aplica, `WHATSAPP_TEMPLATE_NAME`.
- Después, en la aplicación: **Configuración → Notificaciones** muestra el estado y permite enviar una prueba.

## Mantenimiento

| Qué | Comando (en el servidor, como `deploy`) |
|---|---|
| Ver registros | `cd /opt/tekcom && docker compose logs -f --tail 100 web` |
| Estado | `cd /opt/tekcom && docker compose ps` |
| Reiniciar | `cd /opt/tekcom && docker compose restart web` |
| Respaldo manual | `/opt/tekcom/backup.sh` |

**Respaldos:** cada día a las 3:00 se guardan en `/opt/tekcom/backups` la base de datos y los archivos subidos
(14 días). Están en el mismo servidor, así que conviene además activar las copias (snapshots) del VPS en InterServer
o descargar los respaldos de vez en cuando.

**Restaurar la base:** `gunzip -c backups/tekcom-db-FECHA.sql.gz | docker compose exec -T db psql -U tekcom tekcom`
(sobre una base vacía).

## Si algo falla

- **El workflow falla en "Verificar secretos":** falta alguno de `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY` (paso 4).
- **Falla el SSH desde Actions:** la llave no quedó autorizada; repita `ssh-copy-id` del paso 4.
- **El sitio no abre con HTTPS:** el dominio debe apuntar a la IP del VPS y los puertos 80 y 443 estar abiertos
  (también en el firewall del panel de InterServer, si tiene uno). Revise `docker compose logs caddy`.
- **Datos locales de pruebas:** la base del servidor es independiente de cualquier base local.
