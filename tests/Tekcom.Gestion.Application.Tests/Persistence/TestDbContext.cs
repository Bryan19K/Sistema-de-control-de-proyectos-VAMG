using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Application.Tests.Persistence;

/// <summary>Entidad mínima para probar auditoría y borrado lógico.</summary>
public sealed class Note : AuditableEntity
{
    public string Text { get; set; } = string.Empty;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyAuditConventions();
        builder.ApplySoftDeleteQueryFilter();
    }
}
