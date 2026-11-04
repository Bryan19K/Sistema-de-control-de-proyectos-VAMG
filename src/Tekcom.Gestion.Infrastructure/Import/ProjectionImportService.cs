using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;
using Tekcom.Gestion.Infrastructure.Persistence.Seed;

namespace Tekcom.Gestion.Infrastructure.Import;

public sealed class ProjectionImportService(IDbContextFactory<ApplicationDbContext> factory, IDateTimeProvider clock)
    : IProjectionImportService
{
    private const string Sheet = "Proyección";

    public async Task<ProjectionAnalysis> AnalyzeAsync(int projectId, Stream file, string fileName, CancellationToken ct = default)
    {
        var report = new ImportReport { FileName = fileName };
        var rows = new List<ProjectionRow>();

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        memory.Position = 0;

        XLWorkbook book;
        try
        {
            book = new XLWorkbook(memory);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Error, "(archivo)", null, null,
                "No se pudo abrir el archivo. Verifique que sea un Excel (.xlsx) válido."));
            return new ProjectionAnalysis { ProjectId = projectId, Report = report, Rows = rows };
        }

        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name) == "PROYECCION") ?? book.Worksheets.FirstOrDefault();
            if (sheet is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Error, "(archivo)", null, null, "El archivo no tiene hojas."));
                return new ProjectionAnalysis { ProjectId = projectId, Report = report, Rows = rows };
            }

            var (headerRow, nodeCol, materialCol, quantityCol) = FindHeaders(sheet);
            if (headerRow is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, null, null,
                    "No se encontraron las columnas NODO, MATERIAL y CANTIDAD. Use la plantilla."));
                return new ProjectionAnalysis { ProjectId = projectId, Report = report, Rows = rows };
            }

            await using var db = factory.CreateDbContext();
            var sites = await db.Sites.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync(ct);
            var materials = await db.Materials.AsNoTracking().ToListAsync(ct);
            var aliases = await db.MaterialAliases.AsNoTracking().ToListAsync(ct);

            Material? Resolve(string name)
            {
                var key = NameNormalizer.Key(name);
                return materials.FirstOrDefault(m => NameNormalizer.Key(m.Name) == key)
                    ?? aliases.Where(a => a.AliasKey == key).Select(a => materials.FirstOrDefault(m => m.Id == a.MaterialId)).FirstOrDefault();
            }

            var seen = new Dictionary<(int, int), ProjectionRow>();
            var last = sheet.LastRowUsed()?.RowNumber() ?? headerRow.Value;
            for (var r = headerRow.Value + 1; r <= last; r++)
            {
                var code = sheet.Cell(r, nodeCol).GetString().Trim();
                var materialName = sheet.Cell(r, materialCol).GetString().Trim();
                var quantityCell = sheet.Cell(r, quantityCol);
                if (code.Length == 0 && materialName.Length == 0 && quantityCell.IsEmpty())
                {
                    continue;
                }

                var rowOk = true;
                var site = sites.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
                if (site is null)
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, r, "NODO",
                        $"El nodo '{code}' no existe en este proyecto. Créelo primero en 'Nodos y avance'."));
                    rowOk = false;
                }

                var material = Resolve(materialName);
                if (material is null)
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, r, "MATERIAL",
                        $"Material no reconocido: '{materialName}'. Agréguelo al catálogo o regístrelo como alias."));
                    rowOk = false;
                }

                decimal quantity = 0;
                var quantityValue = quantityCell.Value;
                if (quantityValue.IsNumber)
                {
                    quantity = (decimal)quantityValue.GetNumber();
                }
                else if (!(quantityValue.IsText && decimal.TryParse(quantityValue.GetText().Replace(',', '.'),
                             System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out quantity)))
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, r, "CANTIDAD",
                        $"'{quantityValue}' no es una cantidad."));
                    rowOk = false;
                }

                if (quantity < 0)
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet.Name, r, "CANTIDAD", "La cantidad no puede ser negativa."));
                    rowOk = false;
                }

                if (!rowOk)
                {
                    report.Counts.RowsSkipped++;
                    continue;
                }

                var key = (site!.Id, material!.Id);
                if (seen.TryGetValue(key, out var previous))
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, "MATERIAL",
                        $"{code} / {material.Name} aparece repetido (fila {previous.Row}): se suman las cantidades."));
                    seen[key] = new ProjectionRow
                    {
                        Row = previous.Row, NodeCode = previous.NodeCode, MaterialName = previous.MaterialName,
                        Quantity = previous.Quantity + quantity, SiteId = site.Id, MaterialId = material.Id,
                    };
                }
                else
                {
                    seen[key] = new ProjectionRow
                    {
                        Row = r, NodeCode = site.Code, MaterialName = material.Name, Quantity = quantity,
                        SiteId = site.Id, MaterialId = material.Id,
                    };
                }
            }

            rows.AddRange(seen.Values);
            report.NodeCodes.AddRange(rows.Select(x => x.NodeCode).Distinct());
        }

        return new ProjectionAnalysis { ProjectId = projectId, Report = report, Rows = rows };
    }

    public async Task<ImportReport> ApplyAsync(ProjectionAnalysis analysis, CancellationToken ct = default)
    {
        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues);
        report.NodeCodes.AddRange(analysis.Report.NodeCodes);
        report.Counts.RowsSkipped = analysis.Report.Counts.RowsSkipped;

        if (analysis.Rows.Count == 0)
        {
            throw new BusinessRuleException("No hay filas válidas para importar. Revise los errores del reporte.");
        }

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var latest = (await db.DesignProjections.Where(p => p.ProjectId == analysis.ProjectId).Include(p => p.Lines).ToListAsync(ct))
            .GroupBy(p => p.SiteId).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Version).First());

        foreach (var group in analysis.Rows.GroupBy(r => r.SiteId))
        {
            var lines = group.Where(r => r.Quantity > 0).ToDictionary(r => r.MaterialId, r => r.Quantity);
            latest.TryGetValue(group.Key, out var current);
            var same = current is not null
                && current.Lines.Count == lines.Count
                && current.Lines.All(l => lines.TryGetValue(l.MaterialId, out var qty) && qty == l.Quantity);
            if (same)
            {
                continue;
            }

            var projection = new DesignProjection
            {
                ProjectId = analysis.ProjectId, SiteId = group.Key, Version = (current?.Version ?? 0) + 1,
                Date = clock.Today, Source = analysis.Report.FileName, Notes = "Importada desde Excel.",
            };
            foreach (var (materialId, quantity) in lines)
            {
                projection.Lines.Add(new DesignProjectionLine { MaterialId = materialId, Quantity = quantity });
            }

            db.DesignProjections.Add(projection);
            report.Counts.ProjectionVersionsCreated++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        report.Applied = true;
        return report;
    }

    public byte[] BuildTemplate()
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet(Sheet);
        sheet.Cell(1, 1).Value = "NODO";
        sheet.Cell(1, 2).Value = "MATERIAL";
        sheet.Cell(1, 3).Value = "CANTIDAD";
        sheet.Cell(2, 1).Value = "SON19";
        sheet.Cell(2, 2).Value = "NAP de paso";
        sheet.Cell(2, 3).Value = 51;
        sheet.Cell(3, 1).Value = "SON19";
        sheet.Cell(3, 2).Value = "Cable MPO 400 m";
        sheet.Cell(3, 3).Value = 1;
        sheet.Range(1, 1, 1, 3).Style.Font.Bold = true;
        sheet.Range(1, 1, 1, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#12304f");
        sheet.Range(1, 1, 1, 3).Style.Font.FontColor = XLColor.White;
        sheet.Columns().AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        var help = book.AddWorksheet("Instrucciones");
        help.Cell(1, 1).Value = "Cómo llenar la plantilla";
        help.Cell(1, 1).Style.Font.Bold = true;
        help.Cell(3, 1).Value = "1. Una fila por nodo y material. NODO es el código del nodo (ej. SON19).";
        help.Cell(4, 1).Value = "2. MATERIAL puede ser el nombre del catálogo o un alias (ej. SUB-BOX = NAP de paso).";
        help.Cell(5, 1).Value = "3. CANTIDAD es la cantidad proyectada en el diseño.";
        help.Cell(6, 1).Value = "4. Cada importación crea una versión nueva de la proyección del nodo; las anteriores se conservan.";
        help.Cell(7, 1).Value = "5. Las filas con error no se importan y quedan listadas en el reporte.";
        help.Column(1).Width = 100;

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static (int? Row, int Node, int Material, int Quantity) FindHeaders(IXLWorksheet sheet)
    {
        var last = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 10);
        for (var r = 1; r <= last; r++)
        {
            int? node = null, material = null, quantity = null;
            for (var c = 1; c <= Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 1, 15); c++)
            {
                switch (NameNormalizer.Key(sheet.Cell(r, c).GetString()))
                {
                    case "NODO": node = c; break;
                    case "MATERIAL": material = c; break;
                    case "CANTIDAD": quantity = c; break;
                }
            }

            if (node is not null && material is not null && quantity is not null)
            {
                return (r, node.Value, material.Value, quantity.Value);
            }
        }

        return (null, 0, 0, 0);
    }
}
