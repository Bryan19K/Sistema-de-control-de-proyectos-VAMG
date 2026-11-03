using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Infrastructure.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Filtro global: las consultas nunca devuelven registros marcados como
    /// eliminados, salvo que se use <c>IgnoreQueryFilters()</c> de forma explícita.
    /// </summary>
    public static ModelBuilder ApplySoftDeleteQueryFilter(this ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType) || entityType.IsOwned())
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var filter = Expression.Lambda(Expression.Not(isDeleted), parameter);

            builder.Entity(entityType.ClrType).HasQueryFilter(filter);
        }

        return builder;
    }

    /// <summary>
    /// Convenciones comunes para entidades auditables: largo de los campos de usuario.
    /// </summary>
    public static ModelBuilder ApplyAuditConventions(this ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            builder.Entity(entityType.ClrType).Property(nameof(IAuditableEntity.CreatedBy)).HasMaxLength(256);
            builder.Entity(entityType.ClrType).Property(nameof(IAuditableEntity.UpdatedBy)).HasMaxLength(256);

            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                builder.Entity(entityType.ClrType).Property(nameof(ISoftDeletable.DeletedBy)).HasMaxLength(256);
            }
        }

        return builder;
    }
}
