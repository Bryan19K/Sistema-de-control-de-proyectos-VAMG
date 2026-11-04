using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class BuildingService(IDbContextFactory<ApplicationDbContext> factory) : IBuildingService
{
    public async Task<List<BuildingOverview>> OverviewAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var projects = await db.Projects.AsNoTracking().Include(p => p.Client).Include(p => p.ProjectType)
            .Where(p => p.ProjectType!.Code == CatalogSeeder.BuildingCode).ToListAsync(ct);
        var ids = projects.Select(p => p.Id).ToList();
        var phases = await db.ProjectPhases.AsNoTracking().Where(x => ids.Contains(x.ProjectId)).ToListAsync(ct);
        var records = await db.BuildingRecords.AsNoTracking().Where(x => ids.Contains(x.ProjectId))
            .Select(x => new { x.ProjectId, x.Kind, x.ProgressPercent }).ToListAsync(ct);
        var materialLines = await db.InventoryMovements.AsNoTracking()
            .Where(m => ids.Contains(m.ProjectId) && !m.IsVoided && m.Type == MovementType.Delivery)
            .GroupBy(m => m.ProjectId).Select(g => new { ProjectId = g.Key, Lines = g.Select(m => m.MaterialId).Distinct().Count() }).ToListAsync(ct);

        return projects.OrderBy(p => p.Code).Select(p => new BuildingOverview
        {
            Project = p,
            Phases = phases.Where(x => x.ProjectId == p.Id).OrderBy(x => x.SortOrder).ToList(),
            Units = records.Count(r => r.ProjectId == p.Id && r.Kind == BuildingRecordKind.Unit),
            UnitsFinished = records.Count(r => r.ProjectId == p.Id && r.Kind == BuildingRecordKind.Unit && r.ProgressPercent >= 100),
            Levels = records.Count(r => r.ProjectId == p.Id && r.Kind == BuildingRecordKind.Level),
            MaterialLines = materialLines.FirstOrDefault(m => m.ProjectId == p.Id)?.Lines ?? 0,
        }).ToList();
    }

    public async Task<List<BuildingRecord>> RecordsAsync(int projectId, BuildingRecordKind kind, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.BuildingRecords.AsNoTracking().Where(x => x.ProjectId == projectId && x.Kind == kind)
            .OrderBy(x => x.SortOrder).ToListAsync(ct);
    }

    public async Task<List<BuildingMaterialRow>> MaterialsAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var moves = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == projectId && !m.IsVoided)
            .Select(m => new { m.MaterialId, m.Material!.Name, m.Material.Unit, m.Type, m.Quantity })
            .ToListAsync(ct);
        return moves.GroupBy(m => (m.MaterialId, m.Name, m.Unit))
            .Select(g => new BuildingMaterialRow(g.Key.Name, g.Key.Unit,
                g.Where(m => m.Type == MovementType.Delivery).Sum(m => m.Quantity),
                g.Where(m => m.Type == MovementType.Installed).Sum(m => m.Quantity),
                g.Where(m => m.Type == MovementType.Return).Sum(m => m.Quantity)))
            .OrderBy(r => r.Material, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
