# Correr el sistema en GitHub Codespaces

1. En el repositorio: **Code → Codespaces → Create codespace** sobre la rama que quieras revisar.
2. Esperá a que termine de preparar el entorno (instala .NET 10 y levanta SQL Server).
3. En la terminal del Codespace:

   ```bash
   dotnet run --project src/Tekcom.Gestion.Web
   ```

4. Se abre el navegador solo (puerto 5276). Iniciá sesión con:

   - Correo: `admin@tekcom.local`
   - Contraseña: `Admin.Codespace1!`

Estas credenciales y la contraseña de SQL Server existen solo dentro del Codespace, que es un
entorno desechable. En local y en producción se usan `dotnet user-secrets` y la configuración
de Azure (ver README principal).
