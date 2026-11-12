using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Application.Models;

public readonly record struct QuantityPair(decimal Projected, decimal Installed)
{
    public decimal Pending => Projected - Installed;
    public decimal Percent => Projected <= 0 ? 0 : Math.Round(Installed / Projected * 100m, 1);
}

/// <summary>Cantidades de un material en un nodo: proyectado vigente, entregado, instalado y devuelto.</summary>
public sealed class MaterialQuantity
{
    public required Material Material { get; init; }

    /// <summary>Cantidad de la última versión de la proyección de diseño.</summary>
    public decimal DesignProjected { get; init; }

    /// <summary>Suma de los ajustes (suma o resta) al proyectado.</summary>
    public decimal Adjustments { get; init; }
    public decimal Delivered { get; init; }
    public decimal Installed { get; init; }
    public decimal Returned { get; init; }

    /// <summary>Proyectado vigente = diseño ± ajustes.</summary>
    public decimal Projected => DesignProjected + Adjustments;

    /// <summary>Sobrante = entregado − instalado.</summary>
    public decimal Surplus => Delivered - Installed;

    /// <summary>Pendiente por devolver = sobrante − devuelto.</summary>
    public decimal PendingReturn => Surplus - Returned;

    /// <summary>Variación = instalado − proyectado.</summary>
    public decimal Variation => Installed - Projected;

    public decimal VariationPercent => Projected == 0 ? 0 : Math.Round(Variation / Projected * 100m, 1);
}

public sealed class SiteSummary
{
    public required Site Site { get; init; }
    public decimal ProgressPercent { get; init; }
    public required PaceResult Pace { get; init; }
    public required IReadOnlyDictionary<StageCode, StageStatus> Stages { get; init; }
    public required IReadOnlyDictionary<QuantityGroup, QuantityPair> Quantities { get; init; }

    /// <summary>Avance de FATs (sub-box, end-box, Hub-box y X-box) como en el Excel.</summary>
    public decimal FatPercent { get; init; }

    /// <summary>Los elementos de red que llevan viñeta: sub-box, end-box, Hub-box y X-box.</summary>
    public static readonly QuantityGroup[] VinetaGroups = [QuantityGroup.SubBox, QuantityGroup.EndBox, QuantityGroup.HubBox, QuantityGroup.XBox];

    /// <summary>Total de viñetas del nodo = suma de lo instalado de todos los elementos de red (columna VIÑETAS del Excel).</summary>
    public decimal VinetasInstalled => VinetaGroups.Sum(g => Quantities.TryGetValue(g, out var q) ? q.Installed : 0);

    /// <summary>Viñetas proyectadas = suma de lo proyectado de todos los elementos de red.</summary>
    public decimal VinetasProjected => VinetaGroups.Sum(g => Quantities.TryGetValue(g, out var q) ? q.Projected : 0);

    /// <summary>
    /// Avance por piezas instaladas: cables, FATs, fusiones y cruces instalados sobre lo proyectado del nodo
    /// (sin pasarse de lo proyectado en cada grupo). Null si el nodo no tiene proyección.
    /// </summary>
    public decimal? PiecesPercent { get; init; }

    /// <summary>Avance global del nodo: por piezas instaladas si hay proyección; si no, el de las etapas.</summary>
    public decimal GlobalPercent => PiecesPercent ?? ProgressPercent;

    /// <summary>Con el % de FATs instaladas definido en el proyecto y sin mediciones finalizadas.</summary>
    public bool ReadyToMeasure { get; init; }
}

public sealed record UserListItem(
    string Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool IsLockedOut,
    string? Phone = null);

public sealed class StockRow
{
    public required Material Material { get; init; }
    public decimal Received { get; init; }
    public decimal Delivered { get; init; }
    public decimal Returned { get; init; }

    /// <summary>Total instalado en campo (nodos y feeder). No afecta el stock de bodega.</summary>
    public decimal Installed { get; init; }

    /// <summary>Suma neta de los ajustes (positivos y negativos).</summary>
    public decimal AdjustmentNet { get; init; }

    /// <summary>Total proyectado de todos los nodos del proyecto (diseño ± ajustes).</summary>
    public decimal ProjectedTotal { get; init; }

    /// <summary>Stock = ingresos − entregas + devoluciones ± ajustes. Nunca se guarda: se calcula.</summary>
    public decimal Stock => Received - Delivered + Returned + AdjustmentNet;

    /// <summary>Lo que falta recibir para cubrir el diseño.</summary>
    public decimal Shortage => Math.Max(0, ProjectedTotal - Received);

    public StockStatus Status =>
        Stock < 0 ? StockStatus.Negative
        : Shortage > 0 ? StockStatus.Shortage
        : Material.MinimumStock > 0 && Stock <= Material.MinimumStock ? StockStatus.Low
        : StockStatus.Ok;
}

public sealed class MovementFilter
{
    public int ProjectId { get; set; }
    public MovementType? Type { get; set; }
    public int? MaterialId { get; set; }
    public int? SiteId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public bool IncludeVoided { get; set; } = true;
    public int Take { get; set; } = 500;
}

public sealed class LiquidationRow
{
    public required MaterialQuantity Quantity { get; init; }
    public decimal Justified { get; init; }

    /// <summary>Pendiente por devolver que todavía no está justificado.</summary>
    public decimal Unresolved => Math.Max(0, Quantity.PendingReturn - Justified);
}

public sealed class LiquidationView
{
    public required Liquidation Liquidation { get; init; }
    public required Site Site { get; init; }
    public required List<LiquidationRow> Rows { get; init; }
    public required List<LiquidationJustification> Justifications { get; init; }

    public bool IsClosed => Liquidation.Status == LiquidationStatus.Closed;
    public decimal TotalSurplus => Rows.Sum(r => Math.Max(0, r.Quantity.Surplus));
    public decimal TotalPending => Rows.Sum(r => Math.Max(0, r.Quantity.PendingReturn));
    public decimal TotalUnresolved => Rows.Sum(r => r.Unresolved);
    public int WithVariation => Rows.Count(r => r.Quantity.Projected > 0 && r.Quantity.Variation != 0);

    /// <summary>
    /// Precisión de diseño: 100 − (Σ|instalado − proyectado| ÷ Σ proyectado × 100), entre 0 y 100.
    /// Solo cuenta los materiales que tienen proyección (lo instalado sin proyectar no entra en la medida).
    /// </summary>
    public decimal DesignAccuracy
    {
        get
        {
            var projectedRows = Rows.Where(r => r.Quantity.Projected > 0).ToList();
            var projected = projectedRows.Sum(r => r.Quantity.Projected);
            if (projected <= 0)
            {
                return 0;
            }

            var deviation = projectedRows.Sum(r => Math.Abs(r.Quantity.Installed - r.Quantity.Projected));
            return Math.Clamp(Math.Round(100m - deviation / projected * 100m, 0), 0, 100);
        }
    }

    public bool CanClose => !IsClosed && TotalUnresolved == 0;

    public string? BlockingMessage
    {
        get
        {
            if (IsClosed || TotalUnresolved == 0)
            {
                return null;
            }

            var details = string.Join(", ", Rows.Where(r => r.Unresolved > 0)
                .Select(r => $"{r.Quantity.Material.Name} {r.Unresolved:0.###}"));
            return $"No se puede cerrar el nodo: hay {TotalUnresolved:0.###} unidades pendientes por devolver o justificar ({details}).";
        }
    }
}

public sealed record MeasurementSummary(int Measured, int Acceptable, int NotAcceptable);

public sealed class PlanActivityView
{
    public required WorkPlanActivity Activity { get; init; }
    public required string PhaseName { get; init; }

    /// <summary>Avance efectivo: el del nodo si está vinculada a uno; si no, el manual.</summary>
    public decimal Progress { get; init; }
    public required PaceResult Pace { get; init; }
    public required List<WorkPlanActivity> Predecessors { get; init; }

    /// <summary>Hay una predecesora que termina después de que esta actividad empieza.</summary>
    public bool StartsBeforePredecessor { get; init; }

    public bool IsDone => Activity.IsMilestone ? Activity.ActualEnd is not null || Progress >= 100 : Progress >= 100;
}

public sealed class PlanPhaseView
{
    public required WorkPlanPhase Phase { get; init; }
    public required List<PlanActivityView> Activities { get; init; }
    public DateOnly? Start => Activities.Count == 0 ? null : Activities.Min(a => a.Activity.PlannedStart);
    public DateOnly? End => Activities.Count == 0 ? null : Activities.Max(a => a.Activity.PlannedEnd);
    public decimal Progress { get; init; }
    public required PaceResult Pace { get; init; }
}

public sealed class WorkPlanView
{
    public required List<PlanPhaseView> Phases { get; init; }
    public IEnumerable<PlanActivityView> Activities => Phases.SelectMany(p => p.Activities);
    public DateOnly? Start => Activities.Any() ? Activities.Min(a => a.Activity.PlannedStart) : null;
    public DateOnly? End => Activities.Any() ? Activities.Max(a => a.Activity.PlannedEnd) : null;
    public decimal Progress { get; init; }
    public required PaceResult Pace { get; init; }
    public bool IsEmpty => Phases.Count == 0;
}

public sealed record DashboardProjectRow(Project Project, decimal Progress, PaceResult Pace, int Sites, int ReadySites);

public sealed record DashboardSprint(string ProjectName, SprintSummary Summary);

public sealed class DashboardData
{
    public int ActiveProjects { get; init; }
    public int OnTimeProjects { get; init; }
    public int OverdueTasks { get; init; }
    public int OverdueSinceYesterday { get; init; }
    public int TasksThisWeek { get; init; }
    public int TasksThisWeekDone { get; init; }
    public int LowStockMaterials { get; init; }
    public required List<DashboardProjectRow> Projects { get; init; }
    public required List<TaskItem> DueSoon { get; init; }
    public DashboardSprint? CurrentSprint { get; init; }
}

public sealed class ProjectCharts
{
    public required Dictionary<QuantityGroup, QuantityPair> Installation { get; init; }
    public int SitesFinished { get; init; }
    public int SitesInProgress { get; init; }
    public int SitesNotStarted { get; init; }
    public int SitesReady { get; init; }
    public required Dictionary<TaskItemStatus, int> TasksByStatus { get; init; }
    public required List<FeederProgress> Feeder { get; init; }
}

// ---------------------------------------------------------------- Presentación

public sealed record CurvePoint(DateOnly Date, decimal Planned, decimal? Real);

public sealed record NodeBar(string Code, decimal Progress, PaceStatus Pace, bool Ready);

public sealed record MaterialBar(string Name, string Unit, decimal Received, decimal Delivered, decimal Installed, decimal Returned);

public sealed record SprintBar(string Name, decimal Progress, decimal Elapsed, PaceStatus Pace, bool Closed);

public sealed record NamedCount(string Name, int Count);

public enum AttentionSeverity { Info = 0, Warning = 1, Critical = 2 }

public sealed record AttentionItem(string Title, string Detail, string Href, AttentionSeverity Severity, int Count);

public sealed record EvidenceRow(int SiteId, string SiteCode, bool Finalized, bool SignedAtp, bool HasCaes, int Photos, int Documents, List<int> PhotoIds);

public sealed record BrigadeRow(string Name, int Sites, int FinishedSites, decimal AverageProgress, decimal Delivered, decimal Installed, int BehindSites = 0);

/// <summary>Una fila de la matriz técnica por nodo de la Presentación.</summary>
public sealed record NodeMatrixRow(
    int SiteId, string Code, decimal Progress, PaceStatus Pace, int DaysDelta, StageStatus Fusion, StageStatus Measurement,
    bool ReadyToMeasure, bool ReadyToDeliver, decimal FatsInstalled, decimal FatsProjected, decimal CableMeters, decimal Pieces,
    DateOnly? PlannedEnd, DateOnly? ProjectedEnd, string Brigade, int? HomesPassed = null, string? Supervisor = null,
    IReadOnlyDictionary<QuantityGroup, QuantityPair>? Quantities = null, bool OdnClosed = false,
    decimal ElapsedPercent = 0, DateOnly? PlannedStart = null, PermitStatus Permit = PermitStatus.RequestPending,
    PermitStatus MunicipalPermit = PermitStatus.RequestPending,
    DateOnly? ElectricalRequestedOn = null, DateOnly? ElectricalApprovedOn = null, DateOnly? MunicipalRequestedOn = null, DateOnly? MunicipalApprovedOn = null)
{
    /// <summary>Finalizado de verdad: ODN cerrada, o todo instalado y con las mediciones de FATs terminadas.</summary>
    public bool Completed => Progress >= 100 && (OdnClosed || Measurement == StageStatus.Finished);

    /// <summary>Todo instalado, pero todavía faltan las mediciones: obra terminada, no finalizado.</summary>
    public bool BuiltOnly => Progress >= 100 && !Completed;

    /// <summary>Piezas instaladas por kilómetro de cable tendido (null si aún no hay cable).</summary>
    public decimal? Density => CableMeters <= 0 ? null : Math.Round(Pieces / (CableMeters / 1000m), 1);
    public decimal FatPercent => FatsProjected <= 0 ? 0 : Math.Round(FatsInstalled / FatsProjected * 100m, 0);
}

/// <summary>Lo instalado en un día: metros de cable (FC y MPO), FATs y piezas en unidades.</summary>
public sealed record DayProduction(DateOnly Date, decimal Meters, decimal Fats, decimal Pieces);

/// <summary>Lo instalado por un nodo en un día (para poder filtrar la producción por brigada, supervisor o estado).</summary>
public sealed record SiteDayProduction(int SiteId, DateOnly Date, decimal Meters, decimal Fats, decimal Pieces);

public sealed record WeekInstalled(DateOnly WeekStart, decimal Total, Dictionary<string, decimal> ByBrigade);

public sealed record ProjectComparison(int ProjectId, string Name, string Type, ProjectStatus Status, decimal Progress, PaceResult Pace, int Sites, int ReadySites, int OverdueTasks);

public sealed record StageColumn(StageCode Code, string Name);

public sealed record StageRow(string Site, IReadOnlyDictionary<StageCode, StageStatus> Stages);

public sealed class PresentationData
{
    public required string ProjectName { get; init; }
    public required DateOnly Today { get; init; }
    public decimal Progress { get; init; }
    public required PaceResult Pace { get; init; }
    public DateOnly? PlanStart { get; init; }
    public DateOnly? PlanEnd { get; init; }
    public required List<CurvePoint> Curve { get; init; }
    public required List<NodeBar> Nodes { get; init; }
    public required List<StageColumn> StageColumns { get; init; }
    public required List<StageRow> StageRows { get; init; }
    public required List<EvidenceRow> Evidence { get; init; }
    public required List<BrigadeRow> Brigades { get; init; }
    public required List<WeekInstalled> WeeklyInstalled { get; init; }
    public required List<AttentionItem> Attention { get; init; }
    public decimal? ProgressWeekAgo { get; init; }
    public bool HasSnapshots { get; init; }
    public required ProjectCharts Charts { get; init; }
    public required List<MaterialBar> Materials { get; init; }
    public required List<NamedCount> TasksByPerson { get; init; }
    public required List<SprintBar> Sprints { get; init; }
    public int MeasuredFats { get; init; }
    public int AcceptableFats { get; init; }
    public int NotAcceptableFats { get; init; }
    public int ClosedLiquidations { get; init; }
    public int TotalSites { get; init; }

    /// <summary>Matriz técnica: una fila por nodo con su avance, fusión, mediciones, FATs, cable y brigada.</summary>
    public List<NodeMatrixRow> Matrix { get; init; } = [];

    /// <summary>Lo instalado por día (solo días con registros), para la tendencia diaria, semanal y mensual.</summary>
    public List<DayProduction> Production { get; init; } = [];

    /// <summary>La misma producción separada por nodo.</summary>
    public List<SiteDayProduction> ProductionBySite { get; init; } = [];

    /// <summary>Metros de cable tendido: FC y MPO de los nodos (unidades × su largo) más la fibra del feeder.</summary>
    public decimal CableMetersInstalled { get; init; }
    public decimal CableMetersProjected { get; init; }

    /// <summary>Piezas instaladas en unidades (cajas, herrajes…), sin metros.</summary>
    public decimal PiecesInstalled { get; init; }

    /// <summary>Avance planificado para hoy según el plan de trabajo (null si no hay plan).</summary>
    public decimal? PlannedToday { get; init; }

    /// <summary>Home passes equivalentes construidos por semana (solo semanas con instalación).</summary>
    public List<WeekHp> HpByWeek { get; init; } = [];

    /// <summary>Fusiones instaladas por día (todos los nodos).</summary>
    public List<DayCount> FusionByDay { get; init; } = [];
}

public sealed record WeekHp(DateOnly WeekStart, decimal Hp);

public sealed record DayCount(DateOnly Date, decimal Quantity);
