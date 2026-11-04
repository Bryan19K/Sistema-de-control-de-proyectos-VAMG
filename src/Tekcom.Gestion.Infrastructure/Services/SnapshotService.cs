using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class SnapshotService(
    IDbContextFactory<ApplicationDbContext> factory,
    IDashboardService dashboard,
    IProjectService projects,
    IDateTimeProvider clock) : ISnapshotService
{
    public async Task<ProgressSnapshot?> CaptureAsync(int projectId, CancellationToken ct = default)
    {
        var comparison = await dashboard.ComparisonAsync(projectId, ct);
        if (comparison is null) { return null; }

        var charts = await dashboard.GetChartsAsync(projectId, ct);
        var today = clock.Today;
        await using var db = factory.CreateDbContext();
        var snapshot = await db.ProgressSnapshots.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Date == today, ct)
            ?? db.ProgressSnapshots.Add(new ProgressSnapshot { ProjectId = projectId, Date = today }).Entity;

        snapshot.ProgressPercent = Math.Round(comparison.Progress, 2);
        snapshot.ElapsedPercent = Math.Round(comparison.Pace.ElapsedPercent, 2);
        snapshot.TotalSites = comparison.Sites;
        snapshot.SitesFinished = charts.SitesFinished;
        snapshot.SitesReady = charts.SitesReady;
        snapshot.TasksTotal = charts.TasksByStatus.Values.Sum();
        snapshot.TasksDone = charts.TasksByStatus.GetValueOrDefault(TaskItemStatus.Done);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    public async Task<int> CaptureAllAsync(CancellationToken ct = default)
    {
        var count = 0;
        foreach (var project in (await projects.ListAsync(ct)).Where(p => p.Status != ProjectStatus.Closed))
        {
            if (await CaptureAsync(project.Id, ct) is not null) { count++; }
        }

        return count;
    }

    public async Task<List<ProgressSnapshot>> ListAsync(int projectId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var list = await db.ProgressSnapshots.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(ct);
        return list.OrderBy(s => s.Date).ToList();
    }
}
