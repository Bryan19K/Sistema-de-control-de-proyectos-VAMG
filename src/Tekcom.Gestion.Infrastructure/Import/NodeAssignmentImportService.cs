using Tekcom.Gestion.Application.Common;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record AsRow(int Row, string Code, string? Permit, string? Brigade, string? Supervisor, int? Hp, int? Hub, int? Sub, int? End,
    bool? MaterialsDelivered, ClosureDocStatus? Kmz, ClosureDocStatus? Um, ClosureDocStatus? Photo, DateOnly? Start, bool? Finalized, bool? Atp);

internal sealed record AsDrive(string Supervisor, List<string> Responsible, List<string> Pending);

internal sealed class AsSheet
{
    public required string Name { get; init; }
    public required string Zone { get; init; }
    public List<AsRow> Rows { get; } = [];
    public List<AsDrive> Drive { get; } = [];
    public Dictionary<string, decimal> Totals { get; } = [];
}

internal sealed class AssignmentData
{
    public List<AsSheet> Sheets { get; } = [];
}

/// <summary>
/// Carga el Excel de asignación de nodos (orden de construcción): HP, permisos eléctricos, brigada, supervisor, cajas, estado de entrega de
/// materiales, información requerida para el cierre (KMZ, consolidado de UM, reporte fotográfico), fecha de inicio, estado y ATP.
/// Cada hoja es una zona (Sonsonate, Izalco…): si no existe un proyecto de esa zona se crea, y los nodos que faltan se agregan.
/// Importar de nuevo solo completa lo que esté vacío; lo que ya tiene el sistema no se pisa y las diferencias se avisan.
/// </summary>
public sealed class NodeAssignmentImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.NodeAssignment;

    private static readonly string[] Source = ["Excel de asignación de nodos"];

    private static bool IsOptical(string? name) => NameNormalizer.Key(name).Contains("OPTICAL", StringComparison.Ordinal);

    private static string NormalizeCode(string text) => System.Text.RegularExpressions.Regex.Replace(text.Trim().ToUpperInvariant(), @"\s+", string.Empty);

    private static bool? YesNo(string text)
    {
        var key = NameNormalizer.Key(text);
        return key switch { "SI" => true, "NO" => false, _ => null };
    }

    private static ClosureDocStatus? DocStatus(string text)
    {
        var key = NameNormalizer.Key(text);
        if (key.Length == 0)
        {
            return null;
        }

        return key.Contains("VALID", StringComparison.Ordinal) ? ClosureDocStatus.Validated
            : key.Contains("PENDIENTE", StringComparison.Ordinal) ? ClosureDocStatus.PendingContractor : null;
    }

    private static int? Whole(IXLWorksheet sheet, int row, int col)
    {
        var n = col == 0 ? null : Xl.Number(sheet, row, col);
        return n is null ? null : (int)Math.Round(n.Value);
    }

    // ================================================================== lectura

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        if (book is null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var data = new AssignmentData();
        var report = new ImportReport { FileName = fileName };
        foreach (var sheet in book.Worksheets)
        {
            var parsed = ReadSheet(sheet, report);
            if (parsed is not null)
            {
                data.Sheets.Add(parsed);
            }
        }

        if (data.Sheets.Count == 0)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        foreach (var s in data.Sheets)
        {
            report.NodeCodes.AddRange(s.Rows.Select(r => r.Code));
            report.Summary.Add(new($"Zona {Xl.TitleCase(s.Zone)}",
                $"{s.Rows.Count} nodos · HP {s.Rows.Sum(r => r.Hp ?? 0):N0} · {s.Rows.Sum(r => (r.Hub ?? 0) + (r.Sub ?? 0) + (r.End ?? 0)):N0} FATs"));
            foreach (var (name, value, sum) in new (string, string, decimal)[]
            {
                ("HP", "HOMEPASS", s.Rows.Sum(r => r.Hp ?? 0)), ("Hub-box", "HUB BOX", s.Rows.Sum(r => r.Hub ?? 0)),
                ("Sub-box", "SUB BOX", s.Rows.Sum(r => r.Sub ?? 0)), ("End-box", "END BOX", s.Rows.Sum(r => r.End ?? 0)),
                ("Total de FAT", "TOTAL DE FAT", s.Rows.Sum(r => (r.Hub ?? 0) + (r.Sub ?? 0) + (r.End ?? 0))),
            })
            {
                if (s.Totals.TryGetValue(value, out var excelTotal))
                {
                    report.TotalChecks.Add(new TotalCheck($"{Xl.TitleCase(s.Zone)}: {name} (fila TOTAL contra la suma de los nodos)", excelTotal, sum));
                }
            }
        }

        report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
            "Se agregan los nodos que falten y se completan brigada, supervisor, fecha de inicio, HP, permisos y estado de los documentos de cierre. " +
            "Lo que el sistema ya tiene no se pisa; las diferencias se avisan."));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.NodeAssignment,
            Title = "Asignación de nodos (orden de construcción)",
            Description = "Por nodo: HP, permisos eléctricos, brigada, supervisor, cajas, entrega de materiales, documentos de cierre y ATP; y el seguimiento de la bitácora por supervisor.",
            Report = report,
            Payload = data,
        });
    }

    private static AsSheet? ReadSheet(IXLWorksheet sheet, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var headerRow = 0;
        var columns = new Dictionary<string, int>();
        for (var r = 1; r <= Math.Min(last, 12) && headerRow == 0; r++)
        {
            var map = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var key = NameNormalizer.Key(Xl.Text(sheet, r, c));
                if (key.Length > 0)
                {
                    map.TryAdd(key, c);
                }
            }

            if (map.ContainsKey("NODO") && map.ContainsKey("HOMEPASS"))
            {
                headerRow = r;
                columns = map;
            }
        }

        if (headerRow == 0)
        {
            return null;
        }

        int Col(string key) => columns.TryGetValue(key, out var c) ? c : 0;
        var zone = NameNormalizer.Key(sheet.Name).Replace("ASIG NODOS", string.Empty).Trim();
        var result = new AsSheet { Name = sheet.Name, Zone = zone.Length == 0 ? NameNormalizer.Key(sheet.Name) : zone };
        var nodeCol = Col("NODO");
        var seen = new HashSet<string>();

        var r2 = headerRow + 1;
        for (; r2 <= last; r2++)
        {
            var raw = Xl.Text(sheet, r2, nodeCol);
            var key = NameNormalizer.Key(raw);
            if (key.Length == 0)
            {
                break;
            }

            if (key == "TOTAL")
            {
                foreach (var name in new[] { "HOMEPASS", "HUB BOX", "SUB BOX", "END BOX", "TOTAL DE FAT" })
                {
                    if (Xl.Number(sheet, r2, Col(name)) is { } total && Col(name) != 0)
                    {
                        result.Totals[name] = total;
                    }
                }

                r2++;
                break;
            }

            var code = NormalizeCode(raw);
            if (!seen.Add(code))
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r2, "NODO", $"El nodo {code} aparece repetido; se usa solo la primera fila."));
                continue;
            }

            string? Text(string name) => Col(name) == 0 ? null : (Xl.Text(sheet, r2, Col(name)) is { Length: > 0 } t ? t.Trim() : null);
            var status = Text("ESTADO");
            result.Rows.Add(new AsRow(r2, code, Text("ESTATUS DE PERMISOS ELECTRICOS"), Text("BRIGADA"), Text("SUPERVISOR"),
                Whole(sheet, r2, Col("HOMEPASS")), Whole(sheet, r2, Col("HUB BOX")), Whole(sheet, r2, Col("SUB BOX")), Whole(sheet, r2, Col("END BOX")),
                YesNo(Text("ESTATUS DE ENTREGA DE MATERIALES") ?? string.Empty), DocStatus(Text("KMZ") ?? string.Empty),
                DocStatus(Text("CONSOLIDADO DE UM") ?? string.Empty), DocStatus(Text("REPORTE FOTOGRAFICO") ?? string.Empty),
                Col("FECHA DE INCIO") == 0 ? null : Xl.Date(sheet, r2, Col("FECHA DE INCIO")),
                status is null ? null : NameNormalizer.Key(status).Contains("FINALIZ", StringComparison.Ordinal),
                YesNo(Text("ATP") ?? string.Empty)));
        }

        // Seguimiento de actualización de la bitácora (Drive) por supervisor.
        for (var r = r2; r <= last; r++)
        {
            if (!NameNormalizer.Key(Xl.Text(sheet, r, nodeCol)).StartsWith("SEGUIMIENTO DE ACTUALIZACION", StringComparison.Ordinal))
            {
                continue;
            }

            var headers = new Dictionary<string, int>();
            for (var c = 1; c <= lastCol; c++)
            {
                var k = NameNormalizer.Key(Xl.Text(sheet, r + 1, c));
                if (k.Length > 0) { headers.TryAdd(k, c); }
            }

            if (!headers.TryGetValue("SUPERVISOR", out var supCol) || !headers.TryGetValue("NODOS RESPONSABLES", out var respCol))
            {
                break;
            }

            headers.TryGetValue("NODOS PENDIENTES DE ACTUALIZAR", out var pendCol);
            for (var q = r + 2; q <= last; q++)
            {
                var name = Xl.Text(sheet, q, supCol);
                if (name.Length == 0 || NameNormalizer.Key(name) == "TOTAL")
                {
                    break;
                }

                static List<string> Codes(string text) => text.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(NormalizeCode).Where(x => x.Length > 0).ToList();
                result.Drive.Add(new AsDrive(name.Trim(), Codes(Xl.Text(sheet, q, respCol)), pendCol == 0 ? [] : Codes(Xl.Text(sheet, q, pendCol))));
            }

            break;
        }

        return result;
    }

    // ================================================================== carga

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        var data = (AssignmentData)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName, Applied = true };
        var today = DateOnly.FromDateTime(DateTime.Today);

        await using var db = factory.CreateDbContext();
        var contractors = await db.Contractors.ToListAsync(ct);
        var supervisors = await db.Supervisors.ToListAsync(ct);
        var boxMaterials = new Dictionary<QuantityGroup, Material>();
        foreach (var group in new[] { QuantityGroup.HubBox, QuantityGroup.SubBox, QuantityGroup.EndBox })
        {
            var material = (await db.Materials.Where(m => m.Group == group).ToListAsync(ct))
                .OrderByDescending(m => (m.Code ?? string.Empty).StartsWith("NW", StringComparison.OrdinalIgnoreCase)).ThenBy(m => m.Id).FirstOrDefault();
            if (material is not null) { boxMaterials[group] = material; }
        }

        foreach (var sheet in data.Sheets)
        {
            var project = await FindOrCreateProjectAsync(db, sheet.Zone, report, ct);
            var sites = await db.Sites.Where(s => s.ProjectId == project.Id).ToListAsync(ct);
            var created = new List<(Site Site, AsRow Row)>();
            var changedSites = new HashSet<Site>();
            var differences = new List<string>();

            foreach (var row in sheet.Rows)
            {
                var site = sites.FirstOrDefault(s => string.Equals(s.Code, row.Code, StringComparison.OrdinalIgnoreCase));
                var isNew = site is null;
                if (site is null)
                {
                    site = new Site { ProjectId = project.Id, Code = row.Code, Zone = sheet.Zone };
                    db.Sites.Add(site);
                    sites.Add(site);
                    created.Add((site, row));
                    report.Counts.SitesCreated++;
                }

                var changed = false;

                // Brigada y supervisor: solo si el nodo no tiene.
                if (row.Brigade is not null)
                {
                    var contractor = IsOptical(row.Brigade) ? null : FindContractor(contractors, row.Brigade);
                    if (IsOptical(row.Brigade))
                    {
                        if (site.ContractorId is null)
                        {
                            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, row.Row, "BRIGADA",
                                $"{row.Code}: el Excel lo asigna a Optical, que ya no trabaja con TEK-COM; se dejó sin brigada."));
                        }
                    }
                    else if (contractor is null)
                    {
                        report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, row.Row, "BRIGADA",
                            $"{row.Code}: no se encontró la brigada «{row.Brigade.Trim()}» en Contratistas. Créela y asígnela al nodo."));
                    }
                    else if (site.ContractorId is null)
                    {
                        site.ContractorId = contractor.Id;
                        changed = true;
                    }
                    else if (site.ContractorId != contractor.Id)
                    {
                        differences.Add($"{row.Code}: brigada del Excel {contractor.Name}, en el sistema {contractors.FirstOrDefault(c => c.Id == site.ContractorId)?.Name}");
                    }
                }

                if (row.Supervisor is not null && FindSupervisor(supervisors, row.Supervisor) is { } supervisor)
                {
                    if (site.SupervisorId is null)
                    {
                        site.SupervisorId = supervisor.Id;
                        changed = true;
                    }
                    else if (site.SupervisorId != supervisor.Id)
                    {
                        differences.Add($"{row.Code}: supervisor del Excel {supervisor.Name}, en el sistema {supervisors.FirstOrDefault(s => s.Id == site.SupervisorId)?.Name}");
                    }
                }

                if (row.Start is { } start)
                {
                    if (site.PlannedStart is null)
                    {
                        site.PlannedStart = start;
                        changed = true;
                    }
                    else if (site.PlannedStart != start)
                    {
                        differences.Add($"{row.Code}: inicio del Excel {start:dd/MM/yyyy}, en el sistema {site.PlannedStart:dd/MM/yyyy}");
                    }
                }

                changed |= Fill(ref site, s => s.HomesPassed, (s, v) => s.HomesPassed = v, row.Hp);
                changed |= FillText(site, row.Permit is null ? null : Xl.TitleCase(row.Permit));
                changed |= Fill(ref site, s => s.MaterialsDelivered, (s, v) => s.MaterialsDelivered = v, row.MaterialsDelivered);
                changed |= Fill(ref site, s => s.KmzStatus, (s, v) => s.KmzStatus = v, row.Kmz);
                changed |= Fill(ref site, s => s.UmConsolidatedStatus, (s, v) => s.UmConsolidatedStatus = v, row.Um);
                changed |= Fill(ref site, s => s.PhotoReportStatus, (s, v) => s.PhotoReportStatus = v, row.Photo);
                changed |= Fill(ref site, s => s.AtpDelivered, (s, v) => s.AtpDelivered = v, row.Atp);

                if (row.Finalized == true && !site.OdnFinalized && !isNew)
                {
                    differences.Add($"{row.Code}: figura FINALIZADO en el Excel pero la ODN no está finalizada en el sistema");
                }

                if (changed && !isNew)
                {
                    changedSites.Add(site);
                }
            }

            // Seguimiento de la bitácora por supervisor.
            var logsUpdated = 0;
            foreach (var drive in sheet.Drive)
            {
                var supervisor = FindSupervisor(supervisors, drive.Supervisor);
                foreach (var code in drive.Responsible)
                {
                    var site = sites.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (site is null)
                    {
                        continue;
                    }

                    if (supervisor is not null && site.SupervisorId is null)
                    {
                        site.SupervisorId = supervisor.Id;
                        changedSites.Add(site);
                    }

                    if (!drive.Pending.Contains(code) && !site.LogsUpToDate)
                    {
                        site.LogsUpToDate = true;
                        logsUpdated++;
                        changedSites.Add(site);
                    }
                }
            }

            await db.SaveChangesAsync(ct);

            // Etapas y proyección de los nodos nuevos (con las cajas que trae el Excel).
            foreach (var (site, row) in created)
            {
                await SiteService.EnsureStagesAsync(db, site, project.ProjectTypeId, ct);
                if ((row.Hub ?? 0) + (row.Sub ?? 0) + (row.End ?? 0) > 0 && !await db.DesignProjections.AnyAsync(p => p.SiteId == site.Id, ct))
                {
                    var projection = new DesignProjection
                    {
                        ProjectId = project.Id, SiteId = site.Id, Version = 1, Date = today, Source = Source[0],
                        Notes = "Cajas (hub-box, sub-box y end-box) del Excel de asignación de nodos.",
                    };
                    foreach (var (group, quantity) in new[] { (QuantityGroup.HubBox, row.Hub), (QuantityGroup.SubBox, row.Sub), (QuantityGroup.EndBox, row.End) })
                    {
                        if (quantity is > 0 && boxMaterials.TryGetValue(group, out var material))
                        {
                            projection.Lines.Add(new DesignProjectionLine { MaterialId = material.Id, Quantity = quantity.Value });
                        }
                    }

                    if (projection.Lines.Count > 0)
                    {
                        db.DesignProjections.Add(projection);
                    }
                }
            }

            await db.SaveChangesAsync(ct);
            report.Counts.SitesUpdated += changedSites.Count;
            report.NodeCodes.AddRange(sheet.Rows.Select(r => r.Code));
            report.ProjectId ??= project.Id;

            // Cajas del Excel contra las proyectadas en el sistema, y HP contra las órdenes de contrato.
            var orders = await db.ContractOrders.AsNoTracking().Where(o => o.ProjectId == project.Id).ToListAsync(ct);
            var hpDiff = sheet.Rows.Where(r => r.Hp is not null)
                .Select(r => (r.Code, r.Hp, Order: orders.FirstOrDefault(o => string.Equals(NormalizeCode(o.Code), r.Code, StringComparison.OrdinalIgnoreCase))?.HomesPassed))
                .Where(x => x.Order is not null && x.Order != x.Hp).ToList();
            if (hpDiff.Count > 0)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Info, sheet.Name, null, "HOMEPASS",
                    $"{hpDiff.Count} nodos tienen un HP distinto al de su orden de contrato (se guardó el HP del Excel de asignación): " +
                    string.Join(", ", hpDiff.Take(12).Select(x => $"{x.Code} ({x.Hp:N0} contra {x.Order:N0})")) + (hpDiff.Count > 12 ? "…" : string.Empty)));
            }

            foreach (var line in differences)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Info, sheet.Name, null, null, "Se dejó lo del sistema — " + line));
            }

            report.Summary.Add(new($"{Xl.TitleCase(sheet.Zone)} ({project.Name})",
                $"{sheet.Rows.Count} nodos del Excel · {created.Count} nuevos · {changedSites.Count} completados · {logsUpdated} con bitácora al día"));
        }

        return report;
    }

    private static bool Fill<T>(ref Site site, Func<Site, T?> get, Action<Site, T?> set, T? value) where T : struct
    {
        if (value is null || get(site) is not null)
        {
            return false;
        }

        set(site, value);
        return true;
    }

    private static bool FillText(Site site, string? permit)
    {
        if (string.IsNullOrWhiteSpace(permit) || !string.IsNullOrWhiteSpace(site.ElectricalPermitStatus))
        {
            return false;
        }

        site.PermitStatus = ConstructionMath.PermitFromText(permit);
        site.ElectricalPermitStatus = ConstructionMath.PermitName(site.PermitStatus);
        return true;
    }

    private static async Task<Project> FindOrCreateProjectAsync(ApplicationDbContext db, string zone, ImportReport report, CancellationToken ct)
    {
        var zoneKey = NameNormalizer.Key(zone);
        var projects = await db.Projects.Include(p => p.ProjectType).ToListAsync(ct);
        var found = projects.Where(p => NameNormalizer.Key(p.Name).Contains(zoneKey, StringComparison.Ordinal))
            .OrderByDescending(p => p.ProjectType?.Code == "FTTH").FirstOrDefault();
        if (found is not null)
        {
            return found;
        }

        var type = await db.ProjectTypes.FirstOrDefaultAsync(t => t.Code == "FTTH", ct) ?? throw new InvalidOperationException("No existe el tipo de proyecto FTTH.");
        var code = zoneKey.Replace(' ', '-');
        for (var i = 2; projects.Any(p => p.Code == code); i++)
        {
            code = $"{zoneKey.Replace(' ', '-')}-{i}";
        }

        var project = new Project { Code = code, Name = $"Construccion de FTTH - {Xl.TitleCase(zone)}", ProjectTypeId = type.Id, Status = ProjectStatus.Planning };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
            $"No había un proyecto de {Xl.TitleCase(zone)}: se creó «{project.Name}» (código {project.Code}). Quedó «En planificación»: cámbiele el estado, el nombre, el código, el cliente y las fechas en Proyectos."));
        return project;
    }

    private static Contractor? FindContractor(IReadOnlyList<Contractor> contractors, string name)
    {
        var found = ContractorMatcher.Find(contractors, name);
        if (found is not null)
        {
            return found;
        }

        // Nombre de una sola palabra mal escrito ("RAYMUNDO" por "Raimundo"): se compara contra cada palabra del contratista.
        var key = NameNormalizer.Key(name);
        return key.Length >= 5 && !key.Contains(' ')
            ? contractors.FirstOrDefault(c => NameNormalizer.Key(c.Name).Split(' ').Any(t => t.Length >= 5 && Distance(t, key) <= 2))
            : null;
    }

    private static Supervisor? FindSupervisor(IReadOnlyList<Supervisor> supervisors, string name)
    {
        var key = NameNormalizer.Key(name);
        if (key.Length == 0)
        {
            return null;
        }

        return supervisors.FirstOrDefault(s => NameNormalizer.Key(s.Name) == key)
               ?? supervisors.FirstOrDefault(s => NameNormalizer.Key(s.Name).Split(' ').Contains(key));
    }

    private static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) { d[i, 0] = i; }
        for (var j = 0; j <= b.Length; j++) { d[0, j] = j; }
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }

        return d[a.Length, b.Length];
    }
}
