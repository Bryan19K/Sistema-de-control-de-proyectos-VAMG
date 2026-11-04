# Publicación en Azure

La aplicación se publica en **Azure App Service** con base de datos **Azure SQL**. El despliegue es
automático desde `main` con GitHub Actions (`.github/workflows/deploy-azure.yml`) y solo corre si el
repositorio tiene configurada la variable `AZURE_WEBAPP_NAME`.

Todo lo de esta guía lo hace una persona con acceso a la suscripción de Azure: el código no puede crear
recursos ni guardar credenciales por sí mismo.

## 1. Crear los recursos

1. **Grupo de recursos**, por ejemplo `tekcom-gestion`, en la región más cercana (por ejemplo, Central US o East US 2).
2. **Azure SQL Database**: use la oferta gratuita de Azure SQL Database si su suscripción la tiene disponible; si no, el nivel Basic.
   Cree el servidor con autenticación SQL, y en *Redes* active "Permitir que los servicios de Azure accedan al servidor".
3. **App Service** (Linux, runtime .NET 10). Plan:
   - **F1 (gratis)** sirve para probar, pero **se duerme tras unos minutos sin visitas y no tiene "Always On"**.
     Mientras está dormida **no corren los trabajos programados** (revisión horaria de avisos y resumen diario de las 7:00).
   - Para que los avisos salgan a su hora hay que usar **B1 o superior** con **Always On** activado
     (App Service > Configuración > Configuración general). Es una decisión de costo que debe tomar TEK-COM.

## 2. Configuración de la aplicación

En App Service > *Variables de entorno* (o *Configuración*), cargue estos valores. **Nunca** van en el repositorio.

| Nombre | Valor |
|---|---|
| `ConnectionStrings__DefaultConnection` | Cadena de conexión de Azure SQL (tipo *SQLAzure*) |
| `SeedAdmin__Email` / `SeedAdmin__FullName` / `SeedAdmin__Password` | Primer administrador (se crea solo si no existe; después cambie la contraseña desde la app) |
| `Storage__Path` | `/home/uploads` (carpeta persistente de App Service; sin esto los archivos se pierden al reiniciar) |
| `Email__Host`, `Email__Port`, `Email__User`, `Email__Password`, `Email__FromAddress` | Correo saliente (opcional hasta que se quieran avisos por correo) |
| `WhatsApp__PhoneNumberId`, `WhatsApp__AccessToken`, `WhatsApp__TemplateName` | WhatsApp Business Cloud API (opcional; ver README) |
| `Hangfire__TimeZone` | `America/El_Salvador` (ya es el valor por defecto) |

Las migraciones de base de datos se aplican solas al iniciar la aplicación
(`Database__ApplyMigrationsOnStartup=true`, el valor por defecto).

## 3. Conectar GitHub

1. En el App Service, *Información general* > **Descargar perfil de publicación**.
2. En GitHub: *Settings > Secrets and variables > Actions*
   - **Secret** `AZURE_WEBAPP_PUBLISH_PROFILE`: pegue el contenido completo del perfil.
   - **Variable** `AZURE_WEBAPP_NAME`: el nombre del App Service.
3. Cada integración a `main` compila, corre las pruebas y despliega. También se puede lanzar a mano desde la pestaña *Actions*.

## 4. Alerta de presupuesto (hacerlo desde el primer día)

1. Azure Portal > *Cost Management + Billing* > *Presupuestos* > **Agregar**.
2. Alcance: la suscripción o el grupo de recursos `tekcom-gestion`.
3. Monto mensual: el que TEK-COM decida (por ejemplo, 20 USD).
4. Alertas al 50 %, 80 % y 100 % del presupuesto, al correo del responsable.

## 5. Después del primer despliegue

- Entrar con el administrador de `SeedAdmin` y cambiar la contraseña.
- En **Configuración > Notificaciones** verificar el estado de correo y WhatsApp y mandar una prueba.
- El panel de trabajos programados está en `/hangfire` (solo administradores).
- Subir el logo a `src/Tekcom.Gestion.Web/wwwroot/branding/logo.png` para que salga en los exportes (si no existe, se usa el texto "TEK-COM").
