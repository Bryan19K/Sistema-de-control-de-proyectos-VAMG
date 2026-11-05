using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record NwProject(string Name, string Code, string? Client, string? Responsible, string? Contractor,
    DateOnly? Start, DateOnly? End, decimal? Progress, string? StatusText, string? Observations, string? PendingWork);

internal sealed record NwPhase(string Project, string Name, string? Parent, decimal Weight, decimal Percent, int Order);

internal sealed record NwRecord(string Project, BuildingRecordKind Kind, string? Level, string Name, decimal? Progress,
    string? Status, string? Notes, Dictionary<string, string> Details, int Order);

internal sealed record NwMaterial(string Project, string? Po, string Material, string Unit, string? Type, decimal Assigned, decimal Installed, decimal Returned);

internal sealed class NextWaveData
{
    public List<NwProject> Projects { get; } = [];
    public List<NwPhase> Phases { get; } = [];
    public List<NwRecord> Records { get; } = [];
    public List<NwMaterial> Materials { get; } = [];
}

/// <summary>
/// Carga los proyectos de edificios de NextWave desde "TEK-COM · Control de proyectos": proyectos con su centro de costos,
/// fases, apartamentos, infraestructura, resumen por nivel y el control de materiales (asignado, instalado, devuelto).
/// Las hojas de personal (DUI y teléfonos) no se cargan.
/// </summary>
public sealed class NextWaveControlImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.NextWaveControl;

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        var main = book is null ? null : Xl.Sheet(book, "Base de Datos Principal");
        if (book is null || main is null || Xl.Sheet(book, "Detalle por Edificio") is null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var data = new NextWaveData();

        var costCenters = ReadCostCenters(book);
        var pending = ReadPendingWork(book);
        ReadProjects(main, costCenters, pending, data, report);
        ReadPhases(book, data);
        ReadUnits(book, data, report);
        ReadInfrastructure(book, data);
        ReadLevels(book, data);
        ReadMaterials(book, data, report);

        var hidden = book.Worksheets.Where(s => s.Visibility != XLWorksheetVisibility.Visible).Select(s => s.Name)
            .Where(n => NameNormalizer.Key(n).StartsWith("PERSONAL", StringComparison.Ordinal)).ToList();
        if (hidden.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Info, string.Join(", ", hidden), null, null,
                "Las hojas de personal (DUI y teléfonos) no se cargan: son datos personales que no hacen falta en el sistema."));
        }

        foreach (var project in data.Projects)
        {
            if (!costCenters.ContainsKey(NameNormalizer.Key(project.Name)))
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "Dashboard General", null, "CENTRO DE COSTOS",
                    $"'{project.Name}' no tiene centro de costos en el Dashboard; se usa el nombre como código."));
            }
        }

        report.Summary.Add(new("Proyectos", string.Join(", ", data.Projects.Select(p => $"{p.Name} ({p.Code})"))));
        report.Summary.Add(new("Fases", data.Phases.Count.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Apartamentos", data.Records.Count(r => r.Kind == BuildingRecordKind.Unit).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Infraestructura por edificio", data.Records.Count(r => r.Kind == BuildingRecordKind.Infrastructure).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Resumen por nivel", data.Records.Count(r => r.Kind == BuildingRecordKind.Level).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Líneas de materiales", data.Materials.Count.ToString(CultureInfo.InvariantCulture)));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.NextWaveControl,
            Title = "Proyectos NextWave (edificios)",
            Description = "Un proyecto por edificio con sus fases, apartamentos, infraestructura, niveles y control de materiales.",
            Report = report,
            Payload = data,
        });
    }

    // ------------------------------------------------------------------ lectura

    private static string Clean(string text)
    {
        var t = text.Trim();
        var i = 0;
        while (i < t.Length && !char.IsLetterOrDigit(t[i]))
        {
            i++;
        }

        return i >= t.Length ? string.Empty : t[i..].Trim();
    }

    private static decimal? Percent(decimal? value) => value is null ? null : Math.Round(value.Value <= 1.5m ? value.Value * 100 : value.Value, 2);

    private static Dictionary<string, string> ReadCostCenters(XLWorkbook book)
    {
        var map = new Dictionary<string, string>();
        var sheet = Xl.Sheet(book, "Dashboard General");
        if (sheet is null)
        {
            return map;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= last; r++)
        {
            if (!NameNormalizer.Key(Xl.Text(sheet, r, 2)).Contains("CENTRO DE COSTOS", StringComparison.Ordinal))
            {
                continue;
            }

            for (var x = r + 1; x <= last && Xl.Text(sheet, x, 1).Length > 0 && Xl.Text(sheet, x, 2).Length > 0; x++)
            {
                map[NameNormalizer.Key(Xl.Text(sheet, x, 1))] = Xl.Text(sheet, x, 2);
            }

            break;
        }

        return map;
    }

    private static Dictionary<string, string> ReadPendingWork(XLWorkbook book)
    {
        var map = new Dictionary<string, string>();
        var sheet = Xl.Sheet(book, "Dashboard General");
        if (sheet is null)
        {
            return map;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= last; r++)
        {
            if (!NameNormalizer.Key(Xl.Text(sheet, r, 6)).Contains("TRABAJOS POR EJECUTAR", StringComparison.Ordinal))
            {
                continue;
            }

            for (var x = r + 1; x <= last && Xl.Text(sheet, x, 1).Length > 0; x++)
            {
                map[NameNormalizer.Key(Xl.Text(sheet, x, 1))] = Xl.Text(sheet, x, 6);
            }

            break;
        }

        return map;
    }

    private static void ReadProjects(IXLWorksheet sheet, Dictionary<string, string> costCenters, Dictionary<string, string> pending,
        NextWaveData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 3; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 3);
            if (name.Length == 0)
            {
                continue;
            }

            var key = NameNormalizer.Key(name);
            var code = costCenters.TryGetValue(key, out var cc) ? cc : name;
            data.Projects.Add(new NwProject(name, code, Xl.Text(sheet, r, 4).NullIfEmpty(), Xl.Text(sheet, r, 5).NullIfEmpty(),
                Xl.Text(sheet, r, 6).NullIfEmpty(), Xl.Date(sheet, r, 7), Xl.Date(sheet, r, 8), Percent(Xl.Number(sheet, r, 9)),
                Clean(Xl.Text(sheet, r, 10)), Xl.Text(sheet, r, 11).NullIfEmpty(),
                pending.TryGetValue(key, out var work) ? work.NullIfEmpty() : null));
        }

        if (data.Projects.Count == 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, null, null, "No se encontraron proyectos en la hoja."));
        }
    }

    private static void ReadPhases(XLWorkbook book, NextWaveData data)
    {
        var sheet = Xl.Sheet(book, "Fases del Proyecto");
        if (sheet is null)
        {
            return;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;

        // Ponderación de los procesos de ingeniería previa.
        var processes = new List<(string Name, decimal Weight)>();
        for (var r = 4; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 1);
            if (name.Length == 0 || NameNormalizer.Key(name).StartsWith("TOTAL", StringComparison.Ordinal))
            {
                break;
            }

            processes.Add((Xl.Text(sheet, r, 1), Percent(Xl.Number(sheet, r, 2)) ?? 0));
        }

        // Pesos de las dos fases ("Fase 1: Ingeniería Previa (40%)" / "Fase 2: Construcción (60%)").
        decimal engineeringWeight = 40, constructionWeight = 60;
        for (var r = 1; r <= last; r++)
        {
            if (!NameNormalizer.Key(Xl.Text(sheet, r, 1)).StartsWith("PROYECTO", StringComparison.Ordinal) ||
                !Xl.Text(sheet, r, 2).Contains('(') || !Xl.Text(sheet, r, 3).Contains('('))
            {
                continue;
            }

            var a = Regex.Match(Xl.Text(sheet, r, 2), @"\((\d+)%\)");
            var b = Regex.Match(Xl.Text(sheet, r, 3), @"\((\d+)%\)");
            if (a.Success) { engineeringWeight = decimal.Parse(a.Groups[1].Value, CultureInfo.InvariantCulture); }
            if (b.Success) { constructionWeight = decimal.Parse(b.Groups[1].Value, CultureInfo.InvariantCulture); }
            break;
        }

        // Avance por proyecto: procesos de ingeniería (columnas B..), construcción y resumen.
        var engineeringStart = 0;
        var constructionStart = 0;
        for (var r = 1; r <= last; r++)
        {
            var title = NameNormalizer.Key(Xl.Text(sheet, r, 1));
            if (title.Contains("INGENIERIA PREVIA AVANCE", StringComparison.Ordinal) && engineeringStart == 0) { engineeringStart = r + 2; }
            if (title.Contains("CONSTRUCCION AVANCE", StringComparison.Ordinal) && constructionStart == 0) { constructionStart = r + 2; }
        }

        var order = 0;
        foreach (var project in data.Projects)
        {
            var key = NameNormalizer.Key(project.Name);
            var engineeringRow = FindRow(sheet, engineeringStart, last, key);
            var constructionRow = FindRow(sheet, constructionStart, last, key);
            var engineeringPercent = 0m;
            var any = false;

            if (engineeringRow > 0)
            {
                engineeringPercent = Percent(Xl.Number(sheet, engineeringRow, 5)) ?? 0;
                data.Phases.Add(new NwPhase(project.Name, "Ingeniería previa", null, engineeringWeight, engineeringPercent, order++));
                for (var i = 0; i < processes.Count; i++)
                {
                    var pct = Percent(Xl.Number(sheet, engineeringRow, 2 + i)) ?? 0;
                    data.Phases.Add(new NwPhase(project.Name, Xl.TitleCase(processes[i].Name), "Ingeniería previa", processes[i].Weight, pct, order++));
                }

                any = true;
            }

            if (constructionRow > 0)
            {
                data.Phases.Add(new NwPhase(project.Name, "Construcción", null, constructionWeight,
                    Percent(Xl.Number(sheet, constructionRow, 2)) ?? 0, order++));
                any = true;
            }

            if (!any && engineeringPercent == 0)
            {
                continue;
            }
        }
    }

    private static int FindRow(IXLWorksheet sheet, int from, int last, string projectKey)
    {
        if (from <= 0)
        {
            return 0;
        }

        for (var r = from; r <= last; r++)
        {
            var key = NameNormalizer.Key(Xl.Text(sheet, r, 1));
            if (key.Length == 0)
            {
                break;
            }

            if (key == projectKey)
            {
                return r;
            }
        }

        return 0;
    }

    private static Dictionary<string, string> Details(IXLWorksheet sheet, int row, int headerRow, int fromCol, int toCol)
    {
        var details = new Dictionary<string, string>();
        for (var c = fromCol; c <= toCol; c++)
        {
            var header = Regex.Replace(Xl.Text(sheet, headerRow, c), @"\s+", " ");
            var value = Xl.Text(sheet, row, c);
            if (header.Length == 0 || value.Length == 0 || value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            details[header] = value;
        }

        return details;
    }

    private static void ReadUnits(XLWorkbook book, NextWaveData data, ImportReport report)
    {
        var sheet = Xl.Sheet(book, "Detalle por Edificio");
        if (sheet is null)
        {
            return;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var known = data.Projects.Select(p => NameNormalizer.Key(p.Name)).ToHashSet();
        var order = 0;
        for (var r = 4; r <= last; r++)
        {
            var project = Xl.Text(sheet, r, 2);
            var apartment = Xl.Text(sheet, r, 4);
            if (project.Length == 0 || apartment.Length == 0)
            {
                continue;
            }

            if (!known.Contains(NameNormalizer.Key(project)))
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, "Proyecto (Edificio)",
                    $"El proyecto '{project}' no está en la Base de Datos Principal; el apartamento se omite."));
                continue;
            }

            data.Records.Add(new NwRecord(project, BuildingRecordKind.Unit, Xl.Text(sheet, r, 3).NullIfEmpty(), apartment,
                Percent(Xl.Number(sheet, r, 20)), Clean(Xl.Text(sheet, r, 21)).NullIfEmpty(), Xl.Text(sheet, r, 22).NullIfEmpty(),
                Details(sheet, r, 3, 5, 19), order++));
        }
    }

    private static void ReadInfrastructure(XLWorkbook book, NextWaveData data)
    {
        var sheet = Xl.Sheet(book, "Infraestructura por Edificio");
        if (sheet is null)
        {
            return;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var known = data.Projects.Select(p => NameNormalizer.Key(p.Name)).ToHashSet();
        var order = 0;
        for (var r = 4; r <= last; r++)
        {
            var project = Xl.Text(sheet, r, 1);
            if (project.Length == 0 || !known.Contains(NameNormalizer.Key(project)))
            {
                continue;
            }

            data.Records.Add(new NwRecord(project, BuildingRecordKind.Infrastructure, null, "Infraestructura",
                Percent(Xl.Number(sheet, r, 36)), Clean(Xl.Text(sheet, r, 37)).NullIfEmpty(), Xl.Text(sheet, r, 38).NullIfEmpty(),
                Details(sheet, r, 3, 2, 35), order++));
        }
    }

    private static void ReadLevels(XLWorkbook book, NextWaveData data)
    {
        var sheet = Xl.Sheet(book, "Resumen por Nivel");
        if (sheet is null)
        {
            return;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var headerRow = 0;
        for (var r = 1; r <= last; r++)
        {
            if (NameNormalizer.Key(Xl.Text(sheet, r, 1)).StartsWith("PROYECTO EDIFICIO", StringComparison.Ordinal))
            {
                headerRow = r;
                break;
            }
        }

        if (headerRow == 0)
        {
            return;
        }

        var known = data.Projects.Select(p => NameNormalizer.Key(p.Name)).ToHashSet();
        var order = 0;
        for (var r = headerRow + 1; r <= last; r++)
        {
            var project = Xl.Text(sheet, r, 1);
            var level = Xl.Text(sheet, r, 2);
            if (project.Length == 0 || level.Length == 0 || !known.Contains(NameNormalizer.Key(project)))
            {
                continue;
            }

            data.Records.Add(new NwRecord(project, BuildingRecordKind.Level, level, $"Nivel {level}",
                Percent(Xl.Number(sheet, r, 10)), Clean(Xl.Text(sheet, r, 11)).NullIfEmpty(), Xl.Text(sheet, r, 12).NullIfEmpty(),
                Details(sheet, r, headerRow, 3, 9), order++));
        }
    }

    private static void ReadMaterials(XLWorkbook book, NextWaveData data, ImportReport report)
    {
        var sheet = Xl.Sheet(book, "Detalle de Materiales");
        if (sheet is null)
        {
            return;
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var known = data.Projects.Select(p => NameNormalizer.Key(p.Name)).ToHashSet();
        for (var r = 3; r <= last; r++)
        {
            var project = Xl.Text(sheet, r, 1);
            var material = Xl.Text(sheet, r, 4);
            if (project.Length == 0 || material.Length == 0)
            {
                continue;
            }

            if (!known.Contains(NameNormalizer.Key(project)))
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, "Proyecto",
                    $"El proyecto '{project}' no existe en la Base de Datos Principal; la línea se omite."));
                continue;
            }

            data.Materials.Add(new NwMaterial(project, Xl.Text(sheet, r, 2).NullIfEmpty(), material, Xl.Text(sheet, r, 6),
                Xl.Text(sheet, r, 5).NullIfEmpty(), Xl.Number(sheet, r, 7) ?? 0, Xl.Number(sheet, r, 8) ?? 0, Xl.Number(sheet, r, 10) ?? 0));
        }
    }

    // ------------------------------------------------------------------ aplicar

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        var data = (NextWaveData)analysis.Payload!;
        if (data.Projects.Count == 0)
        {
            throw new BusinessRuleException("No hay proyectos válidos para importar.");
        }

        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues);

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CatalogSeeder.EnsureAllAsync(db, ct);

        var type = await db.ProjectTypes.FirstAsync(t => t.Code == CatalogSeeder.BuildingCode, ct);
        var clients = await db.Clients.ToListAsync(ct);
        var projects = await db.Projects.ToListAsync(ct);
        var created = 0;
        var map = new Dictionary<string, Project>();

        foreach (var item in data.Projects)
        {
            var project = projects.FirstOrDefault(p => p.Code == item.Code)
                ?? projects.FirstOrDefault(p => NameNormalizer.Key(p.Name) == NameNormalizer.Key(item.Name) && p.ProjectTypeId == type.Id);
            if (project is null)
            {
                project = new Project { Code = item.Code, Name = item.Name, ProjectTypeId = type.Id };
                db.Projects.Add(project);
                projects.Add(project);
                created++;
            }

            if (item.Client is not null)
            {
                var key = NameNormalizer.Key(item.Client);
                var client = clients.FirstOrDefault(c => NameNormalizer.Key(c.Name) == key);
                if (client is null)
                {
                    client = new Client { Name = Xl.TitleCase(item.Client) == "Nextwave" ? "NextWave" : Xl.TitleCase(item.Client) };
                    db.Clients.Add(client);
                    clients.Add(client);
                }

                project.Client ??= client;
            }

            project.ResponsibleName ??= item.Responsible is null ? null : Xl.TitleCase(item.Responsible);
            project.PlannedStart ??= item.Start;
            project.PlannedEnd ??= item.End;
            project.ActualStart ??= item.Start;
            if (project.Id == 0)
            {
                // Solo al crear: después el avance y el estado se editan en el sistema y no se pisan.
                project.ActualEnd = item.End;
                project.ReportedProgress = item.Progress;
                project.PendingWork = item.PendingWork;
                project.Status = NameNormalizer.Key(item.StatusText) switch
                {
                    "FINALIZADO" or "FINALIZADA" or "COMPLETADO" => ProjectStatus.Closed,
                    "EN PROCESO" or "EN CURSO" => ProjectStatus.Active,
                    _ => ProjectStatus.Planning,
                };
            }
            var details = new List<string>();
            if (item.Contractor is not null) { details.Add($"Contratista: {item.Contractor}"); }
            if (item.Observations is not null) { details.Add(item.Observations); }
            if (string.IsNullOrWhiteSpace(project.Description) && details.Count > 0)
            {
                var text = string.Join("\n", details);
                project.Description = text.Length > 2000 ? text[..2000] : text;
            }

            map[NameNormalizer.Key(item.Name)] = project;
        }

        await db.SaveChangesAsync(ct);

        // Fases, apartamentos, infraestructura y niveles: solo se cargan en los proyectos que todavía no los tienen,
        // para no pisar lo que se haya corregido en el sistema.
        var ids = map.Values.Select(p => p.Id).ToList();
        var withPhases = (await db.ProjectPhases.Where(x => ids.Contains(x.ProjectId)).Select(x => x.ProjectId).Distinct().ToListAsync(ct)).ToHashSet();
        var withRecords = (await db.BuildingRecords.Where(x => ids.Contains(x.ProjectId)).Select(x => x.ProjectId).Distinct().ToListAsync(ct)).ToHashSet();
        var skippedProjects = new List<string>();

        foreach (var phase in data.Phases)
        {
            var target = map[NameNormalizer.Key(phase.Project)];
            if (withPhases.Contains(target.Id))
            {
                continue;
            }

            db.ProjectPhases.Add(new ProjectPhase
            {
                ProjectId = target.Id, Name = phase.Name, ParentName = phase.Parent,
                Weight = phase.Weight, Percent = phase.Percent, SortOrder = phase.Order,
            });
        }

        foreach (var record in data.Records)
        {
            var target = map[NameNormalizer.Key(record.Project)];
            if (withRecords.Contains(target.Id))
            {
                if (!skippedProjects.Contains(target.Name)) { skippedProjects.Add(target.Name); }
                continue;
            }

            db.BuildingRecords.Add(new BuildingRecord
            {
                ProjectId = target.Id, Kind = record.Kind, Level = record.Level, Name = record.Name,
                ProgressPercent = record.Progress, Status = record.Status, Notes = record.Notes, SortOrder = record.Order,
                DetailJson = record.Details.Count == 0 ? null : JsonSerializer.Serialize(record.Details),
            });
        }

        await db.SaveChangesAsync(ct);
        if (skippedProjects.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Info, string.Join(", ", skippedProjects), null, null,
                "Estos proyectos ya tenían apartamentos y avance cargados: no se sobrescriben para no pisar lo editado en el sistema."));
        }

        var materialResult = await NextWaveMaterials.ApplyAsync(db, map, data.Materials.Select(m => (m.Project, m)).ToList(), "Resumen", ct);
        await transaction.CommitAsync(ct);

        report.ProjectId = map.Values.First().Id;
        report.Summary.Add(new("Proyectos creados", created.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Proyectos actualizados", (data.Projects.Count - created).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Fases", data.Phases.Count.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Apartamentos", data.Records.Count(r => r.Kind == BuildingRecordKind.Unit).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Infraestructura", data.Records.Count(r => r.Kind == BuildingRecordKind.Infrastructure).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Niveles", data.Records.Count(r => r.Kind == BuildingRecordKind.Level).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Movimientos de materiales nuevos", materialResult.Movements.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Materiales nuevos en el catálogo", materialResult.MaterialsCreated.ToString(CultureInfo.InvariantCulture)));
        report.Counts.MaterialsCreated = materialResult.MaterialsCreated;
        foreach (var issue in materialResult.Issues)
        {
            report.Issues.Add(issue);
        }

        report.Applied = true;
        return report;
    }
}

internal sealed record NwMaterialResult(int Movements, int MaterialsCreated, List<ImportIssue> Issues);

/// <summary>Carga asignado / instalado / devuelto de los materiales de un proyecto como movimientos de bodega, sin duplicar.</summary>
internal static class NextWaveMaterials
{
    public const string Prefix = "Excel · NextWave · ";

    /// <summary>
    /// <paramref name="source"/> "Resumen" = hoja Detalle de Materiales del Excel de control; "Control" = el Excel dedicado de
    /// materiales por proyecto, que es más completo y reemplaza lo que haya cargado el resumen.
    /// </summary>
    public static async Task<NwMaterialResult> ApplyAsync(ApplicationDbContext db, Dictionary<string, Project> projects,
        List<(string Project, NwMaterial Line)> lines, string source, CancellationToken ct)
    {
        var assignedRef = $"{Prefix}{source} · Asignado";
        var installedRef = $"{Prefix}{source} · Instalado";
        var returnedRef = $"{Prefix}{source} · Devuelto";
        var issues = new List<ImportIssue>();
        if (lines.Count == 0)
        {
            return new NwMaterialResult(0, 0, issues);
        }

        // El control dedicado manda: si ya existe para un proyecto, el resumen no se vuelve a cargar; si llega, reemplaza al resumen.
        var projectIdsAll = projects.Values.Select(p => p.Id).ToList();
        var all = await db.InventoryMovements
            .Where(m => projectIdsAll.Contains(m.ProjectId) && !m.IsVoided && m.Reference != null && m.Reference.StartsWith(Prefix))
            .ToListAsync(ct);
        var skipProjects = new HashSet<string>();
        foreach (var (key, project) in projects)
        {
            if (source == "Resumen" && all.Any(m => m.ProjectId == project.Id && m.Reference!.StartsWith(Prefix + "Control")))
            {
                skipProjects.Add(key);
                issues.Add(new ImportIssue(ImportSeverity.Info, project.Name, null, null,
                    "Ya tiene el control de materiales dedicado cargado; no se vuelve a cargar el resumen del Excel de control."));
            }
            else if (source == "Control")
            {
                var old = all.Where(m => m.ProjectId == project.Id && m.Reference!.StartsWith(Prefix + "Resumen")).ToList();
                foreach (var m in old)
                {
                    m.IsVoided = true;
                    m.VoidReason = "Reemplazado por el control de materiales del proyecto (Excel dedicado).";
                    m.VoidedAt = DateTimeOffset.UtcNow;
                    m.VoidedBy = "Importación Excel";
                }

                if (old.Count > 0)
                {
                    issues.Add(new ImportIssue(ImportSeverity.Info, project.Name, null, null,
                        $"Se reemplazaron {old.Count} movimientos del resumen del Excel de control por el control de materiales dedicado."));
                }
            }
        }

        lines = lines.Where(l => !skipProjects.Contains(NameNormalizer.Key(l.Project))).ToList();
        if (lines.Count == 0)
        {
            await db.SaveChangesAsync(ct);
            return new NwMaterialResult(0, 0, issues);
        }

        var resolver = await MaterialResolver.LoadAsync(db, ct);
        var materialByLine = new Dictionary<NwMaterial, Material>();
        foreach (var (_, line) in lines)
        {
            var material = resolver.Resolve(line.Material, line.Unit);
            materialByLine[line] = material;
        }

        await db.SaveChangesAsync(ct);

        var projectIds = projects.Values.Select(p => p.Id).ToList();
        var existing = await db.InventoryMovements
            .Where(m => projectIds.Contains(m.ProjectId) && !m.IsVoided && m.Reference != null && m.Reference.StartsWith(Prefix + source))
            .ToListAsync(ct);

        var created = 0;
        var date = DateOnly.FromDateTime(DateTime.Today);
        foreach (var group in lines.GroupBy(l => (Project: NameNormalizer.Key(l.Project), Material: materialByLine[l.Line].Id)))
        {
            var project = projects[group.Key.Project];
            var kinds = new (MovementType Type, string Ref, decimal Want, string Label)[]
            {
                (MovementType.Delivery, assignedRef, group.Sum(l => l.Line.Assigned), "asignado"),
                (MovementType.Installed, installedRef, group.Sum(l => l.Line.Installed), "instalado"),
                (MovementType.Return, returnedRef, group.Sum(l => l.Line.Returned), "devuelto"),
            };

            foreach (var (type, reference, want, label) in kinds)
            {
                if (want <= 0)
                {
                    continue;
                }

                var have = existing.Where(m => m.ProjectId == project.Id && m.MaterialId == group.Key.Material && m.Type == type && m.Reference == reference)
                    .Sum(m => m.Quantity);
                if (have > want)
                {
                    issues.Add(new ImportIssue(ImportSeverity.Warning, project.Name, null, null,
                        $"{materialByLine[group.First().Line].Name}: ya hay {have:N0} {label} cargado y el Excel dice {want:N0}; no se resta nada (use un ajuste si corresponde)."));
                    continue;
                }

                if (have == want)
                {
                    continue;
                }

                var po = string.Join(", ", group.Select(l => l.Line.Po).Where(p => p is not null).Distinct());
                db.InventoryMovements.Add(new InventoryMovement
                {
                    ProjectId = project.Id, Type = type, MaterialId = group.Key.Material, Quantity = want - have,
                    Date = project.ActualEnd ?? project.ActualStart ?? date, Reference = reference, IsOpeningBalance = type == MovementType.Delivery,
                    Notes = po.Length > 0 ? $"PO {po}" : "Cargado desde el Excel de control",
                });
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new NwMaterialResult(created, resolver.Created, issues);
    }
}

/// <summary>
/// Control de materiales por proyecto de NextWave (una hoja por proyecto). Usa los mismos movimientos que el Excel de
/// control de proyectos, así que cargar los dos archivos no duplica nada.
/// </summary>
public sealed class NextWaveMaterialsImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.NextWaveMaterials;

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        if (book is null || Xl.Sheet(book, "Base de Datos Principal") is not null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var lines = new List<NwMaterial>();
        var projects = new List<string>();
        foreach (var sheet in book.Worksheets)
        {
            // Hoja de control: una fila de encabezado (fila 3 o 4) con Proyecto / Material / Unidad / Cant. Asignada…
            var headerRow = 0;
            for (var r = 2; r <= 6 && headerRow == 0; r++)
            {
                if (NameNormalizer.Key(Xl.Text(sheet, r, 2)) == "PROYECTO" && NameNormalizer.Key(Xl.Text(sheet, r, 3)).StartsWith("MATERIAL", StringComparison.Ordinal))
                {
                    headerRow = r;
                }
            }

            if (headerRow == 0)
            {
                continue;
            }

            int Col(Func<string, bool> match)
            {
                for (var c = 3; c <= 20; c++)
                {
                    if (match(NameNormalizer.Key(Xl.Text(sheet, headerRow, c))))
                    {
                        return c;
                    }
                }

                return 0;
            }

            var unitCol = Col(h => h == "UNIDAD");
            var typeCol = Col(h => h == "TIPO");
            var assignedCol = Col(h => h.Contains("ASIGNADA", StringComparison.Ordinal));
            var installedCol = Col(h => h.Contains("INSTALADA", StringComparison.Ordinal));
            var returnedCol = Col(h => (h.Contains("DEVUELTA", StringComparison.Ordinal) || h.Contains("REINTEGRADA", StringComparison.Ordinal))
                                       && !h.Contains("POR REINTEGRAR", StringComparison.Ordinal) && !h.Contains("POR DEVOLVER", StringComparison.Ordinal)
                                       && !h.Contains("PENDIENTE", StringComparison.Ordinal));
            if (assignedCol == 0)
            {
                continue;
            }

            projects.Add(sheet.Name.Trim());
            var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var r = headerRow + 1; r <= last; r++)
            {
                var project = Xl.Text(sheet, r, 2);
                var material = Xl.Text(sheet, r, 3);
                var key = NameNormalizer.Key(project);
                if (project.Length == 0 || key.StartsWith("MATERIALES PENDIENTES", StringComparison.Ordinal) || key == "TOTAL")
                {
                    break;
                }

                if (material.Length == 0)
                {
                    continue;
                }

                lines.Add(new NwMaterial(sheet.Name.Trim(), null, material, unitCol > 0 ? Xl.Text(sheet, r, unitCol) : "u",
                    typeCol > 0 ? Xl.Text(sheet, r, typeCol).NullIfEmpty() : null, Xl.Number(sheet, r, assignedCol) ?? 0,
                    installedCol > 0 ? Xl.Number(sheet, r, installedCol) ?? 0 : 0, returnedCol > 0 ? Xl.Number(sheet, r, returnedCol) ?? 0 : 0));
            }
        }

        if (lines.Count == 0)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        report.Summary.Add(new("Proyectos", string.Join(", ", projects)));
        report.Summary.Add(new("Líneas de materiales", lines.Count.ToString(CultureInfo.InvariantCulture)));
        report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
            "Los proyectos deben existir (cargue primero el Excel de control de proyectos de TEK-COM). Si ya cargó esos materiales, no se duplican."));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.NextWaveMaterials,
            Title = "Control de materiales de proyectos NextWave",
            Description = "Material asignado, instalado y devuelto por proyecto.",
            Report = report,
            Payload = lines,
        });
    }

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        var lines = (List<NwMaterial>)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues.Where(i => i.Severity != ImportSeverity.Info));

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var all = await db.Projects.ToListAsync(ct);
        var map = new Dictionary<string, Project>();
        foreach (var name in lines.Select(l => l.Project).Distinct())
        {
            var key = NameNormalizer.Key(name);
            var project = all.FirstOrDefault(p => NameNormalizer.Key(p.Name) == key);
            if (project is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Error, name, null, null,
                    $"No existe el proyecto '{name}'. Cargue primero el Excel de control de proyectos de TEK-COM."));
                continue;
            }

            map[key] = project;
        }

        var usable = lines.Where(l => map.ContainsKey(NameNormalizer.Key(l.Project))).Select(l => (l.Project, l)).ToList();
        if (usable.Count == 0)
        {
            throw new BusinessRuleException("Ninguno de los proyectos del archivo existe todavía. Cargue primero el Excel de control de proyectos.");
        }

        var result = await NextWaveMaterials.ApplyAsync(db, map, usable, "Control", ct);
        await transaction.CommitAsync(ct);
        report.Issues.AddRange(result.Issues);
        report.Summary.Add(new("Movimientos nuevos", result.Movements.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Materiales nuevos en el catálogo", result.MaterialsCreated.ToString(CultureInfo.InvariantCulture)));
        report.Counts.MaterialsCreated = result.MaterialsCreated;
        report.Applied = true;
        return report;
    }
}
