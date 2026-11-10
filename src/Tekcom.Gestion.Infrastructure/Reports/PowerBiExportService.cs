using System.Data;
using ClosedXML.Excel;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Infrastructure.Reports;

/// <summary>
/// Datos para Power BI: un Excel con tablas (hechos y dimensiones) listo para "Obtener datos › Libro de Excel".
/// Es el mismo modelo estrella de la guía del tablero de producción, con los datos del despliegue FTTH:
/// fInstalado (lo instalado por día y nodo), fNodos (una fila por nodo), fPartidas (proyectado e instalado por partida),
/// fEtapas (estado de cada etapa en cada nodo), dBrigada, dSupervisor, dEstado y tInfo (fecha de corte).
/// </summary>
public sealed class PowerBiExportService(IPresentationService presentation) : IPowerBiExportService
{
    public const string Finished = "Finalizado";
    public const string InProgress = "En avance";
    public const string NotStarted = "Sin iniciar";

    private static string StageName(StageStatus status) => status switch
    {
        StageStatus.Finished => "Finalizada",
        StageStatus.InProgress => "En proceso",
        StageStatus.InCorrection => "En corrección",
        StageStatus.NotApplicable => "No aplica",
        _ => "Pendiente",
    };

    private static string StateOf(decimal progress) => progress >= 100 ? Finished : progress > 0 ? InProgress : NotStarted;
    private static int StateOrder(string state) => state == Finished ? 1 : state == InProgress ? 2 : 3;

    private static readonly (QuantityGroup Group, string Name)[] Items =
    [
        (QuantityGroup.FcCable, "Cable FC"), (QuantityGroup.MpoCable, "Cable MPO"), (QuantityGroup.SubBox, "Sub-box"),
        (QuantityGroup.EndBox, "End-box"), (QuantityGroup.HubBox, "Hub-box"), (QuantityGroup.XBox, "X-box"),
        (QuantityGroup.Fusion, "Fusiones"), (QuantityGroup.Crossing, "Cruces americanos"),
    ];

    public async Task<byte[]> BuildAsync(int projectId, CancellationToken ct = default)
    {
        var d = await presentation.GetAsync(projectId, ct);
        var rows = d.Matrix;
        var stateOf = rows.ToDictionary(r => r.SiteId, r => StateOf(r.Progress));

        // ---- fNodos
        var nodes = new DataTable("fNodos");
        foreach (var c in new[] { "Nodo", "Brigada", "Supervisor", "Estado" }) { nodes.Columns.Add(c, typeof(string)); }
        nodes.Columns.Add("EstadoOrden", typeof(int));
        foreach (var c in new[] { "AvancePct", "FATsInstaladas", "FATsProyectadas", "CableMetros", "Piezas" }) { nodes.Columns.Add(c, typeof(decimal)); }
        nodes.Columns.Add("HP", typeof(int));
        foreach (var c in new[] { "XBoxFusionada", "ListoParaMedir", "ListoParaEntregar" }) { nodes.Columns.Add(c, typeof(int)); }
        nodes.Columns.Add("FinPlan", typeof(DateTime));
        var xboxByCode = d.StageRows.ToDictionary(s => s.Site, s => s.Stages.GetValueOrDefault(StageCode.Fusion) == StageStatus.Finished);
        foreach (var r in rows.OrderBy(r => r.Code))
        {
            var state = stateOf[r.SiteId];
            nodes.Rows.Add(r.Code, r.Brigade, r.Supervisor ?? "Sin supervisor", state, StateOrder(state), Math.Round(r.Progress, 1), r.FatsInstalled, r.FatsProjected,
                Math.Round(r.CableMeters, 1), r.Pieces, r.HomesPassed ?? 0, xboxByCode.GetValueOrDefault(r.Code) ? 1 : 0, r.ReadyToMeasure ? 1 : 0,
                r.ReadyToDeliver ? 1 : 0, r.PlannedEnd is { } end ? end.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        }

        // ---- fInstalado
        var installed = new DataTable("fInstalado");
        installed.Columns.Add("Fecha", typeof(DateTime));
        foreach (var c in new[] { "Nodo", "Brigada", "Supervisor", "Estado" }) { installed.Columns.Add(c, typeof(string)); }
        foreach (var c in new[] { "Metros", "FATs", "Piezas" }) { installed.Columns.Add(c, typeof(decimal)); }
        var bySite = rows.ToDictionary(r => r.SiteId);
        foreach (var p in d.ProductionBySite.OrderBy(p => p.Date).ThenBy(p => bySite.TryGetValue(p.SiteId, out var s) ? s.Code : string.Empty))
        {
            if (!bySite.TryGetValue(p.SiteId, out var node)) { continue; }
            installed.Rows.Add(p.Date.ToDateTime(TimeOnly.MinValue), node.Code, node.Brigade, node.Supervisor ?? "Sin supervisor", stateOf[node.SiteId],
                Math.Round(p.Meters, 1), p.Fats, p.Pieces);
        }

        // ---- fPartidas (una fila por nodo y partida)
        var items = new DataTable("fPartidas");
        foreach (var c in new[] { "Nodo", "Brigada", "Supervisor", "Estado", "Partida" }) { items.Columns.Add(c, typeof(string)); }
        items.Columns.Add("OrdenPartida", typeof(int));
        foreach (var c in new[] { "Proyectado", "Instalado" }) { items.Columns.Add(c, typeof(decimal)); }
        foreach (var r in rows.OrderBy(r => r.Code))
        {
            for (var i = 0; i < Items.Length; i++)
            {
                var pair = r.Quantities is null ? default : r.Quantities.GetValueOrDefault(Items[i].Group);
                if (pair.Projected == 0 && pair.Installed == 0) { continue; }
                items.Rows.Add(r.Code, r.Brigade, r.Supervisor ?? "Sin supervisor", stateOf[r.SiteId], Items[i].Name, i + 1, pair.Projected, pair.Installed);
            }
        }

        // ---- fEtapas (una fila por nodo y etapa)
        var stages = new DataTable("fEtapas");
        foreach (var c in new[] { "Nodo", "Brigada", "Supervisor", "Estado", "Etapa" }) { stages.Columns.Add(c, typeof(string)); }
        stages.Columns.Add("OrdenEtapa", typeof(int));
        stages.Columns.Add("EstadoEtapa", typeof(string));
        stages.Columns.Add("Finalizada", typeof(int));
        var codeToRow = rows.ToDictionary(r => r.Code);
        foreach (var s in d.StageRows.OrderBy(s => s.Site))
        {
            if (!codeToRow.TryGetValue(s.Site, out var node)) { continue; }
            for (var i = 0; i < d.StageColumns.Count; i++)
            {
                var status = s.Stages.TryGetValue(d.StageColumns[i].Code, out var v) ? v : StageStatus.Pending;
                stages.Rows.Add(s.Site, node.Brigade, node.Supervisor ?? "Sin supervisor", stateOf[node.SiteId], $"{i + 1}. {d.StageColumns[i].Name}", i + 1,
                    StageName(status), status == StageStatus.Finished ? 1 : 0);
            }
        }

        // ---- dimensiones e información
        var brigades = new DataTable("dBrigada"); brigades.Columns.Add("Brigada", typeof(string));
        foreach (var b in rows.Select(r => r.Brigade).Distinct().OrderBy(x => x)) { brigades.Rows.Add(b); }
        var supervisors = new DataTable("dSupervisor"); supervisors.Columns.Add("Supervisor", typeof(string));
        foreach (var s in rows.Select(r => r.Supervisor ?? "Sin supervisor").Distinct().OrderBy(x => x)) { supervisors.Rows.Add(s); }
        var states = new DataTable("dEstado"); states.Columns.Add("Estado", typeof(string)); states.Columns.Add("Orden", typeof(int));
        foreach (var s in new[] { Finished, InProgress, NotStarted }) { states.Rows.Add(s, StateOrder(s)); }
        var info = new DataTable("tInfo"); info.Columns.Add("Proyecto", typeof(string)); info.Columns.Add("Corte", typeof(DateTime));
        info.Rows.Add(d.ProjectName, d.Today.ToDateTime(TimeOnly.MinValue));

        using var book = new XLWorkbook();
        var readme = book.AddWorksheet("Leeme");
        readme.Cell(1, 1).Value = $"Datos para Power BI · {d.ProjectName} · corte {d.Today:dd/MM/yyyy}";
        readme.Cell(1, 1).Style.Font.Bold = true;
        readme.Cell(3, 1).Value = "Cada hoja es una tabla de Excel con el mismo nombre. En Power BI: Inicio › Obtener datos › Libro de Excel, marque las 8 tablas y siga la guía.";
        readme.Cell(4, 1).Value = "Las cifras salen del sistema de proyectos de TEK-COM; vuelva a descargar este archivo para actualizar.";
        readme.Column(1).Width = 120;
        foreach (var table in new[] { nodes, installed, items, stages, brigades, supervisors, states, info })
        {
            var sheet = book.AddWorksheet(table.TableName);
            sheet.Cell(1, 1).InsertTable(table, table.TableName, true);
            sheet.Columns().AdjustToContents();
            foreach (var col in table.Columns.Cast<DataColumn>().Where(c => c.DataType == typeof(DateTime)))
            {
                sheet.Column(col.Ordinal + 1).Style.DateFormat.Format = "yyyy-MM-dd";
            }
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
