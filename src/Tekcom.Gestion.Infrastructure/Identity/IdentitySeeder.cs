using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tekcom.Gestion.Domain.Identity;

namespace Tekcom.Gestion.Infrastructure.Identity;

/// <summary>
/// Crea los roles del sistema y el usuario administrador inicial si no existen.
/// Es idempotente: se puede ejecutar en cada arranque sin duplicar nada.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(IdentitySeeder));
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var options = provider.GetRequiredService<IOptions<SeedAdminOptions>>().Value;

        foreach (var role in AppRoles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                var result = await roleManager.CreateAsync(new IdentityRole(role));
                EnsureSucceeded(result, $"crear el rol '{role}'");
                logger.LogInformation("Rol '{Role}' creado.", role);
            }
        }

        if (string.IsNullOrWhiteSpace(options.Email) || string.IsNullOrWhiteSpace(options.Password))
        {
            logger.LogWarning(
                "No se creó el usuario administrador: faltan SeedAdmin:Email o SeedAdmin:Password en la configuración (user-secrets).");
            return;
        }

        var admin = await userManager.FindByEmailAsync(options.Email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = options.Email,
                Email = options.Email,
                EmailConfirmed = true,
                FullName = options.FullName,
            };

            var created = await userManager.CreateAsync(admin, options.Password);
            EnsureSucceeded(created, $"crear el usuario administrador '{options.Email}'");
            logger.LogInformation("Usuario administrador '{Email}' creado.", options.Email);
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.Admin))
        {
            var added = await userManager.AddToRoleAsync(admin, AppRoles.Admin);
            EnsureSucceeded(added, $"asignar el rol Admin a '{options.Email}'");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void EnsureSucceeded(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            var errors = string.Join("; ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"No se pudo {action}: {errors}");
        }
    }
}
