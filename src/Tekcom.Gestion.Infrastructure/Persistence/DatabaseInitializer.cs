using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    /// <summary>
    /// Crea o migra la base, siembra los datos base (tipos de proyecto, etapas, catálogo),
    /// los roles y el usuario administrador.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (db.Database.IsSqlite())
            {
                // SQLite se usa solo para desarrollo y pruebas: sin migraciones.
                await db.Database.EnsureCreatedAsync(ct);
            }
            else
            {
                await db.Database.MigrateAsync(ct);
            }

            await CatalogSeeder.EnsureAllAsync(db, ct);
        }

        await IdentitySeeder.SeedAsync(services, ct);
    }
}
