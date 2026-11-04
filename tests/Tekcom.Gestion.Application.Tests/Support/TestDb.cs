using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Tests.Persistence;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Interceptors;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Application.Tests.Support;

/// <summary>Base SQLite en memoria con el modelo real y los datos base, para probar servicios.</summary>
public sealed class TestDb : IDbContextFactory<ApplicationDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeDateTime Clock { get; } = new(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));

    private TestDb()
    {
        _connection.Open();
    }

    public static async Task<TestDb> CreateAsync()
    {
        var db = new TestDb();
        await using var context = db.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        await CatalogSeeder.EnsureAllAsync(context);
        return db;
    }

    public ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .UseApplicationServiceProvider(IdentitySchema.CreateMinimalServiceProvider())
            .AddInterceptors(new AuditableEntityInterceptor(CurrentUser, Clock))
            .Options;

        return new ApplicationDbContext(options);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Ruta de un archivo del repositorio (sube carpetas hasta encontrar la solución).</summary>
    public static string RepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tekcom.Gestion.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio."), relativePath);
    }
}
