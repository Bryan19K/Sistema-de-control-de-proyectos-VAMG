using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Tekcom.Gestion.Infrastructure.Identity;

namespace Tekcom.Gestion.Infrastructure.Persistence;

/// <summary>
/// Fábrica usada solo por las herramientas de EF Core (<c>dotnet ef migrations add</c>).
/// Crear una migración no necesita conectarse a la base, así que basta una cadena de ejemplo.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=tekcom_gestion;Username=postgres")
            .UseApplicationServiceProvider(IdentitySchema.CreateMinimalServiceProvider())
            .Options;

        return new ApplicationDbContext(options);
    }
}
