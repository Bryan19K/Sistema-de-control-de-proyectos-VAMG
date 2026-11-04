using Microsoft.AspNetCore.Components.Authorization;
using Tekcom.Gestion.Domain.Identity;

namespace Tekcom.Gestion.Web.Services;

/// <summary>
/// Qué puede hacer el usuario actual en pantalla. Gerencia y Contratista solo consultan;
/// Administrador, Supervisor y Bodega pueden registrar y editar.
/// </summary>
public sealed class UserAccess(AuthenticationStateProvider authentication)
{
    public async Task<bool> IsAdminAsync() => (await UserAsync()).IsInRole(AppRoles.Admin);

    public async Task<bool> CanEditAsync()
    {
        var user = await UserAsync();
        return user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Supervisor) || user.IsInRole(AppRoles.Bodega);
    }

    public async Task<bool> CanManageWarehouseAsync()
    {
        var user = await UserAsync();
        return user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Bodega);
    }

    public async Task<string> UserIdAsync() =>
        (await UserAsync()).FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

    private async Task<System.Security.Claims.ClaimsPrincipal> UserAsync() =>
        (await authentication.GetAuthenticationStateAsync()).User;
}
