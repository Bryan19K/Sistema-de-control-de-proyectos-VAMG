using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Tekcom.Gestion.Infrastructure.Identity;

/// <summary>
/// Versión del esquema de tablas de Identity que usa el sistema (v3 incluye passkeys).
/// IdentityDbContext la lee de las opciones de Identity del proveedor de servicios de la
/// aplicación, por eso las herramientas de EF y las pruebas necesitan un proveedor mínimo.
/// </summary>
public static class IdentitySchema
{
    public static readonly Version Current = IdentitySchemaVersions.Version3;

    public static IServiceProvider CreateMinimalServiceProvider() =>
        new ServiceCollection()
            .Configure<IdentityOptions>(options => options.Stores.SchemaVersion = Current)
            .BuildServiceProvider();
}
