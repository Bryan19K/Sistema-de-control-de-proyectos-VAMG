# Sistema de Gestión de Proyectos TEK-COM

Sistema web que reemplaza el control en múltiples Excel de los proyectos de TEK-COM: proyectos,
plan de trabajo, sprints, tareas, avance por sitio, materiales, bodega, proyección de diseño,
liquidación, equipos y documentación. Los Excel no desaparecen: se generan desde el sistema.

El plan completo está en [`docs/INICIO_PROYECTO.md`](docs/INICIO_PROYECTO.md).
Las decisiones técnicas se registran en [`docs/decisiones/`](docs/decisiones/).
El diseño de referencia de la interfaz (mockups y guía de estilos) está en [`docs/diseno/`](docs/diseno/);
la página **Diseño** de la aplicación (solo Admin) muestra esas pantallas con datos de ejemplo.

## Tecnología

.NET 10 · Blazor Web App (render interactivo en servidor) · MudBlazor · EF Core 10 · SQL Server ·
ASP.NET Core Identity · xUnit + FluentAssertions.

## Estructura

```
src/
  Tekcom.Gestion.Domain/          Entidades, enums y reglas de negocio puras
  Tekcom.Gestion.Application/     Servicios, DTOs, validaciones e interfaces
  Tekcom.Gestion.Infrastructure/  EF Core, migraciones, Identity, auditoría (luego Excel, PDF, WhatsApp, correo)
  Tekcom.Gestion.Web/             Blazor + MudBlazor, páginas y componentes
tests/
  Tekcom.Gestion.Domain.Tests/
  Tekcom.Gestion.Application.Tests/
docs/                              Plan, ADR, diseño de referencia, logo y Excel de origen
.github/workflows/                 CI: build + pruebas en cada PR
```

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server: **SQL Server Express**, LocalDB (viene con Visual Studio) o un contenedor Docker:

  ```bash
  docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=TuPassw0rd!" -p 1433:1433 --name tekcom-sql -d mcr.microsoft.com/mssql/server:2022-latest
  ```

- Visual Studio 2022/2026 o VS Code con la extensión C# Dev Kit (opcional)

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
   #   SQL Server Express:  "Server=localhost\\SQLEXPRESS;Database=TekcomGestion;Trusted_Connection=True;TrustServerCertificate=True"
   #   LocalDB:             "Server=(localdb)\\mssqllocaldb;Database=TekcomGestion;Trusted_Connection=True"
   #   Docker:              "Server=localhost,1433;Database=TekcomGestion;User Id=sa;Password=TuPassw0rd!;TrustServerCertificate=True"
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost\\SQLEXPRESS;Database=TekcomGestion;Trusted_Connection=True;TrustServerCertificate=True"

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

4. **Compilar y probar**

   ```bash
   dotnet build
   dotnet test
   ```

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
