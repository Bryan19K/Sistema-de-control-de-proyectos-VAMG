using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Antes de guardar:
/// - Llena CreatedAt/By y UpdatedAt/By en las entidades auditables.
/// - Convierte un borrado físico de una entidad <see cref="ISoftDeletable"/>
///   en un borrado lógico (IsDeleted = true, DeletedAt/By).
/// </summary>
public sealed class AuditableEntityInterceptor(
    ICurrentUserService currentUser,
    IDateTimeProvider dateTime) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditInformation(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditInformation(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = dateTime.UtcNow;
        var user = currentUser.UserName ?? currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable deletable)
            {
                entry.State = EntityState.Modified;
                deletable.IsDeleted = true;
                deletable.DeletedAt = now;
                deletable.DeletedBy = user;

                if (entry.Entity is IAuditableEntity deletedAuditable)
                {
                    deletedAuditable.UpdatedAt = now;
                    deletedAuditable.UpdatedBy = user;
                }

                continue;
            }

            if (entry.Entity is not IAuditableEntity auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = user;
                    break;

                case EntityState.Modified when HasChangedOwnedEntities(entry) || entry.Properties.Any(p => p.IsModified):
                    auditable.UpdatedAt = now;
                    auditable.UpdatedBy = user;
                    break;
            }
        }
    }

    private static bool HasChangedOwnedEntities(EntityEntry entry) =>
        entry.References.Any(r =>
            r.TargetEntry is not null &&
            r.TargetEntry.Metadata.IsOwned() &&
            r.TargetEntry.State is EntityState.Added or EntityState.Modified);
}
