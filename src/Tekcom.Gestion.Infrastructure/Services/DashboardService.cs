using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>
/// Arma los indicadores del tablero de inicio con los mismos servicios que usan las pantallas:
/// el avance de un proyecto sale de su plan de trabajo; si no tiene plan, del avance de sus nodos;
/// y si tampoco tiene nodos, de las tareas hechas.
/// </summary>
public sealed class DashboardService(
    IDbContextFactory<ApplicationDbContext> factory,
    IProjectService projects,
    ISiteService sites,
    IWorkPlanService workPlan,
    ISprintService sprints,
    IInventoryService inventory,
    IQuantityService quantities,
    IFeederService feeder,
    IDateTimeProvider clock) : IDashboardService
{
    public async Task<DashboardData> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var all = await projects.ListAsync(ct);
        var active = all.Where(p => p.Status == ProjectStatus.Active).ToList();

        await using var db = factory.CreateDbContext();
        var tasks = await db.Tasks.AsNoTracking()
            .Include(t => t.Project)
            .Where(t => t.Project!.Status != ProjectStatus.Closed)
            .ToListAsync(ct);

        var rows = new List<DashboardProjectRow>();
        var lowStock = 0;
        DashboardSprint? currentSprint = null;
        foreach (var project in active)
        {
            rows.Add(await BuildRowAsync(project, tasks.Where(t => t.ProjectId == project.Id).ToList(), ct));

            lowStock += (await inventory.StockAsync(project.Id, null, ct))
                .Count(r => r.Status is StockStatus.Low or StockStatus.Shortage or StockStatus.Negative);

            var activeSprint = (await sprints.ListAsync(project.Id, ct))
                .Where(s => s.Sprint.Status == SprintStatus.Active)
                .OrderBy(s => s.Sprint.EndDate)
                .FirstOrDefault();
            if (activeSprint is not null && (currentSprint is null || activeSprint.Sprint.EndDate < currentSprint.Summary.Sprint.EndDate))
            {
                currentSprint = new DashboardSprint(project.Name, activeSprint);
            }
        }

        var open = tasks.Where(t => t.Status != TaskItemStatus.Done && t.DueDate is not null).ToList();
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekTasks = tasks.Where(t => t.DueDate >= monday && t.DueDate <= monday.AddDays(6)).ToList();

        var dueSoon = open.Where(t => t.DueDate < today).OrderBy(t => t.DueDate).Take(3)
            .Concat(open.Where(t => t.DueDate >= today && t.DueDate <= today.AddDays(7)).OrderBy(t => t.DueDate).ThenByDescending(t => t.Priority))
            .Take(6)
            .ToList();

        return new DashboardData
        {
            ActiveProjects = active.Count,
            OnTimeProjects = rows.Count(r => r.Pace.Status is PaceStatus.OnTime or PaceStatus.Ahead or PaceStatus.Completed or PaceStatus.Unknown),
            OverdueTasks = open.Count(t => t.DueDate < today),
            OverdueSinceYesterday = open.Count(t => t.DueDate == today.AddDays(-1)),
            TasksThisWeek = weekTasks.Count,
            TasksThisWeekDone = weekTasks.Count(t => t.Status == TaskItemStatus.Done),
            LowStockMaterials = lowStock,
            Projects = rows,
            DueSoon = dueSoon,
            CurrentSprint = currentSprint,
        };
    }

    public async Task<ProjectCharts> GetChartsAsync(int projectId, CancellationToken ct = default)
    {
        var siteList = await sites.ListAsync(projectId, ct);
        var totals = await quantities.GetGroupTotalsBySiteAsync(projectId, ct);

        var installation = new Dictionary<QuantityGroup, QuantityPair>();
        foreach (var group in totals.Values.SelectMany(g => g))
        {
            installation.TryGetValue(group.Key, out var current);
            installation[group.Key] = new QuantityPair(current.Projected + group.Value.Projected, current.Installed + group.Value.Installed);
        }

        await using var db = factory.CreateDbContext();
        var taskStatuses = await db.Tasks.AsNoTracking().Where(t => t.ProjectId == projectId).Select(t => t.Status).ToListAsync(ct);

        return new ProjectCharts
        {
            Installation = installation,
            SitesFinished = siteList.Count(s => s.Site.OdnFinalized),
            SitesInProgress = siteList.Count(s => !s.Site.OdnFinalized && s.ProgressPercent > 0),
            SitesNotStarted = siteList.Count(s => !s.Site.OdnFinalized && s.ProgressPercent == 0),
            SitesReady = siteList.Count(s => s.Site.IsReadyToDeliver),
            TasksByStatus = Enum.GetValues<TaskItemStatus>().ToDictionary(s => s, s => taskStatuses.Count(x => x == s)),
            Feeder = await feeder.GetAsync(projectId, ct),
        };
    }

    private async Task<DashboardProjectRow> BuildRowAsync(Project project, List<TaskItem> tasks, CancellationToken ct)
    {
        var siteList = await sites.ListAsync(project.Id, ct);
        var plan = await workPlan.GetAsync(project.Id, ct);

        if (!plan.IsEmpty)
        {
            // Con plan de trabajo, el ritmo del proyecto es el del plan (mismas fechas que ve en su pantalla).
            return new DashboardProjectRow(project, plan.Progress, plan.Pace, siteList.Count, siteList.Count(s => s.Site.IsReadyToDeliver));
        }

        decimal progress;
        DateOnly? start = project.PlannedStart, end = project.PlannedEnd;
        if (siteList.Count > 0)
        {
            progress = Math.Round(siteList.Average(s => s.Site.OdnFinalized ? 100 : s.ProgressPercent), 1);
            start ??= siteList.Min(s => s.Site.PlannedStart);
            end ??= siteList.Max(s => s.Site.PlannedEnd);
        }
        else
        {
            progress = tasks.Count == 0 ? 0 : Math.Round(tasks.Count(t => t.Status == TaskItemStatus.Done) * 100m / tasks.Count, 1);
        }

        var pace = PaceCalculator.Calculate(progress, start, end, clock.Today, project.PaceMarginPercent);
        return new DashboardProjectRow(project, progress, pace, siteList.Count, siteList.Count(s => s.Site.IsReadyToDeliver));
    }
}
