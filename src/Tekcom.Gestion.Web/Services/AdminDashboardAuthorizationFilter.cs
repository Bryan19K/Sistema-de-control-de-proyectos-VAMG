using Hangfire.Dashboard;
using Tekcom.Gestion.Domain.Identity;

namespace Tekcom.Gestion.Web.Services;

/// <summary>El panel de Hangfire solo se abre con sesión iniciada y rol Admin.</summary>
public sealed class AdminDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var user = context.GetHttpContext().User;
        return user.Identity?.IsAuthenticated == true && user.IsInRole(AppRoles.Admin);
    }
}
