namespace Tekcom.Gestion.Infrastructure.Identity;

/// <summary>
/// Datos del usuario administrador inicial. Se leen de la configuración
/// (sección "SeedAdmin"); la contraseña nunca va en appsettings.json,
/// se guarda con <c>dotnet user-secrets</c> en local y en la configuración
/// de Azure App Service en producción.
/// </summary>
public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = "Administrador";
    public string Password { get; set; } = string.Empty;
}
