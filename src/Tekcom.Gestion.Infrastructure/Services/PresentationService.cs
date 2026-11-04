using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>Reúne en un solo lugar los datos de la sección Presentación a partir de los mismos servicios de las pantallas.</summary>
public sealed class PresentationService(
    IProjectService projects,
    IWorkPlanService workPlan,
    ISiteService sites,
    IDashboardService dashboard,
    IInventoryService inventory,
    ITaskService tasks,
    IUserAdminService users,
    ISprintService sprints,
    IMeasurementService measurements,
    ILiquidationService liquidation,
    IDateTimeProvider clock) : IPresentationService
{
    public async Task<PresentationData> GetAsync(int projectId, CancellationToken ct = default)
    {
        var project = await projects.GetAsync(projectId, ct) ?? throw new BusinessRuleException("Seleccione un proyecto.");
        var today = clock.Today;
        var plan = await workPlan.GetAsync(projectId, ct);
        var siteList = await sites.ListAsync(projectId, ct);
        var charts = await dashboard.GetChartsAsync(projectId, ct);

        // Avance general: el del plan de trabajo; sin plan, el promedio de los nodos.
        decimal progress;
        PaceResult pace;
        if (!plan.IsEmpty)
        {
            progress = plan.Progress;
            pace = plan.Pace;
        }
        else
        {
            progress = siteList.Count == 0 ? 0 : Math.Round(siteList.Average(s => s.Site.OdnFinalized ? 100 : s.ProgressPercent), 1);
            var start = project.PlannedStart ?? siteList.Min(s => s.Site.PlannedStart);
            var end = project.PlannedEnd ?? siteList.Max(s => s.Site.PlannedEnd);
            pace = PaceCalculator.Calculate(progress, start, end, today, project.PaceMarginPercent);
        }

        var stock = (await inventory.StockAsync(projectId, null, ct))
            .Where(r => r.ProjectedTotal > 0 || r.Received > 0 || r.Delivered > 0 || r.Installed > 0)
            .OrderByDescending(r => Math.Max(r.ProjectedTotal, r.Received))
            .Take(10)
            .Select(r => new MaterialBar(r.Material.Name, r.Material.Unit, r.Received, r.Delivered, r.Installed, r.Returned))
            .ToList();

        var names = (await users.ListAsync(ct)).ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName);
        var taskList = await tasks.ListAsync(new TaskFilter { ProjectId = projectId }, ct);
        var byPerson = taskList.GroupBy(t => t.AssigneeUserId is not null && names.TryGetValue(t.AssigneeUserId, out var n) ? n : "Sin responsable")
            .Select(g => new NamedCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(8)
            .ToList();

        var sprintBars = (await sprints.ListAsync(projectId, ct))
            .OrderBy(s => s.Sprint.StartDate)
            .Select(s => new SprintBar(s.Sprint.Name, s.ProgressPercent, s.Pace.ElapsedPercent, s.Pace.Status, s.Sprint.Status == SprintStatus.Closed))
            .ToList();

        int measured = 0, ok = 0, bad = 0, closed = 0;
        foreach (var s in siteList)
        {
            var m = await measurements.SummaryAsync(s.Site.Id, ct);
            measured += m.Measured;
            ok += m.Acceptable;
            bad += m.NotAcceptable;
            if (await liquidation.IsClosedAsync(s.Site.Id, ct)) { closed++; }
        }

        return new PresentationData
        {
            ProjectName = project.Name,
            Today = today,
            Progress = progress,
            Pace = pace,
            PlanStart = plan.IsEmpty ? project.PlannedStart : plan.Start,
            PlanEnd = plan.IsEmpty ? project.PlannedEnd : plan.End,
            Curve = BuildCurve(plan, today),
            Nodes = siteList.OrderByDescending(s => s.Site.OdnFinalized ? 100 : s.ProgressPercent).ThenBy(s => s.Site.Code)
                .Select(s => new NodeBar(s.Site.Code, s.Site.OdnFinalized ? 100 : s.ProgressPercent, s.Pace.Status, s.Site.IsReadyToDeliver)).ToList(),
            Charts = charts,
            Materials = stock,
            TasksByPerson = byPerson,
            Sprints = sprintBars,
            MeasuredFats = measured,
            AcceptableFats = ok,
            NotAcceptableFats = bad,
            ClosedLiquidations = closed,
            TotalSites = siteList.Count,
        };
    }

    /// <summary>
    /// Curva en S planificada (ponderada por la duración de cada actividad) contra el avance real. El real se arma con las
    /// fechas de fin reales registradas y, el día de hoy, con el avance actual del plan.
    /// </summary>
    private static List<CurvePoint> BuildCurve(WorkPlanView plan, DateOnly today)
    {
        var activities = plan.Activities.Select(a => a.Activity).ToList();
        if (activities.Count == 0 || plan.Start is null || plan.End is null)
        {
            return [];
        }

        var start = plan.Start.Value;
        var end = plan.End.Value > today ? plan.End.Value : today;
        var total = (double)activities.Sum(a => Weight(a));
        var points = new List<CurvePoint>();
        var totalDays = Math.Max(1, end.DayNumber - start.DayNumber);
        var step = Math.Max(1, (int)Math.Ceiling(totalDays / 24.0));

        for (var d = start; ; d = d.AddDays(step))
        {
            var date = d > end ? end : d;
            var planned = activities.Sum(a => Weight(a) * PlannedShare(a, date)) / total * 100;
            decimal? real = null;
            if (date <= today)
            {
                real = date == today
                    ? plan.Progress
                    : (decimal)(activities.Where(a => a.ActualEnd is not null && a.ActualEnd <= date).Sum(a => Weight(a)) / total * 100);
            }

            points.Add(new CurvePoint(date, Math.Round((decimal)planned, 1), real is null ? null : Math.Round(real.Value, 1)));
            if (date >= end) { break; }
        }

        // Si "hoy" cae entre dos puntos, se agrega para que el real llegue exacto al avance actual.
        if (today >= start && today <= end && points.All(p => p.Date != today))
        {
            var planned = activities.Sum(a => Weight(a) * PlannedShare(a, today)) / total * 100;
            points.Add(new CurvePoint(today, Math.Round((decimal)planned, 1), plan.Progress));
            points = points.OrderBy(p => p.Date).ToList();
        }

        return points;
    }

    private static double Weight(Domain.Entities.WorkPlanActivity a) => Math.Max(1, a.PlannedEnd.DayNumber - a.PlannedStart.DayNumber + 1);

    private static double PlannedShare(Domain.Entities.WorkPlanActivity a, DateOnly date)
    {
        var days = Math.Max(1, a.PlannedEnd.DayNumber - a.PlannedStart.DayNumber + 1);
        var done = date.DayNumber - a.PlannedStart.DayNumber + 1;
        return Math.Clamp(done / (double)days, 0, 1);
    }
}
