# ADR-003 · Despliegue en VPS con PostgreSQL y Docker

**Estado:** aceptada.

## Contexto

El plan original publicaba en Azure App Service con Azure SQL. TEK-COM ya cuenta con un VPS en InterServer.
Además, el plan gratuito de App Service se duerme y no corre los trabajos programados (avisos).

## Decisión

- **Base de datos:** PostgreSQL (Npgsql) en lugar de SQL Server. SQL Server nunca llegó a producción, por lo que
  se regeneró una sola migración inicial `InitialCreate` para PostgreSQL. SQLite sigue solo para desarrollo y pruebas.
- **Trabajos programados:** Hangfire con almacenamiento en PostgreSQL (`Hangfire.PostgreSql`, esquema `hangfire`).
- **Despliegue:** Docker Compose (PostgreSQL + aplicación + Caddy para HTTPS automático) en el VPS.
- **Publicación:** GitHub Actions construye la imagen, la sube a `ghcr.io` y la despliega por SSH en cada merge a `main`.
- Los secretos viven en `/opt/tekcom/.env` del servidor y en los secretos del repositorio, nunca en el código.

## Consecuencias

- La aplicación está siempre encendida: los avisos programados corren a su hora sin costo extra de plataforma.
- TEK-COM administra el servidor (actualizaciones del sistema, respaldos fuera del VPS). Ver `docs/DESPLIEGUE.md`.
- Licencias nuevas: Npgsql (PostgreSQL License), Hangfire.PostgreSql (Apache 2.0).
