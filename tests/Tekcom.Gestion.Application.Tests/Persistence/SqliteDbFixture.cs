using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Infrastructure.Persistence.Interceptors;

namespace Tekcom.Gestion.Application.Tests.Persistence;

/// <summary>
/// Base SQLite en memoria que vive mientras la conexión esté abierta.
/// Cada instancia del contexto comparte la misma base.
/// </summary>
public sealed class SqliteDbFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeDateTime Clock { get; } = new(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));

    public SqliteDbFixture()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditableEntityInterceptor(CurrentUser, Clock))
            .Options;

        return new TestDbContext(options);
    }

    public void Dispose() => _connection.Dispose();
}
