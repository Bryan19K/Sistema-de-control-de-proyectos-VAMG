using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
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

    // ------------------------------------------------------------------ edición

    public async Task<Project> SaveProjectAsync(int projectId, decimal? reportedProgress, string? pendingWork, ProjectStatus status,
        string? responsible, DateOnly? plannedStart, DateOnly? plannedEnd, CancellationToken ct = default)
    {
        if (reportedProgress is < 0 or > 100)
        {
            throw new BusinessRuleException("El avance va de 0 a 100.");
        }

        if (plannedStart is not null && plannedEnd is not null && plannedEnd < plannedStart)
        {
            throw new BusinessRuleException("La fecha final no puede ser anterior a la inicial.");
        }

        await using var db = factory.CreateDbContext();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct) ?? throw new BusinessRuleException("El proyecto no existe.");
        project.ReportedProgress = reportedProgress;
        project.PendingWork = string.IsNullOrWhiteSpace(pendingWork) ? null : pendingWork.Trim();
        project.Status = status;
        project.ResponsibleName = string.IsNullOrWhiteSpace(responsible) ? null : responsible.Trim();
        project.PlannedStart = plannedStart;
        project.PlannedEnd = plannedEnd;
        await db.SaveChangesAsync(ct);
        return project;
    }

    public async Task<ProjectPhase> SavePhaseAsync(ProjectPhase phase, CancellationToken ct = default)
    {
        var name = (phase.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("La fase necesita un nombre.");
        }

        if (phase.Percent is < 0 or > 100 || phase.Weight is < 0 or > 100)
        {
            throw new BusinessRuleException("El peso y el avance van de 0 a 100.");
        }

        await using var db = factory.CreateDbContext();
        ProjectPhase target;
        if (phase.Id == 0)
        {
            target = new ProjectPhase { ProjectId = phase.ProjectId, SortOrder = 1000 + await db.ProjectPhases.CountAsync(p => p.ProjectId == phase.ProjectId, ct) };
            db.ProjectPhases.Add(target);
        }
        else
        {
            target = await db.ProjectPhases.FirstOrDefaultAsync(p => p.Id == phase.Id, ct) ?? throw new BusinessRuleException("La fase no existe.");
        }

        target.Name = name;
        target.ParentName = string.IsNullOrWhiteSpace(phase.ParentName) ? null : phase.ParentName.Trim();
        target.Weight = phase.Weight;
        target.Percent = phase.Percent;
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeletePhaseAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var phase = await db.ProjectPhases.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new BusinessRuleException("La fase no existe.");
        db.ProjectPhases.Remove(phase);
        await db.SaveChangesAsync(ct);
    }

    public async Task<BuildingRecord> SaveRecordAsync(BuildingRecord record, CancellationToken ct = default)
    {
        var name = (record.Name ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new BusinessRuleException("Escriba el apartamento o el nombre del rubro.");
        }

        if (record.ProgressPercent is < 0 or > 100)
        {
            throw new BusinessRuleException("El avance va de 0 a 100.");
        }

        await using var db = factory.CreateDbContext();
        BuildingRecord target;
        if (record.Id == 0)
        {
            target = new BuildingRecord
            {
                ProjectId = record.ProjectId, Kind = record.Kind,
                SortOrder = 10000 + await db.BuildingRecords.CountAsync(r => r.ProjectId == record.ProjectId && r.Kind == record.Kind, ct),
            };
            db.BuildingRecords.Add(target);
        }
        else
        {
            target = await db.BuildingRecords.FirstOrDefaultAsync(r => r.Id == record.Id, ct) ?? throw new BusinessRuleException("El registro no existe.");
        }

        target.Level = string.IsNullOrWhiteSpace(record.Level) ? null : record.Level.Trim();
        target.Name = name;
        target.ProgressPercent = record.ProgressPercent;
        target.Status = string.IsNullOrWhiteSpace(record.Status) ? null : record.Status.Trim();
        target.Notes = string.IsNullOrWhiteSpace(record.Notes) ? null : record.Notes.Trim();
        await db.SaveChangesAsync(ct);
        return target;
    }

    public async Task DeleteRecordAsync(int id, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var record = await db.BuildingRecords.FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new BusinessRuleException("El registro no existe.");
        db.BuildingRecords.Remove(record);
        await db.SaveChangesAsync(ct);
    }
}
