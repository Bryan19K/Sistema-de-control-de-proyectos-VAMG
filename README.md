# Sistema de Gestión de Proyectos TEK-COM

Sistema web que reemplaza el control en múltiples Excel de los proyectos de TEK-COM: proyectos,
plan de trabajo, sprints, tareas, avance por sitio, materiales, bodega, proyección de diseño,
liquidación, equipos y documentación. Los Excel no desaparecen: se generan desde el sistema.

El plan completo está en [`docs/INICIO_PROYECTO.md`](docs/INICIO_PROYECTO.md).
Las decisiones técnicas se registran en [`docs/decisiones/`](docs/decisiones/).
El flujo operativo por nodo (etapas de la ODN y feeder) está en [`docs/FLUJO_OPERATIVO.md`](docs/FLUJO_OPERATIVO.md).
El diseño de referencia de la interfaz (mockups y guía de estilos) está en [`docs/diseno/`](docs/diseno/);
la página **Diseño** de la aplicación (solo Admin) muestra esas pantallas con datos de ejemplo.

## Tecnología

.NET 10 · Blazor Web App (render interactivo en servidor) · MudBlazor · EF Core 10 · PostgreSQL ·
ASP.NET Core Identity · xUnit + FluentAssertions.

## Estructura

```
src/
  Tekcom.Gestion.Domain/          Entidades, enums y reglas de negocio puras
  Tekcom.Gestion.Application/     Servicios, DTOs, validaciones e interfaces
  Tekcom.Gestion.Infrastructure/  EF Core, migraciones, Identity, auditoría, importador y exportes Excel/PDF, avisos (correo, WhatsApp) y Hangfire
  Tekcom.Gestion.Web/             Blazor + MudBlazor, páginas y componentes
tests/
  Tekcom.Gestion.Domain.Tests/
  Tekcom.Gestion.Application.Tests/
docs/                              Plan, ADR, diseño de referencia, logo y Excel de origen
.github/workflows/                 CI (build + pruebas + imagen Docker en cada PR) y publicación al VPS desde main
```

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- PostgreSQL 15 o superior, por ejemplo con Docker:

  ```bash
  docker run -e POSTGRES_PASSWORD=TuPassw0rd -e POSTGRES_DB=tekcom -p 5432:5432 --name tekcom-db -d postgres:17-alpine
  ```

- Visual Studio 2022/2026 o VS Code con la extensión C# Dev Kit (opcional)

## Verlo sin instalar nada (GitHub Codespaces)

El repositorio trae un Dev Container con .NET 10 y PostgreSQL listos. Pasos en
[`.devcontainer/README.md`](.devcontainer/README.md).

## Correr en local

1. **Clonar y restaurar**

   ```bash
   git clone https://github.com/Bryan19K/Sistema-de-control-de-proyectos-VAMG.git
   cd Sistema-de-control-de-proyectos-VAMG
   dotnet restore
   ```

2. **Configurar secretos** (nunca van al repositorio). Desde la carpeta raíz:

   ```bash
   cd src/Tekcom.Gestion.Web

   # Cadena de conexión. Ejemplos:
   #   Docker (arriba):  "Host=localhost;Database=tekcom;Username=postgres;Password=TuPassw0rd"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=tekcom;Username=postgres;Password=TuPassw0rd"

   # Usuario administrador inicial (se crea al arrancar si no existe)
   dotnet user-secrets set "SeedAdmin:Email" "bryan@tekcom.com"
   dotnet user-secrets set "SeedAdmin:FullName" "Bryan Calderón"
   dotnet user-secrets set "SeedAdmin:Password" "UnaContraseñaSegura1!"
   ```

   La contraseña debe tener al menos 8 caracteres, mayúscula, minúscula, número y símbolo.
   `appsettings.Example.json` documenta todas las claves disponibles.

3. **Ejecutar**

   ```bash
   dotnet run --project src/Tekcom.Gestion.Web
   ```

   Al arrancar, la aplicación aplica las migraciones pendientes (crea la base si no existe),
   crea los roles (`Admin`, `Bodega`, `Supervisor`, `Gerencia`, `Contratista`) y el usuario
   administrador. Abrir la URL que muestra la consola (por defecto `https://localhost:7246`)
   e iniciar sesión con el correo y contraseña configurados.

   **Sin PostgreSQL (solo para probar):** se puede usar SQLite con
   `dotnet user-secrets set "Database:Provider" "Sqlite"` y
   `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "DataSource=tekcom.db"`.
   En producción siempre se usa PostgreSQL con migraciones.

4. **Compilar y probar**

   ```bash
   dotnet build
   dotnet test
   ```

## Publicación

El despliegue al VPS (Docker, PostgreSQL y Caddy con HTTPS) es automático con cada merge a `main`.
Los pasos para preparar el servidor y conectar GitHub están en [`docs/DESPLIEGUE.md`](docs/DESPLIEGUE.md).

## Notificaciones (correo y WhatsApp)

Los avisos salen por dos canales que se activan solos cuando se configuran; mientras no lo estén, no envían nada:

| Canal | Variables (user-secrets en local, archivo `/opt/tekcom/.env` en el servidor) |
|---|---|
| Correo (SMTP) | `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__FromAddress`, `Email__UseSsl` |
| WhatsApp Business Cloud API | `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`, `WhatsApp__TemplateName` (plantilla aprobada por Meta con un parámetro de cuerpo; sin plantilla solo se puede escribir dentro de las 24 h de una conversación abierta por el usuario) |

- Los trabajos corren con Hangfire: revisión cada hora de 7:00 a 18:00 y resumen diario a las 7:00 (hora de El Salvador, ajustable con `Hangfire__ChecksCron`, `Hangfire__SummaryCron` y `Hangfire__TimeZone`). En PostgreSQL la cola vive en el esquema `hangfire`; con SQLite (solo desarrollo) vive en memoria.
- Panel de trabajos en `/hangfire`, solo para administradores. `Hangfire__Enabled=false` desactiva los trabajos.
- Qué se avisa y por qué canal se cambia en **Configuración → Notificaciones**, donde también está la bitácora y el botón de prueba. Los teléfonos para WhatsApp se registran por usuario en **Configuración → Usuarios**.
- Un mismo aviso no se repite el mismo día al mismo destinatario.

## Migraciones de base de datos

Las migraciones viven en `src/Tekcom.Gestion.Infrastructure/Persistence/Migrations`.
Para crear una nueva después de cambiar entidades:

```bash
dotnet tool install -g dotnet-ef        # una sola vez
dotnet ef migrations add NombreDescriptivo \
  --project src/Tekcom.Gestion.Infrastructure \
  --startup-project src/Tekcom.Gestion.Web \
  --output-dir Persistence/Migrations
```

Se aplican solas al arrancar la aplicación. Para desactivarlo (por ejemplo si se aplican desde
CI) poner `Database:ApplyMigrationsOnStartup` en `false`.

## Principios que el código respeta

1. Un dato, un lugar.
2. El stock no se guarda, se calcula a partir de movimientos.
3. Nada se borra: `Remove()` sobre una entidad se convierte en borrado lógico (`IsDeleted`) y
   un filtro global oculta esos registros en todas las consultas.
4. Toda entidad guarda quién y cuándo la creó o modificó (`CreatedAt/By`, `UpdatedAt/By`); lo
   llena automáticamente un interceptor de EF Core.
5. Entregas y su efecto en stock se guardan en una sola transacción.
6. Sin stock negativo sin justificación explícita.
7. Todo exporte lleva encabezado fijo con logo TEK-COM, proyecto, fecha de corte y "Elaborado por".
8. Módulos según tipo de proyecto.
9. Interfaz en español; código en inglés (ver [ADR-001](docs/decisiones/ADR-001-convencion-de-idioma.md)).

## Flujo de trabajo

- Un sprint a la vez; rama `feature/sprint-N-nombre`, commits pequeños en español, pull request
  por sprint revisado por Bryan. Nunca commit directo a `main`.
- `dotnet build` y `dotnet test` en verde antes de abrir el PR (el CI lo verifica).
- Secretos nunca en el repositorio.
