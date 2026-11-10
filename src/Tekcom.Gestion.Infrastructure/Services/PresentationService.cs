using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Infrastructure.Services;

/// <summary>Reúne en un solo lugar los datos de la sección Presentación a partir de los mismos servicios de las pantallas.</summary>
public sealed class PresentationService(
    IProjectService projects,
    IReferenceDataService reference,
    IWorkPlanService workPlan,
    ISiteService sites,
    IDashboardService dashboard,
    IInventoryService inventory,
    ITaskService tasks,
    IUserAdminService users,
    ISprintService sprints,
    IMeasurementService measurements,
    ILiquidationService liquidation,
    IDocumentService documents,
    ISnapshotService snapshots,
    IAttentionService attention,
    IQuantityService quantities,
    IMaterialService materials,
    IDateTimeProvider clock) : IPresentationService
{
    public async Task<PresentationData> GetAsync(int projectId, CancellationToken ct = default)
    {
        var project = await projects.GetAsync(projectId, ct) ?? throw new BusinessRuleException("Seleccione un proyecto.");
        await snapshots.CaptureAsync(projectId, ct); // asegura el punto de hoy en la curva real
        var history = await snapshots.ListAsync(projectId, ct);
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

        var stageDefs = (await reference.StageDefinitionsAsync(project.ProjectTypeId, ct)).OrderBy(x => x.SortOrder).ToList();
        var stageColumns = stageDefs.Select(x => new StageColumn(x.Code, x.Name)).ToList();
        var stageRows = siteList.OrderBy(s => s.Site.Code)
            .Select(s => new StageRow(s.Site.Code, s.Stages)).ToList();

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

        var docs = await documents.ListAsync(new DocumentFilter { ProjectId = projectId }, ct);
        var evidence = siteList.Where(s => s.Site.OdnFinalized).OrderBy(s => s.Site.Code)
            .Select(s =>
            {
                var own = docs.Where(x => x.SiteId == s.Site.Id).ToList();
                return new EvidenceRow(s.Site.Id, s.Site.Code, true,
                    own.Any(x => x.Kind == DocumentKind.Atp && x.IsSigned), own.Any(x => x.Kind == DocumentKind.Caes),
                    own.Count(x => x.Kind == DocumentKind.Photo), own.Count,
                    own.Where(x => x.Kind == DocumentKind.Photo && x.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(x => x.DocumentDate).Take(3).Select(x => x.Id).ToList());
            }).ToList();

        // Brigadas: avance de sus nodos y piezas entregadas/instaladas (solo unidades, no metros).
        var brigadeOf = siteList.ToDictionary(s => s.Site.Id, s => s.Site.Contractor?.Name ?? "Sin brigada");
        var allMovements = await inventory.MovementsAsync(new MovementFilter { ProjectId = projectId, IncludeVoided = false, Take = 20000 }, ct);
        var movements = allMovements.Where(m => m.Material?.Unit == "UND" && m.SiteId is not null).ToList();
        var brigades = siteList.GroupBy(s => brigadeOf[s.Site.Id])
            .Select(g => new BrigadeRow(g.Key, g.Count(), g.Count(x => x.Site.OdnFinalized),
                Math.Round(g.Average(x => x.Site.OdnFinalized ? 100 : x.ProgressPercent), 1),
                movements.Where(m => m.Type == MovementType.Delivery && brigadeOf.TryGetValue(m.SiteId!.Value, out var b) && b == g.Key).Sum(m => m.Quantity),
                movements.Where(m => m.Type == MovementType.Installed && brigadeOf.TryGetValue(m.SiteId!.Value, out var b2) && b2 == g.Key).Sum(m => m.Quantity),
                g.Count(x => x.Pace.Status == PaceStatus.Behind)))
            .OrderByDescending(b => b.AverageProgress).ToList();

        var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var weekly = Enumerable.Range(0, 8).Select(i => thisMonday.AddDays(-7 * (7 - i)))
            .Select(week =>
            {
                var inWeek = movements.Where(m => m.Type == MovementType.Installed && m.Date >= week && m.Date < week.AddDays(7)).ToList();
                return new WeekInstalled(week, inWeek.Sum(m => m.Quantity),
                    inWeek.GroupBy(m => brigadeOf.GetValueOrDefault(m.SiteId!.Value, "Sin brigada")).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity)));
            }).ToList();

        // Cable tendido en metros: los cables FC y MPO se cuentan por unidad, así que se multiplican por su largo.
        static decimal MetersOf(InventoryMovement m) =>
            m.Material is { Group: QuantityGroup.FcCable or QuantityGroup.MpoCable, LengthMeters: { } length } ? m.Quantity * length : 0;
        static bool IsFat(InventoryMovement m) => m.Material is not null && SiteSummary.VinetaGroups.Contains(m.Material.Group);

        var installedMoves = allMovements.Where(m => m.Type == MovementType.Installed && m.SiteId is not null).ToList();
        var metersBySite = installedMoves.GroupBy(m => m.SiteId!.Value).ToDictionary(g => g.Key, g => g.Sum(MetersOf));
        var piecesBySite = installedMoves.Where(m => m.Material?.Unit == "UND").GroupBy(m => m.SiteId!.Value).ToDictionary(g => g.Key, g => g.Sum(m => m.Quantity));

        var matrix = siteList.OrderBy(s => s.Site.Code).Select(s => new NodeMatrixRow(
            s.Site.Id, s.Site.Code, s.Site.OdnFinalized ? 100 : s.GlobalPercent, s.Pace.Status, s.Pace.DaysDelta,
            s.Stages.GetValueOrDefault(StageCode.Fusion), s.Stages.GetValueOrDefault(StageCode.FatMeasurements),
            s.ReadyToMeasure, s.Site.IsReadyToDeliver, s.VinetasInstalled, s.VinetasProjected,
            metersBySite.GetValueOrDefault(s.Site.Id), piecesBySite.GetValueOrDefault(s.Site.Id),
            s.Site.PlannedEnd, s.Pace.ProjectedEnd, s.Site.Contractor?.Name ?? "Sin brigada")).ToList();

        var production = installedMoves.GroupBy(m => m.Date).OrderBy(g => g.Key)
            .Select(g => new DayProduction(g.Key, g.Sum(MetersOf), g.Where(IsFat).Sum(m => m.Quantity), g.Where(m => m.Material?.Unit == "UND").Sum(m => m.Quantity)))
            .ToList();

        var feederCables = charts.Feeder.Where(f => f.Line.Kind is FeederItemKind.Cable12 or FeederItemKind.Cable24 or FeederItemKind.Cable48 or FeederItemKind.Cable96).ToList();
        var catalog = (await materials.ListAsync(includeInactive: true, ct: ct)).ToDictionary(m => m.Id);
        var projectedByMaterial = await quantities.GetProjectedByMaterialAsync(projectId, ct);
        var odnMetersProjected = projectedByMaterial.Sum(p =>
            catalog.TryGetValue(p.Key, out var mat) && mat is { Group: QuantityGroup.FcCable or QuantityGroup.MpoCable, LengthMeters: { } len } ? p.Value * len : 0);
        var curve = BuildCurve(plan, today, history);

        var weekAgo = history.Where(h => h.Date <= today.AddDays(-7)).OrderByDescending(h => h.Date).FirstOrDefault();
        var attentionItems = await attention.GetAsync(projectId, ct);

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
            Curve = curve,
            Matrix = matrix,
            Production = production,
            CableMetersInstalled = metersBySite.Values.Sum() + feederCables.Sum(f => f.Installed),
            CableMetersProjected = odnMetersProjected + feederCables.Sum(f => f.Planned),
            PiecesInstalled = piecesBySite.Values.Sum(),
            PlannedToday = curve.FirstOrDefault(p => p.Date == today)?.Planned,
            Nodes = siteList.OrderByDescending(s => s.Site.OdnFinalized ? 100 : s.ProgressPercent).ThenBy(s => s.Site.Code)
                .Select(s => new NodeBar(s.Site.Code, s.Site.OdnFinalized ? 100 : s.ProgressPercent, s.Pace.Status, s.Site.IsReadyToDeliver)).ToList(),
            StageColumns = stageColumns,
            StageRows = stageRows,
            Evidence = evidence,
            Brigades = brigades,
            WeeklyInstalled = weekly,
            Attention = attentionItems,
            ProgressWeekAgo = weekAgo?.ProgressPercent,
            HasSnapshots = history.Count >= 2,
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
    private static List<CurvePoint> BuildCurve(WorkPlanView plan, DateOnly today, List<ProgressSnapshot> history)
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
            if (date <= today && history.Count >= 2)
            {
                // Con fotos diarias, el real es la última foto hasta esa fecha (sin inventar historia).
                var shot = history.Where(h => h.Date <= date).OrderByDescending(h => h.Date).FirstOrDefault();
                real = date == today ? plan.Progress : shot?.ProgressPercent;
            }
            else if (date <= today)
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
