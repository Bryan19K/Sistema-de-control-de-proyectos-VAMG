using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Tekcom.Gestion.Application.Common.Interfaces;

namespace Tekcom.Gestion.Web.Services;

/// <summary>
/// Obtiene el usuario actual tanto en peticiones HTTP normales (páginas de
/// cuenta, endpoints) como dentro de un circuito interactivo de Blazor Server,
/// donde HttpContext no está disponible y se usa AuthenticationStateProvider.
/// </summary>
public sealed class CurrentUserService(
    IHttpContextAccessor httpContextAccessor,
    AuthenticationStateProvider authenticationStateProvider) : ICurrentUserService
{
    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    private ClaimsPrincipal? Principal
    {
        get
        {
            var httpUser = httpContextAccessor.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated == true)
            {
                return httpUser;
            }

            var stateTask = authenticationStateProvider.GetAuthenticationStateAsync();
            var state = stateTask.IsCompleted ? stateTask.Result : stateTask.GetAwaiter().GetResult();
            return state.User;
        }
    }
}
