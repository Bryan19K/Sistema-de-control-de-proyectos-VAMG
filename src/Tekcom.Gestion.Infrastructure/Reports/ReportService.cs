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
    IEquipmentService equipment,
    IMinuteService minutes,
    IDocumentService documents,
    IMeasurementService measurements,
    IUserAdminService users,
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
        new("atp", "ATP del nodo", "Instalación, etapas, mediciones de potencia, fusiones y registro de documentos y fotos del nodo, con firmas.", NeedsSite: true),
        new("seriales", "Equipos y seriales SFP", "OLT y equipos con sus tarjetas, y cada SFP con serial, ubicación y estado."),
        new("minutas", "Minutas y pendientes", "Minutas del proyecto y pendientes abiertos con responsable y fecha."),
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
            case "atp": await AtpAsync(document, project.Id, request.SiteId!.Value, ct); break;
            case "seriales": await SerialsAsync(document, project.Id, ct); break;
            case "minutas": await MinutesAsync(document, project.Id, ct); break;
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

    private async Task AtpAsync(ReportDocument document, int projectId, int siteId, CancellationToken ct)
    {
        var summary = await sites.GetSummaryAsync(siteId, ct) ?? throw new BusinessRuleException("El nodo no existe.");
        if (summary.Site.ProjectId != projectId)
        {
            throw new BusinessRuleException("El nodo no pertenece al proyecto elegido.");
        }

        var site = await sites.GetAsync(siteId, ct) ?? summary.Site;
        var stages = await sites.StagesAsync(siteId, ct);
        var fat = await measurements.SummaryAsync(siteId, ct);
        var docs = await documents.ListAsync(new DocumentFilter { ProjectId = projectId, SiteId = siteId }, ct);

        document.Subtitle = $"Nodo {site.Code}";
        document.Facts.Add(("Nodo", site.Code));
        document.Facts.Add(("Zona", site.Zone ?? "—"));
        document.Facts.Add(("Contratista", site.Contractor?.Name ?? "—"));
        document.Facts.Add(("Supervisor", site.Supervisor?.Name ?? "—"));
        document.Facts.Add(("Avance del nodo", $"{summary.ProgressPercent:0.#}%"));
        document.Facts.Add(("ODN", site.OdnFinalized ? "Finalizada" : "Pendiente"));
        document.Facts.Add(("Feeder", FeederName(site.FeederStatus)));
        document.Facts.Add(("Mediciones", fat.Measured == 0 ? "Sin mediciones" : $"{fat.Measured} FAT medidas ({fat.Acceptable} aceptables, {fat.NotAcceptable} no aceptables)"));

        var installed = new ReportSection
        {
            Heading = "Instalación del nodo",
            Columns =
            [
                new("Group", "Concepto", ReportColumnType.Text, 3),
                new("Projected", "Proyectado", ReportColumnType.Decimal, 1.2),
                new("Installed", "Instalado", ReportColumnType.Decimal, 1.2),
                new("Pending", "Pendiente", ReportColumnType.Decimal, 1.2),
                new("Percent", "Avance", ReportColumnType.Percent, 1),
            ],
        };
        foreach (var (group, label) in AtpGroups)
        {
            if (summary.Quantities.TryGetValue(group, out var q) && q.Projected > 0)
            {
                installed.Rows.Add([label, q.Projected, q.Installed, Math.Max(0, q.Pending), q.Percent / 100m]);
            }
        }

        document.Sections.Add(installed);

        var stageSection = new ReportSection
        {
            Heading = "Etapas del flujo",
            Columns =
            [
                new("Stage", "Etapa", ReportColumnType.Text, 3),
                new("Status", "Estado", ReportColumnType.Text, 1.6),
                new("Start", "Inicio real", ReportColumnType.Date, 1.4),
                new("End", "Fin real", ReportColumnType.Date, 1.4),
            ],
        };
        foreach (var stage in stages.Where(st => st.Status != StageStatus.NotApplicable).OrderBy(st => st.Definition?.SortOrder ?? 0))
        {
            stageSection.Rows.Add([stage.Definition?.Name ?? stage.Code.ToString(), SpanishNames.Of(stage.Status), stage.ActualStart, stage.ActualEnd]);
        }

        document.Sections.Add(stageSection);

        var power = await measurements.ListAsync(siteId, ct);
        if (power.Count > 0)
        {
            var powerSection = new ReportSection
            {
                Heading = "Mediciones de potencia por FAT",
                Columns =
                [
                    new("Fat", "FAT", ReportColumnType.Text, 2),
                    new("Date", "Fecha", ReportColumnType.Date, 1.4),
                    new("Dbm", "Potencia (dBm)", ReportColumnType.Decimal, 1.4),
                    new("Result", "Resultado", ReportColumnType.Text, 1.4),
                    new("Notes", "Notas", ReportColumnType.Text, 3),
                ],
            };
            foreach (var m in power.OrderBy(m => m.FatCode).ThenBy(m => m.Date))
            {
                powerSection.Rows.Add([m.FatCode, m.Date, m.PowerDbm, m.IsAcceptable ? "Aceptable" : "No aceptable", m.Notes]);
            }

            document.Sections.Add(powerSection);
        }

        var docSection = new ReportSection
        {
            Heading = "Documentos y registro fotográfico",
            Columns =
            [
                new("Kind", "Tipo", ReportColumnType.Text, 1.2),
                new("Title", "Título", ReportColumnType.Text, 3.4),
                new("Date", "Fecha", ReportColumnType.Date, 1.4),
                new("Signed", "Firmado", ReportColumnType.Text, 1),
                new("File", "Archivo", ReportColumnType.Text, 3),
                new("By", "Subió", ReportColumnType.Text, 2),
            ],
        };
        foreach (var d in docs.OrderBy(d => d.Kind).ThenBy(d => d.DocumentDate))
        {
            docSection.Rows.Add([SpanishNames.Of(d.Kind), d.Title, d.DocumentDate, d.IsSigned ? "Sí" : "No", d.FileName, d.CreatedBy]);
        }

        document.Sections.Add(docSection);
        if (docs.Count == 0)
        {
            document.Notes.Add("El nodo todavía no tiene documentos ni fotos adjuntos.");
        }

        document.Signatures.AddRange(["Contratista", "Supervisor TEK-COM", "Cliente"]);
    }

    private static readonly (QuantityGroup Group, string Label)[] AtpGroups =
    [
        (QuantityGroup.FcCable, "Cable FC (m)"),
        (QuantityGroup.MpoCable, "Cable MPO (m)"),
        (QuantityGroup.SubBox, "Sub-box (NAP de paso)"),
        (QuantityGroup.EndBox, "End-box (NAP final)"),
        (QuantityGroup.HubBox, "Hub-box"),
        (QuantityGroup.XBox, "X-box"),
        (QuantityGroup.Fusion, "Fusiones"),
        (QuantityGroup.Crossing, "Cruces americanos"),
    ];

    private async Task SerialsAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var all = await equipment.ListAsync(projectId, null, ct);
        var equipmentSection = new ReportSection
        {
            Heading = "Equipos y tarjetas",
            Columns =
            [
                new("Equipment", "Equipo", ReportColumnType.Text, 2.6),
                new("Kind", "Tipo", ReportColumnType.Text, 1),
                new("Site", "Nodo", ReportColumnType.Text, 1),
                new("Model", "Marca / modelo", ReportColumnType.Text, 2.4),
                new("Serial", "Serial", ReportColumnType.Text, 2.2),
                new("Slot", "Ranura", ReportColumnType.Text, 1),
                new("Card", "Tarjeta", ReportColumnType.Text, 2),
                new("CardSerial", "Serial tarjeta", ReportColumnType.Text, 2.2),
                new("Ports", "Puertos", ReportColumnType.Integer, 1),
                new("Status", "Estado", ReportColumnType.Text, 1.4),
            ],
        };
        foreach (var view in all)
        {
            var e = view.Equipment;
            var model = string.Join(' ', new[] { e.Brand, e.Model }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (view.Slots.Count == 0)
            {
                equipmentSection.Rows.Add([e.Name, SpanishNames.Of(e.Kind), e.Site?.Code, model, e.Serial, null, null, null, null, SpanishNames.Of(e.Status)]);
                continue;
            }

            var first = true;
            foreach (var slot in view.Slots)
            {
                equipmentSection.Rows.Add([first ? e.Name : null, first ? SpanishNames.Of(e.Kind) : null, first ? e.Site?.Code : null,
                    first ? model : null, first ? e.Serial : null, slot.SlotNumber.ToString(), slot.CardModel, slot.CardSerial,
                    slot.PortCount, first ? SpanishNames.Of(e.Status) : SpanishNames.Of(slot.Status)]);
                first = false;
            }
        }

        document.Sections.Add(equipmentSection);

        var sfp = await equipment.TransceiversAsync(projectId, null, null, ct);
        var sfpSection = new ReportSection
        {
            Heading = "Transceptores SFP",
            Columns =
            [
                new("Serial", "Serial SFP", ReportColumnType.Text, 2.4),
                new("Model", "Modelo", ReportColumnType.Text, 2.2),
                new("Wave", "λ (nm)", ReportColumnType.Integer, 1),
                new("Equipment", "Equipo", ReportColumnType.Text, 2.4),
                new("Slot", "Ranura", ReportColumnType.Text, 1),
                new("Port", "Puerto", ReportColumnType.Text, 1),
                new("Status", "Estado", ReportColumnType.Text, 1.4),
                new("On", "Instalado", ReportColumnType.Date, 1.5),
                new("Notes", "Notas", ReportColumnType.Text, 2.6),
            ],
        };
        foreach (var t in sfp)
        {
            sfpSection.Rows.Add([t.Serial, t.Model, t.WavelengthNm, t.Equipment?.Name, t.Slot?.SlotNumber.ToString(), t.PortNumber?.ToString(),
                SpanishNames.Of(t.Status), t.InstalledOn, t.Notes]);
        }

        document.Sections.Add(sfpSection);
        document.Notes.Add($"Equipos: {all.Count} · SFP registrados: {sfp.Count} · instalados: {sfp.Count(t => t.Status == TransceiverStatus.Installed)} · en stock: {sfp.Count(t => t.Status == TransceiverStatus.InStock)}.");
    }

    private async Task MinutesAsync(ReportDocument document, int projectId, CancellationToken ct)
    {
        var list = await minutes.ListAsync(projectId, ct);
        var names = (await users.ListAsync(ct)).ToDictionary(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Email : u.FullName);

        var minuteSection = new ReportSection
        {
            Heading = "Minutas",
            Columns =
            [
                new("Date", "Fecha", ReportColumnType.Date, 1.4),
                new("Title", "Título", ReportColumnType.Text, 3),
                new("Attendees", "Asistentes", ReportColumnType.Text, 3),
                new("Summary", "Resumen", ReportColumnType.Text, 5),
                new("Items", "Pendientes", ReportColumnType.Text, 1.2),
            ],
        };
        foreach (var m in list)
        {
            minuteSection.Rows.Add([m.Date, m.Title, m.Attendees, m.Summary, $"{m.Items.Count(i => i.IsDone)}/{m.Items.Count} cerrados"]);
        }

        document.Sections.Add(minuteSection);

        var pending = list.SelectMany(m => m.Items.Where(i => !i.IsDone).Select(i => (Minute: m, Item: i)))
            .OrderBy(x => x.Item.DueDate ?? DateOnly.MaxValue).ToList();
        var pendingSection = new ReportSection
        {
            Heading = "Pendientes abiertos",
            Columns =
            [
                new("Item", "Pendiente", ReportColumnType.Text, 5),
                new("Site", "Nodo", ReportColumnType.Text, 1),
                new("Who", "Responsable", ReportColumnType.Text, 2.4),
                new("Due", "Fecha límite", ReportColumnType.Date, 1.5),
                new("Minute", "Minuta", ReportColumnType.Text, 3),
            ],
        };
        foreach (var (minute, item) in pending)
        {
            pendingSection.Rows.Add([item.Description, item.Site?.Code,
                item.ResponsibleUserId is not null && names.TryGetValue(item.ResponsibleUserId, out var who) ? who : null,
                item.DueDate, $"{minute.Date:dd/MM/yyyy} · {minute.Title}"]);
        }

        document.Sections.Add(pendingSection);
        document.Notes.Add($"Minutas: {list.Count} · pendientes abiertos: {pending.Count} · vencidos: {pending.Count(x => x.Item.DueDate < document.CutoffDate)}.");
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
