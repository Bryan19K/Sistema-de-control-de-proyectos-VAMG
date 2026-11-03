namespace Tekcom.Gestion.Domain.Identity;

/// <summary>
/// Roles del sistema. Se crean como semilla al arrancar la aplicación.
/// </summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Bodega = "Bodega";
    public const string Supervisor = "Supervisor";
    public const string Gerencia = "Gerencia";
    public const string Contratista = "Contratista";

    public static readonly IReadOnlyList<string> All =
    [
        Admin,
        Bodega,
        Supervisor,
        Gerencia,
        Contratista,
    ];
}
