using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Calcula cantidades a partir de la proyección de diseño (última versión), los ajustes y los
/// movimientos. Nada se guarda: todo se deriva (principios 1 y 2 del plan).
/// Los cálculos se hacen en memoria para no depender de agregados de decimales del proveedor.
/// </summary>
public sealed class QuantityService(IDbContextFactory<ApplicationDbContext> factory) : IQuantityService
{
    public async Task<List<MaterialQuantity>> GetSiteMaterialsAsync(int siteId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var projection = await db.DesignProjections.AsNoTracking()
            .Where(p => p.SiteId == siteId)
            .OrderByDescending(p => p.Version)
            .Include(p => p.Lines)
            .FirstOrDefaultAsync(ct);

        var adjustments = await db.ProjectionAdjustments.AsNoTracking()
            .Where(a => a.SiteId == siteId)
            .Select(a => new { a.MaterialId, a.Delta })
            .ToListAsync(ct);

        var movements = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.SiteId == siteId && !m.IsVoided)
            .Select(m => new { m.MaterialId, m.Type, m.Quantity })
            .ToListAsync(ct);

        var materialIds = new HashSet<int>();
        if (projection is not null)
        {
            materialIds.UnionWith(projection.Lines.Select(l => l.MaterialId));
        }

        materialIds.UnionWith(adjustments.Select(a => a.MaterialId));
        materialIds.UnionWith(movements.Select(m => m.MaterialId));

        var materials = await db.Materials.AsNoTracking()
            .Where(m => materialIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        decimal Sum(IEnumerable<decimal> values) => values.Sum();

        var result = new List<MaterialQuantity>();
        foreach (var id in materialIds)
        {
            if (!materials.TryGetValue(id, out var material))
            {
                continue;
            }

            result.Add(new MaterialQuantity
            {
                Material = material,
                DesignProjected = Sum(projection?.Lines.Where(l => l.MaterialId == id).Select(l => l.Quantity) ?? []),
                Adjustments = Sum(adjustments.Where(a => a.MaterialId == id).Select(a => a.Delta)),
                Delivered = Sum(movements.Where(m => m.MaterialId == id && m.Type == MovementType.Delivery).Select(m => m.Quantity)),
                Installed = Sum(movements.Where(m => m.MaterialId == id && m.Type == MovementType.Installed).Select(m => m.Quantity)),
                Returned = Sum(movements.Where(m => m.MaterialId == id && m.Type == MovementType.Return).Select(m => m.Quantity)),
            });
        }

        return result
            .OrderBy(q => q.Material.Category)
            .ThenBy(q => q.Material.LengthMeters ?? 0)
            .ThenBy(q => q.Material.Name)
            .ToList();
    }

    public async Task<Dictionary<int, Dictionary<QuantityGroup, QuantityPair>>> GetGroupTotalsBySiteAsync(
        int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var groups = await db.Materials.AsNoTracking()
            .Where(m => m.Group != QuantityGroup.None)
            .ToDictionaryAsync(m => m.Id, m => m.Group, ct);

        var projections = await db.DesignProjections.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Include(p => p.Lines)
            .ToListAsync(ct);

        var latest = projections
            .GroupBy(p => p.SiteId)
            .Select(g => g.OrderByDescending(p => p.Version).First())
            .ToList();

        var adjustments = await db.ProjectionAdjustments.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => new { a.SiteId, a.MaterialId, a.Delta })
            .ToListAsync(ct);

        var installed = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == projectId && m.SiteId != null && !m.IsVoided && m.Type == MovementType.Installed)
            .Select(m => new { SiteId = m.SiteId!.Value, m.MaterialId, m.Quantity })
            .ToListAsync(ct);

        var result = new Dictionary<int, Dictionary<QuantityGroup, QuantityPair>>();

        Dictionary<QuantityGroup, QuantityPair> For(int siteId)
        {
            if (!result.TryGetValue(siteId, out var dict))
            {
                dict = [];
                result[siteId] = dict;
            }

            return dict;
        }

        void Add(int siteId, int materialId, decimal projected, decimal installedQty)
        {
            if (!groups.TryGetValue(materialId, out var group))
            {
                return;
            }

            var dict = For(siteId);
            dict.TryGetValue(group, out var current);
            dict[group] = new QuantityPair(current.Projected + projected, current.Installed + installedQty);
        }

        foreach (var projection in latest)
        {
            foreach (var line in projection.Lines)
            {
                Add(projection.SiteId, line.MaterialId, line.Quantity, 0);
            }
        }

        foreach (var adjustment in adjustments)
        {
            Add(adjustment.SiteId, adjustment.MaterialId, adjustment.Delta, 0);
        }

        foreach (var movement in installed)
        {
            Add(movement.SiteId, movement.MaterialId, 0, movement.Quantity);
        }

        return result;
    }

    public async Task<Dictionary<int, decimal>> GetProjectedByMaterialAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();

        var projections = await db.DesignProjections.AsNoTracking()
            .Where(p => p.ProjectId == projectId)
            .Include(p => p.Lines)
            .ToListAsync(ct);
        var latest = projections
            .GroupBy(p => p.SiteId)
            .Select(g => g.OrderByDescending(p => p.Version).First());

        var result = new Dictionary<int, decimal>();
        foreach (var line in latest.SelectMany(p => p.Lines))
        {
            result[line.MaterialId] = result.GetValueOrDefault(line.MaterialId) + line.Quantity;
        }

        var adjustments = await db.ProjectionAdjustments.AsNoTracking()
            .Where(a => a.ProjectId == projectId)
            .Select(a => new { a.MaterialId, a.Delta })
            .ToListAsync(ct);
        foreach (var adjustment in adjustments)
        {
            result[adjustment.MaterialId] = result.GetValueOrDefault(adjustment.MaterialId) + adjustment.Delta;
        }

        return result;
    }
}
