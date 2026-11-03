using Microsoft.AspNetCore.Identity;

namespace Tekcom.Gestion.Infrastructure.Identity;

/// <summary>
/// Usuario del sistema. Extiende IdentityUser con el nombre completo que se
/// muestra en la interfaz y en el campo "Elaborado por" de los exportes.
/// </summary>
public class ApplicationUser : IdentityUser
{
    [PersonalData]
    public string FullName { get; set; } = string.Empty;
}
