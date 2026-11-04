using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Infrastructure.Reports;

/// <summary>
/// Arma los reportes a partir de los mismos servicios que usa la pantalla (un dato, un lugar).
/// Cada reporte devuelve un <see cref="ReportDocument"/> que luego se exporta a Excel o PDF.
/// </summary>
public sealed class ReportService(
    IProjectService projects,
    IInventoryService inventory,
    ISiteService sites,
    ISprintService sprints,
    ILiquidationService liquidation,
    IProjectionService projection,
    IWorkPlanService workPlan,
    IDateTimeProvider clock) : IReportService
{
    public IReadOnlyList<ReportDefinition> Definitions { get; } =
    [
        new("materiales", "Consolidado de materiales", "Proyectado, recibido, entregado, instalado y stock de cada material."),
        new("entregas", "Entregas por nodo", "Cada entrega a contratista con su nodo, fecha y referencia."),
        new("inventario", "Inventario de bodega", "Stock actual con mínimo y estado de cada material."),
        new("liquidacion", "Acta de liquidación", "Comparación proyectado, entregado, instalado y devuelto de un nodo, con firmas.", NeedsSite: true),
        new("sprints", "Estado de sprints", "Avance, tiempo transcurrido y ritmo de cada sprint."),
        new("avance", "Avance por nodo", "Avance, ritmo, ODN, feeder y etapas de cada nodo."),
        new("plan", "Plan de trabajo", "Fases y actividades con fechas plan y real, avance, ritmo y dependencias."),
        new("movimientos", "Bitácora de movimientos", "Todos los movimientos de bodega, incluidos los anulados."),
    ];

    public async Task<ReportDocument> BuildAsync(ReportRequest request, CancellationToken ct = default)
    {
        var definition = Definitions.FirstOrDefault(d => d.Key == request.Key)
            ?? throw new BusinessRuleException("El reporte no existe.");
        var project = await projects.GetAsync(request.ProjectId, ct)
            ?? throw new BusinessRuleException("Seleccione un proyecto.");
        if (definition.NeedsSite && request.SiteId is null)
        {
            throw new BusinessRuleException("Este reporte necesita que elija un nodo.");
        }

        var document = new ReportDocument
        {
            Key = definition.Key,
            Title = definition.Name,
            ProjectName = project.Name,
            CutoffDate = request.CutoffDate ?? clock.Today,
            PreparedBy = request.PreparedBy,
        };

        switch (definition.Key)
        {
            case "materiales": await MaterialsAsync(document, project.Id, ct); break;
            case "entregas": await DeliveriesAsync(document, project.Id, ct); break;
            case "inventario": await InventoryAsync(document, project.Id, ct); break;
            case "liquidacion": await LiquidationAsync(document, request.SiteId!.Value, ct); break;
            case "sprints": await SprintsAsync(document, project.Id, ct); break;
            case "avance": await ProgressAsync(document, project.Id, ct); break;
            case "plan": await PlanAsync(document, project.Id, ct); break;
            case "movimientos": await MovementsAsync(document, project.Id, ct); break;
        }

        return document;
    }

    // ---------------------------------------------------------------- reportes

    private async Task MaterialsAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var rows = await inventory.StockAsync(projectId, document.CutoffDate, ct);
        var section = new ReportSection
        {
            Columns =
            [
                new("Material", "Material", ReportColumnType.Text, 4),
                new("Unit", "UM", ReportColumnType.Text, 0.8),
                new("Projected", "Proyectado", ReportColumnType.Decimal, 1.3),
                new("Received", "Recibido", ReportColumnType.Decimal, 1.2),
                new("Delivered", "Entregado", ReportColumnType.Decimal, 1.2),
                new("Returned", "Devuelto", ReportColumnType.Decimal, 1.2),
                new("Installed", "Instalado", ReportColumnType.Decimal, 1.2),
                new("Stock", "Stock", ReportColumnType.Decimal, 1.2),
            ],
        };
        foreach (var r in rows)
        {
            section.Rows.Add([r.Material.Name, r.Material.Unit, r.ProjectedTotal, r.Received, r.Delivered, r.Returned, r.Installed, r.Stock]);
        }

        document.Sections.Add(section);
        document.Notes.Add("Stock = ingresos − entregas + devoluciones ± ajustes, sin contar movimientos anulados.");
    }

    private async Task DeliveriesAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var movements = await inventory.MovementsAsync(new MovementFilter
        {
            ProjectId = projectId, Type = MovementType.Delivery, To = document.CutoffDate, IncludeVoided = false, Take = 5000,
        }, ct);

        var section = new ReportSection
        {
            Columns =
            [
                new("Site", "Nodo", ReportColumnType.Text, 1),
                new("Contractor", "Contratista", ReportColumnType.Text, 2.2),
                new("Material", "Material", ReportColumnType.Text, 3.5),
                new("Unit", "UM", ReportColumnType.Text, 0.8),
                new("Quantity", "Cantidad", ReportColumnType.Decimal, 1.1),
                new("Date", "Fecha", ReportColumnType.Date, 1.7),
                new("Reference", "Referencia", ReportColumnType.Text, 2),
            ],
        };
        foreach (var m in movements.OrderBy(m => m.Site?.Code).ThenBy(m => m.Date).ThenBy(m => m.Id))
        {
            section.Rows.Add([m.Site?.Code, m.Contractor?.Name, m.Material?.Name, m.Material?.Unit, m.Quantity, m.Date, m.Reference ?? m.Notes]);
        }

        document.Sections.Add(section);
    }

    private async Task InventoryAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var rows = await inventory.StockAsync(projectId, document.CutoffDate, ct);
        var section = new ReportSection
        {
            Columns =
            [
                new("Material", "Material", ReportColumnType.Text, 4),
                new("Code", "Código ERP", ReportColumnType.Text, 1.5),
                new("Unit", "UM", ReportColumnType.Text, 0.8),
                new("Stock", "Stock", ReportColumnType.Decimal, 1.2),
                new("Minimum", "Mínimo", ReportColumnType.Decimal, 1.2),
                new("Status", "Estado", ReportColumnType.Text, 1.8),
            ],
        };
        foreach (var r in rows)
        {
            var status = r.Status switch
            {
                StockStatus.Negative => "Revisar (stock negativo)",
                StockStatus.Shortage => $"Faltan {r.Shortage:0.###}",
                StockStatus.Low => "Bajo",
                _ => "OK",
            };
            section.Rows.Add([r.Material.Name, r.Material.Code, r.Material.Unit, r.Stock, r.Material.MinimumStock, status]);
        }

        document.Sections.Add(section);
    }

    private async Task LiquidationAsync(ReportDocument document, int siteId, CancellationToken ct)
    {
        var view = await liquidation.GetAsync(siteId, ct);
        var versions = await projection.VersionsAsync(siteId, ct);

        document.Subtitle = $"Nodo {view.Site.Code}";
        document.Facts.Add(("Nodo", view.Site.Code));
        document.Facts.Add(("Zona", view.Site.Zone ?? "—"));
        document.Facts.Add(("Contratista", view.Site.Contractor?.Name ?? "—"));
        document.Facts.Add(("Proyección de diseño", versions.Count == 0 ? "Sin proyección" : $"v{versions[0].Projection.Version} ({versions[0].Projection.Date:dd/MM/yyyy})"));
        document.Facts.Add(("Liquidación", view.IsClosed ? $"Cerrada el {view.Liquidation.ClosedOn:dd/MM/yyyy}" : "Abierta"));

        var materials = new ReportSection
        {
            Heading = "Comparación por material",
            Columns =
            [
                new("Material", "Material", ReportColumnType.Text, 3.6),
                new("Unit", "UM", ReportColumnType.Text, 0.7),
                new("Projected", "Proyectado", ReportColumnType.Decimal, 1.1),
                new("Delivered", "Entregado", ReportColumnType.Decimal, 1.1),
                new("Installed", "Instalado", ReportColumnType.Decimal, 1.1),
                new("Surplus", "Sobrante", ReportColumnType.Decimal, 1.1),
                new("Returned", "Devuelto", ReportColumnType.Decimal, 1.1),
                new("Pending", "Pendiente", ReportColumnType.Decimal, 1.1),
                new("Justified", "Justificado", ReportColumnType.Decimal, 1.1),
                new("Variation", "Var. diseño", ReportColumnType.Percent, 1.1),
            ],
        };

        foreach (var r in view.Rows.Where(r => r.Quantity.Projected > 0 || r.Quantity.Delivered > 0 || r.Quantity.Returned > 0))
        {
            var q = r.Quantity;
            materials.Rows.Add([q.Material.Name, q.Material.Unit, q.Projected, q.Delivered, q.Installed,
                Math.Max(0, q.Surplus), q.Returned, Math.Max(0, q.PendingReturn), r.Justified,
                q.Projected == 0 ? null : q.VariationPercent / 100m]);
        }

        document.Sections.Add(materials);

        if (view.Justifications.Count > 0)
        {
            var justifications = new ReportSection
            {
                Heading = "Justificaciones del material pendiente",
                Columns =
                [
                    new("Material", "Material", ReportColumnType.Text, 3),
                    new("Quantity", "Cantidad", ReportColumnType.Decimal, 1),
                    new("Reason", "Motivo", ReportColumnType.Text, 4),
                    new("By", "Registró", ReportColumnType.Text, 2),
                ],
            };
            foreach (var j in view.Justifications)
            {
                justifications.Rows.Add([j.Material?.Name, j.Quantity, j.Reason, j.CreatedBy]);
            }

            document.Sections.Add(justifications);
        }

        if (!string.IsNullOrWhiteSpace(view.Liquidation.Observations))
        {
            document.Notes.Add($"Observaciones: {view.Liquidation.Observations}");
        }

        document.Notes.Add($"Sobrante = entregado − instalado · Pendiente = sobrante − devuelto · Precisión de diseño: {view.DesignAccuracy:0}%.");
        document.Signatures.Add("Entregó (TEK-COM)");
        document.Signatures.Add($"Recibió ({view.Site.Contractor?.Name ?? "Contratista"})");
        document.Signatures.Add("Supervisor");
    }

    private async Task SprintsAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var list = await sprints.ListAsync(projectId, ct);
        var section = new ReportSection
        {
            Columns =
            [
                new("Sprint", "Sprint", ReportColumnType.Text, 2),
                new("Start", "Inicio", ReportColumnType.Date, 1.7),
                new("End", "Fin", ReportColumnType.Date, 1.7),
                new("Status", "Estado", ReportColumnType.Text, 1.3),
                new("Tasks", "Tareas", ReportColumnType.Integer, 0.9),
                new("Done", "Hechas", ReportColumnType.Integer, 0.9),
                new("Progress", "% hecho", ReportColumnType.Percent, 1),
                new("Elapsed", "% tiempo", ReportColumnType.Percent, 1),
                new("Pace", "Ritmo", ReportColumnType.Text, 1.3),
                new("Days", "Días ±", ReportColumnType.Integer, 0.9),
            ],
        };
        foreach (var s in list.OrderBy(s => s.Sprint.StartDate))
        {
            section.Rows.Add([s.Sprint.Name, s.Sprint.StartDate, s.Sprint.EndDate, SprintStatusName(s.Sprint.Status), s.Total, s.Done,
                s.ProgressPercent / 100m, s.Pace.ElapsedPercent / 100m,
                s.Sprint.Status == SprintStatus.Closed ? "—" : PaceName(s.Pace.Status), s.Sprint.Status == SprintStatus.Closed ? null : s.Pace.DaysDelta]);
        }

        document.Sections.Add(section);
        document.Notes.Add("Días ±: positivo = adelanto, negativo = atraso respecto del tiempo transcurrido.");
    }

    private async Task ProgressAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var list = await sites.ListAsync(projectId, ct);
        var section = new ReportSection
        {
            Columns =
            [
                new("Site", "Nodo", ReportColumnType.Text, 1),
                new("Contractor", "Brigada", ReportColumnType.Text, 2.2),
                new("Supervisor", "Supervisor", ReportColumnType.Text, 2),
                new("Start", "Inicio", ReportColumnType.Date, 1.7),
                new("End", "Fin", ReportColumnType.Date, 1.7),
                new("Progress", "Avance", ReportColumnType.Percent, 1),
                new("Elapsed", "Tiempo", ReportColumnType.Percent, 1),
                new("Pace", "Ritmo", ReportColumnType.Text, 1.3),
                new("Fat", "FATs", ReportColumnType.Percent, 1),
                new("Stages", "Etapas", ReportColumnType.Text, 1),
                new("Odn", "ODN", ReportColumnType.Text, 1.2),
                new("Feeder", "Feeder", ReportColumnType.Text, 1.2),
                new("Ready", "Listo p/ entregar", ReportColumnType.Text, 1.3),
            ],
        };
        foreach (var s in list)
        {
            var counted = s.Stages.Count;
            var finished = s.Stages.Count(kv => kv.Value == StageStatus.Finished);
            section.Rows.Add([s.Site.Code, s.Site.Contractor?.Name, s.Site.Supervisor?.Name, s.Site.PlannedStart, s.Site.PlannedEnd,
                s.ProgressPercent / 100m, s.Pace.ElapsedPercent / 100m, PaceName(s.Pace.Status), s.FatPercent / 100m,
                $"{finished}/{counted}", s.Site.OdnFinalized ? "Finalizada" : "Pendiente", FeederName(s.Site.FeederStatus),
                s.Site.IsReadyToDeliver ? "Sí" : "No"]);
        }

        document.Sections.Add(section);
        document.Notes.Add($"Nodos: {list.Count} · ODN finalizadas: {list.Count(s => s.Site.OdnFinalized)} · " +
                           $"Listos para entregar: {list.Count(s => s.Site.IsReadyToDeliver)}.");
        document.Notes.Add("El avance se pondera por los días planificados de cada etapa.");
    }

    private async Task PlanAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var plan = await workPlan.GetAsync(projectId, ct);
        document.Facts.Add(("Avance del plan", $"{plan.Progress:0.#}%"));
        document.Facts.Add(("Tiempo transcurrido", $"{plan.Pace.ElapsedPercent:0.#}%"));
        document.Facts.Add(("Ritmo", plan.Pace.Status == PaceStatus.Unknown ? "Sin fechas" :
            $"{PaceName(plan.Pace.Status)}{(plan.Pace.DaysDelta != 0 ? $" ({Math.Abs(plan.Pace.DaysDelta)} días)" : string.Empty)}"));
        document.Facts.Add(("Cierre planificado", plan.End is null ? "—" : plan.End.Value.ToString("dd/MM/yyyy")));
        document.Facts.Add(("Cierre proyectado", plan.Pace.ProjectedEnd is null ? "—" : plan.Pace.ProjectedEnd.Value.ToString("dd/MM/yyyy")));

        var section = new ReportSection
        {
            Columns =
            [
                new("Phase", "Fase", ReportColumnType.Text, 2.4),
                new("Activity", "Actividad", ReportColumnType.Text, 3.2),
                new("PlannedStart", "Inicio plan", ReportColumnType.Date, 1.7),
                new("PlannedEnd", "Fin plan", ReportColumnType.Date, 1.7),
                new("ActualStart", "Inicio real", ReportColumnType.Date, 1.7),
                new("ActualEnd", "Fin real", ReportColumnType.Date, 1.7),
                new("Progress", "Avance", ReportColumnType.Percent, 1),
                new("Status", "Estado", ReportColumnType.Text, 1.5),
                new("DependsOn", "Depende de", ReportColumnType.Text, 2.6),
            ],
        };
        foreach (var phase in plan.Phases)
        {
            foreach (var a in phase.Activities)
            {
                section.Rows.Add([phase.Phase.Name, a.Activity.Name + (a.Activity.IsMilestone ? " (hito)" : string.Empty),
                    a.Activity.PlannedStart, a.Activity.IsMilestone ? null : a.Activity.PlannedEnd, a.Activity.ActualStart, a.Activity.ActualEnd,
                    a.Progress / 100m, PaceName(a.Pace.Status), string.Join(", ", a.Predecessors.Select(p => p.Name))]);
            }
        }

        document.Sections.Add(section);
        document.Notes.Add("El avance de la fase y del plan se pondera por la duración de sus actividades..");
    }

    private async Task MovementsAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var movements = await inventory.MovementsAsync(new MovementFilter
        {
            ProjectId = projectId, To = document.CutoffDate, IncludeVoided = true, Take = 5000,
        }, ct);

        var section = new ReportSection
        {
            Columns =
            [
                new("Date", "Fecha", ReportColumnType.Date, 1.7),
                new("Type", "Tipo", ReportColumnType.Text, 1.2),
                new("Material", "Material", ReportColumnType.Text, 3.3),
                new("Quantity", "Cantidad", ReportColumnType.Decimal, 1),
                new("Site", "Nodo", ReportColumnType.Text, 1),
                new("Contractor", "Contratista", ReportColumnType.Text, 2),
                new("Reference", "Referencia", ReportColumnType.Text, 2),
                new("Status", "Estado", ReportColumnType.Text, 2.2),
                new("By", "Registró", ReportColumnType.Text, 2),
            ],
        };
        foreach (var m in movements)
        {
            var signed = m.Type == MovementType.Adjustment && !m.IsIncrease ? -m.Quantity : m.Quantity;
            section.Rows.Add([m.Date, MovementName(m.Type), m.Material?.Name, signed, m.Site?.Code, m.Contractor?.Name,
                m.Reference ?? m.Batch ?? m.Notes, m.IsVoided ? $"Anulado: {m.VoidReason}" : "Vigente", m.CreatedBy]);
        }

        document.Sections.Add(section);
    }

    // ---------------------------------------------------------------- textos

    private static string MovementName(MovementType type) => type switch
    {
        MovementType.Receipt => "Ingreso",
        MovementType.Delivery => "Entrega",
        MovementType.Return => "Devolución",
        MovementType.Adjustment => "Ajuste",
        MovementType.Installed => "Instalado",
        _ => type.ToString(),
    };

    private static string PaceName(PaceStatus status) => status switch
    {
        PaceStatus.Ahead => "Adelantado",
        PaceStatus.OnTime => "En tiempo",
        PaceStatus.Behind => "Atrasado",
        PaceStatus.Completed => "Finalizado",
        _ => "Sin fechas",
    };

    private static string SprintStatusName(SprintStatus status) => status switch
    {
        SprintStatus.Planned => "Planificado",
        SprintStatus.Active => "Activo",
        _ => "Cerrado",
    };

    private static string FeederName(FeederStatus status) => status switch
    {
        FeederStatus.Finished => "Finalizado",
        FeederStatus.InProgress => "En proceso",
        _ => "Pendiente",
    };
}
