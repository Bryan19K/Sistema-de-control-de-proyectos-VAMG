#!/usr/bin/env bash
# Respaldo diario: base de datos (pg_dump) y archivos subidos. Conserva 14 días.
# Lo programa setup-server.sh en /etc/cron.d/tekcom-backup (corre a las 3:00).
# OJO: los respaldos quedan en el mismo servidor; conviene copiarlos fuera (ver docs/DESPLIEGUE.md).
set -euo pipefail
cd /opt/tekcom
mkdir -p backups
STAMP="$(date +%Y%m%d-%H%M)"

docker compose exec -T db pg_dump -U tekcom tekcom | gzip > "backups/tekcom-db-${STAMP}.sql.gz"
docker run --rm -v tekcom_appdata:/data:ro -v /opt/tekcom/backups:/backup alpine \
  tar czf "/backup/tekcom-archivos-${STAMP}.tar.gz" -C /data uploads

find backups -type f -mtime +14 -delete
echo "Respaldo ${STAMP} listo en /opt/tekcom/backups"
