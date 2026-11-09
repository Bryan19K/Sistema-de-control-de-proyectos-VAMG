using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class DeletionService(
    IDbContextFactory<ApplicationDbContext> factory,
    ICurrentUserService currentUser,
    IDateTimeProvider clock) : IDeletionService
{
    private static readonly MethodInfo RemoveIntMethod = typeof(DeletionService).GetMethod(nameof(RemoveWhereInt), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo RemoveNullableMethod = typeof(DeletionService).GetMethod(nameof(RemoveWhereNullable), BindingFlags.NonPublic | BindingFlags.Instance)!;

    // ------------------------------------------------------------------ material dentro de un nodo

    public async Task<int> RemoveMaterialFromSiteAsync(int siteId, int materialId, string reason, CancellationToken ct = default)
    {
        RequireReason(reason);
        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (await db.Liquidations.AnyAsync(l => l.SiteId == siteId && l.Status == LiquidationStatus.Closed, ct))
        {
            throw new BusinessRuleException("El nodo está liquidado: reabra la liquidación para quitar materiales.");
        }

        var touched = 0;
        var movements = await db.InventoryMovements
            .Where(m => m.SiteId == siteId && m.MaterialId == materialId && !m.IsVoided).ToListAsync(ct);
        foreach (var movement in movements)
        {
            Void(movement, $"Material quitado del nodo: {reason.Trim()}");
        }

        touched += movements.Count;

        var adjustments = await db.ProjectionAdjustments.Where(a => a.SiteId == siteId && a.MaterialId == materialId).ToListAsync(ct);
        adjustments.ForEach(SoftDelete);
        touched += adjustments.Count;

        var lines = await db.DesignProjectionLines
            .Where(l => l.MaterialId == materialId && l.DesignProjection!.SiteId == siteId).ToListAsync(ct);
        lines.ForEach(SoftDelete);
        touched += lines.Count;

        var justifications = await db.LiquidationJustifications.Where(j => j.Liquidation!.SiteId == siteId && j.MaterialId == materialId).ToListAsync(ct);
        justifications.ForEach(SoftDelete);
        touched += justifications.Count;

        if (touched == 0)
        {
            throw new BusinessRuleException("Ese material no tiene registros en el nodo.");
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return touched;
    }

    // ------------------------------------------------------------------ material del catálogo

    public async Task DeleteMaterialAsync(int materialId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var material = await db.Materials.Include(m => m.Aliases).FirstOrDefaultAsync(m => m.Id == materialId, ct)
            ?? throw new BusinessRuleException("El material no existe.");

        var movements = await db.InventoryMovements.CountAsync(m => m.MaterialId == materialId && !m.IsVoided, ct);
        var adjustments = await db.ProjectionAdjustments.CountAsync(a => a.MaterialId == materialId, ct);
        var lines = await db.DesignProjectionLines.CountAsync(l => l.MaterialId == materialId, ct);
        var total = movements + adjustments + lines;
        if (total > 0)
        {
            throw new BusinessRuleException(
                $"'{material.Name}' todavía se usa ({movements} movimientos de bodega, {adjustments} ajustes y {lines} líneas de proyectado). " +
                "Quítelo de los nodos (el tacho rojo en la fila del material) y anule sus movimientos en Materiales → Movimientos; o, si solo no lo quiere ver, desmarque «Activo».");
        }

        material.Aliases.ToList().ForEach(SoftDelete);
        SoftDelete(material);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ nodo

    public async Task<DeletionImpact> SiteImpactAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var movements = await db.InventoryMovements.CountAsync(m => m.SiteId == siteId && !m.IsVoided, ct);
        return new DeletionImpact(1, movements);
    }

    public async Task DeleteSiteAsync(int siteId, string reason, CancellationToken ct = default)
    {
        RequireReason(reason);
        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct) ?? throw new BusinessRuleException("El nodo no existe.");
        await RemoveSiteAsync(db, site, reason, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private async Task RemoveSiteAsync(ApplicationDbContext db, Site site, string reason, CancellationToken ct)
    {
        var movements = await db.InventoryMovements.Where(m => m.SiteId == site.Id && !m.IsVoided).ToListAsync(ct);
        foreach (var movement in movements)
        {
            Void(movement, $"Nodo {site.Code} eliminado: {reason.Trim()}");
        }

        // El dinero no se borra con el nodo: los gastos conservan su historial.
        await RemoveByKeyAsync(db, "SiteId", site.Id, [typeof(InventoryMovement), typeof(ExpenseEntry), typeof(Site)], ct);
        SoftDelete(site);
    }

    // ------------------------------------------------------------------ proyecto

    public async Task<DeletionImpact> ProjectImpactAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var nodes = await db.Sites.CountAsync(s => s.ProjectId == projectId, ct);
        var movements = await db.InventoryMovements.CountAsync(m => m.ProjectId == projectId && !m.IsVoided, ct);
        return new DeletionImpact(nodes, movements);
    }

    public async Task DeleteProjectAsync(int projectId, string reason, CancellationToken ct = default)
    {
        RequireReason(reason);
        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new BusinessRuleException("El proyecto no existe.");

        foreach (var site in await db.Sites.Where(s => s.ProjectId == projectId).ToListAsync(ct))
        {
            await RemoveSiteAsync(db, site, reason, ct);
        }

        foreach (var movement in await db.InventoryMovements.Where(m => m.ProjectId == projectId && !m.IsVoided).ToListAsync(ct))
        {
            Void(movement, $"Proyecto {project.Code} eliminado: {reason.Trim()}");
        }

        await RemoveByKeyAsync(db, "ProjectId", projectId, [typeof(InventoryMovement), typeof(Site), typeof(Project)], ct);
        SoftDelete(project);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // ------------------------------------------------------------------ contratistas y supervisores

    public async Task DeleteContractorAsync(int contractorId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var contractor = await db.Contractors.FirstOrDefaultAsync(c => c.Id == contractorId, ct) ?? throw new BusinessRuleException("El contratista no existe.");
        var sites = await db.Sites.CountAsync(s => s.ContractorId == contractorId, ct);
        var movements = await db.InventoryMovements.CountAsync(m => m.ContractorId == contractorId && !m.IsVoided, ct);
        if (sites + movements > 0)
        {
            throw new BusinessRuleException(
                $"'{contractor.Name}' tiene {sites} nodo(s) y {movements} movimiento(s) de bodega a su nombre. " +
                "Para no perder el historial, desactívelo (ya no aparecerá para elegir) en lugar de borrarlo.");
        }

        await RemoveByKeyAsync(db, "ContractorId", contractorId, [typeof(Site), typeof(InventoryMovement), typeof(Contractor)], ct);
        SoftDelete(contractor);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteSupervisorAsync(int supervisorId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var supervisor = await db.Supervisors.FirstOrDefaultAsync(s => s.Id == supervisorId, ct) ?? throw new BusinessRuleException("El supervisor no existe.");
        var sites = await db.Sites.CountAsync(s => s.SupervisorId == supervisorId, ct);
        if (sites > 0)
        {
            throw new BusinessRuleException($"'{supervisor.Name}' supervisa {sites} nodo(s): cámbieles el supervisor antes de borrarlo, o desactívelo.");
        }

        SoftDelete(supervisor);
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ ayudas

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new BusinessRuleException("Escriba el motivo: queda registrado para la auditoría.");
        }
    }

    /// <summary>Borrado lógico directo (sin cascada de EF): queda quién y cuándo, y el registro sigue en la base.</summary>
    private void SoftDelete(ISoftDeletable entity)
    {
        entity.IsDeleted = true;
        entity.DeletedAt = clock.UtcNow;
        entity.DeletedBy = currentUser.UserName ?? currentUser.UserId;
    }

    private void Void(InventoryMovement movement, string reason)
    {
        movement.IsVoided = true;
        movement.VoidReason = reason.Length > 500 ? reason[..500] : reason;
        movement.VoidedAt = clock.UtcNow;
        movement.VoidedBy = currentUser.UserName ?? currentUser.UserId;
    }

    /// <summary>Borra (lógicamente) todo registro que apunte al id por la columna indicada, salvo los tipos excluidos.</summary>
    private async Task RemoveByKeyAsync(ApplicationDbContext db, string key, int id, Type[] excluded, CancellationToken ct)
    {
        foreach (var entityType in db.Model.GetEntityTypes().ToList())
        {
            var clr = entityType.ClrType;
            if (entityType.IsOwned() || !typeof(ISoftDeletable).IsAssignableFrom(clr) || excluded.Contains(clr))
            {
                continue;
            }

            var property = entityType.FindProperty(key);
            if (property is null)
            {
                continue;
            }

            var method = (property.ClrType == typeof(int) ? RemoveIntMethod : RemoveNullableMethod).MakeGenericMethod(clr);
            await (Task)method.Invoke(this, [db, key, id, ct])!;
        }
    }

    private async Task RemoveWhereInt<T>(ApplicationDbContext db, string key, int id, CancellationToken ct) where T : class
    {
        var items = await db.Set<T>().Where(e => EF.Property<int>(e, key) == id).ToListAsync(ct);
        foreach (var item in items)
        {
            SoftDelete((ISoftDeletable)item);
        }
    }

    private async Task RemoveWhereNullable<T>(ApplicationDbContext db, string key, int id, CancellationToken ct) where T : class
    {
        var items = await db.Set<T>().Where(e => EF.Property<int?>(e, key) == id).ToListAsync(ct);
        foreach (var item in items)
        {
            SoftDelete((ISoftDeletable)item);
        }
    }
}
