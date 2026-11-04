using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record WarehouseReceipt(string Section, DateOnly Date, string Material, string Unit, decimal Quantity,
    string? Model, string? Description, bool HasSerial);

internal sealed record WarehouseDelivery(string Source, string? NodeCode, string? Brigade, string Material, string Unit,
    decimal Quantity, DateOnly Date, string? Origin, string? Note);

internal sealed record WarehouseSfp(string Serial, string? Model, DateOnly Date, string? Description);

internal sealed class WarehouseData
{
    public List<WarehouseReceipt> Receipts { get; } = [];
    public List<WarehouseDelivery> Deliveries { get; } = [];
    public List<WarehouseSfp> Sfps { get; } = [];
    public decimal ExcelReceivedTotal { get; set; }
    public Dictionary<string, decimal> ConsolidatedByMaterial { get; } = [];
}

/// <summary>
/// Carga la bodega del Excel PROYECTO_FTTH: lo recibido en cada entrega del proveedor (catálogo maestro),
/// lo entregado a cada brigada por nodo, el feeder y el personal de O&amp;M, y los SFP con su serial.
/// Es idempotente: cada movimiento lleva una referencia y solo se agrega la diferencia.
/// </summary>
public sealed partial class WarehouseImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.Warehouse;

    private const string ReceiptPrefix = "Excel · Ingreso ";
    private const string DeliveryPrefix = "Excel · Entrega ";

    [GeneratedRegex(@"^ENTREGA DE MATERIALES (?<node>[A-Z]+\d+|FEEDER)$")]
    private static partial Regex DeliverySheetPattern();

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        if (book is null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var catalog = Xl.SheetContaining(book, "CATALOGO MAESTRO");
        var inventory = Xl.SheetContaining(book, "INVENTARIO POR MATERIAL");
        var deliverySheets = book.Worksheets.Where(s => DeliverySheetPattern().IsMatch(NameNormalizer.Key(s.Name))).ToList();
        var oym = Xl.SheetContaining(book, "ENTREGA A PERSONAL OYM");
        if (catalog is null && inventory is null && deliverySheets.Count == 0)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var data = new WarehouseData();

        if (catalog is not null)
        {
            ReadCatalog(catalog, data, report);
        }

        if (inventory is not null)
        {
            ReadInventory(inventory, data, report);
        }

        foreach (var sheet in deliverySheets)
        {
            ReadDeliverySheet(sheet, data, report);
        }

        if (oym is not null)
        {
            ReadBlock(oym, 2, "PERSONAL OYM", null, "Personal O&M", data, report);
        }

        var consolidated = Xl.SheetContaining(book, "CONSOLIDADO ENTREGAS");
        if (consolidated is not null)
        {
            ReadConsolidatedTotal(consolidated, data);
        }

        BuildChecks(data, report);
        report.Summary.Add(new("Ingresos del proveedor", $"{data.Receipts.Count} líneas · {data.Receipts.Sum(r => r.Quantity):N0} unidades"));
        report.Summary.Add(new("Entregas a brigadas", $"{data.Deliveries.Count} líneas · {data.Deliveries.Sum(d => d.Quantity):N0} unidades"));
        report.Summary.Add(new("Nodos con entregas", string.Join(", ", data.Deliveries.Select(d => d.NodeCode).Where(n => n is not null).Distinct().Order()!)));
        report.Summary.Add(new("SFP con serial", data.Sfps.Count.ToString(CultureInfo.InvariantCulture)));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.Warehouse,
            Title = "Bodega: ingresos, entregas y seriales",
            Description = "Lo recibido del proveedor por entrega, lo entregado a cada brigada por nodo, feeder y O&M, y los SFP con serial.",
            Report = report,
            NeedsProject = true,
            Payload = data,
        });
    }

    // ------------------------------------------------------------------ lectura

    private static void ReadCatalog(IXLWorksheet sheet, WarehouseData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var section = "General";
        var rows = new List<(string Section, int Row, string Name, string Unit, decimal Qty, DateOnly? Date, string? Model, string? Desc, string? Serial)>();

        for (var r = 4; r <= last; r++)
        {
            var b = Xl.Text(sheet, r, 2);
            var c = Xl.Text(sheet, r, 3);
            if (b.Length > 0 && c.Length == 0 && !decimal.TryParse(b, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            {
                section = b;
                continue;
            }

            if (Xl.Number(sheet, r, 2) is null || c.Length == 0)
            {
                continue;
            }

            var name = Xl.Text(sheet, r, 4);
            if (name.Length == 0)
            {
                name = c;
            }

            var quantity = Xl.Number(sheet, r, 8) ?? 1;
            if (quantity <= 0)
            {
                continue;
            }

            rows.Add((section, r, name, Xl.Text(sheet, r, 7), quantity, Xl.Date(sheet, r, 11),
                Xl.Text(sheet, r, 6).NullIfEmpty(), c, Xl.Text(sheet, r, 5).NullIfEmpty()));
        }

        var sections = rows.GroupBy(x => x.Section).ToDictionary(g => g.Key,
            g => g.Where(x => x.Date is not null).Select(x => x.Date!.Value).DefaultIfEmpty().Max());
        foreach (var group in rows.GroupBy(x => (x.Section, x.Name, Date: x.Date ?? sections[x.Section])))
        {
            if (group.Key.Date == default)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, group.First().Row, "Fecha de ingreso",
                    $"'{group.Key.Name}' no trae fecha de ingreso ni su sección tiene una; se omite."));
                continue;
            }

            var first = group.First();
            data.Receipts.Add(new WarehouseReceipt(SectionLabel(group.Key.Section), group.Key.Date, group.Key.Name, first.Unit,
                group.Sum(x => x.Qty), first.Model, first.Desc, group.Any(x => x.Serial is not null)));
        }

        foreach (var row in rows.Where(x => x.Serial is not null && IsSfp(x.Name)))
        {
            data.Sfps.Add(new WarehouseSfp(row.Serial!, row.Model, row.Date ?? sections[row.Section], row.Desc));
        }
    }

    private static bool IsSfp(string name) =>
        NameNormalizer.Key(name).StartsWith("SFP", StringComparison.Ordinal) || NameNormalizer.Key(name).StartsWith("OPTICAL TRANSCEIVER", StringComparison.Ordinal);

    private static string SectionLabel(string section)
    {
        var key = NameNormalizer.Key(section);
        if (key.StartsWith("1RA", StringComparison.Ordinal)) { return "1ª entrega (ODN)"; }
        if (key.StartsWith("2DA", StringComparison.Ordinal)) { return "2ª entrega (OLT-nodo)"; }
        if (key.StartsWith("3RA", StringComparison.Ordinal)) { return "3ª entrega"; }
        if (key.StartsWith("SUMINISTRO", StringComparison.Ordinal)) { return "Suministro TEK-COM (feeder)"; }
        return section.Length > 60 ? section[..60] : section;
    }

    /// <summary>Si el libro no trae catálogo maestro, el inventario por material da lo recibido por entrega.</summary>
    private static void ReadInventory(IXLWorksheet sheet, WarehouseData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var received = 0m;
        string[] labels = ["1ª entrega (ODN)", "2ª entrega (OLT-nodo)", "3ª entrega", "Suministro TEK-COM (feeder)"];
        var fromInventory = new List<WarehouseReceipt>();
        for (var r = 5; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 3);
            if (name.Length == 0 || Xl.Number(sheet, r, 2) is null)
            {
                continue;
            }

            var total = Xl.Number(sheet, r, 9) ?? 0;
            received += total;
            for (var i = 0; i < labels.Length; i++)
            {
                var qty = Xl.Number(sheet, r, 5 + i) ?? 0;
                if (qty > 0)
                {
                    fromInventory.Add(new WarehouseReceipt(labels[i], default, name, Xl.Text(sheet, r, 4), qty, null, null, false));
                }
            }
        }

        data.ExcelReceivedTotal = received;
        if (data.Receipts.Count == 0)
        {
            var date = DateOnly.FromDateTime(DateTime.Today);
            foreach (var receipt in fromInventory)
            {
                data.Receipts.Add(receipt with { Date = date });
            }

            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, null, null,
                "El archivo no trae el catálogo maestro: los ingresos se cargan con la fecha de hoy."));
        }
    }

    private static void ReadDeliverySheet(IXLWorksheet sheet, WarehouseData data, ImportReport report)
    {
        var match = DeliverySheetPattern().Match(NameNormalizer.Key(sheet.Name));
        var node = match.Groups["node"].Value;
        var isFeeder = node == "FEEDER";
        var nodeCode = isFeeder ? null : node;
        var source = isFeeder ? "FEEDER" : node;

        // Bloque principal en B:G
        ReadBlock(sheet, 2, source, nodeCode, isFeeder ? "Feeder" : null, data, report);

        // Algunos nodos (SON23) traen un segundo bloque I:N con otra brigada.
        if (string.Equals(Xl.Text(sheet, 4, 9), "MATERIAL", StringComparison.OrdinalIgnoreCase))
        {
            ReadBlock(sheet, 9, source, nodeCode, isFeeder ? "Feeder" : null, data, report);
        }
    }

    /// <summary>Lee un bloque MATERIAL / UM / CANTIDAD / FECHA / ORIGEN / OBSERVACIONES que empieza en la fila 4.</summary>
    private static void ReadBlock(IXLWorksheet sheet, int firstCol, string source, string? nodeCode, string? note,
        WarehouseData data, ImportReport report)
    {
        // La brigada va en la fila 3, una o dos columnas a la derecha de "BRIGADA RESPONSABLE:".
        var brigade = Xl.Text(sheet, 3, firstCol + 2);
        if (brigade.Length == 0)
        {
            brigade = Xl.Text(sheet, 3, firstCol + 1);
        }

        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lines = new List<(int Row, string Material, string Unit, decimal Qty, DateOnly? Date, string? Origin, string? Text)>();
        for (var r = 5; r <= last; r++)
        {
            var material = Xl.Text(sheet, r, firstCol);
            if (material.Length == 0 || NameNormalizer.Key(material).StartsWith("TOTAL", StringComparison.Ordinal))
            {
                break;
            }

            var quantity = Xl.Number(sheet, r, firstCol + 2);
            if (quantity is null or <= 0)
            {
                continue;
            }

            var observation = Xl.Text(sheet, r, firstCol + 5);
            var text = string.Join(" · ", new[] { note, observation.NullIfEmpty() }.Where(x => x is not null));
            lines.Add((r, material, Xl.Text(sheet, r, firstCol + 1), quantity.Value, Xl.Date(sheet, r, firstCol + 3),
                Xl.Text(sheet, r, firstCol + 4).NullIfEmpty(), text.Length == 0 ? null : text));
        }

        var fallback = lines.Where(l => l.Date is not null).Select(l => l.Date!.Value).DefaultIfEmpty().Max();
        foreach (var line in lines)
        {
            var date = line.Date;
            if (date is null)
            {
                if (fallback == default)
                {
                    report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, line.Row, "FECHA DE ENTREGA",
                        $"'{line.Material}' no trae fecha de entrega y la hoja no tiene otra; se omite."));
                    continue;
                }

                date = fallback;
                report.Issues.Add(new ImportIssue(ImportSeverity.Info, sheet.Name, line.Row, "FECHA DE ENTREGA",
                    $"'{line.Material}' no trae fecha de entrega; se usa la última fecha de la hoja ({fallback:dd/MM/yyyy})."));
            }

            data.Deliveries.Add(new WarehouseDelivery(source, nodeCode, brigade.NullIfEmpty(), line.Material, line.Unit,
                line.Qty, date.Value, line.Origin, line.Text));
        }
    }

    private static void ReadConsolidatedTotal(IXLWorksheet sheet, WarehouseData data)
    {
        // La columna "TOTAL ENTREGADO" suma todas las brigadas por material: sirve para comparar con las hojas por nodo.
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var totalCol = 0;
        for (var c = 1; c <= 40; c++)
        {
            if (NameNormalizer.Key(Xl.Text(sheet, 3, c)).Contains("TOTAL ENTREGADO", StringComparison.Ordinal))
            {
                totalCol = c;
                break;
            }
        }

        if (totalCol == 0)
        {
            return;
        }

        for (var r = 5; r <= last; r++)
        {
            var name = Xl.Text(sheet, r, 3);
            if (name.Length > 0 && Xl.Number(sheet, r, totalCol) is { } total)
            {
                data.ConsolidatedByMaterial[NameNormalizer.Key(name)] = total;
            }
        }
    }

    private static void BuildChecks(WarehouseData data, ImportReport report)
    {
        if (data.ExcelReceivedTotal > 0)
        {
            report.TotalChecks.Add(new TotalCheck("Unidades recibidas del proveedor", data.ExcelReceivedTotal, data.Receipts.Sum(r => r.Quantity)));
        }

        if (data.ConsolidatedByMaterial.Count == 0)
        {
            return;
        }

        var mine = data.Deliveries.GroupBy(d => NameNormalizer.Key(d.Material)).ToDictionary(g => g.Key, g => g.Sum(d => d.Quantity));
        var differences = new List<string>();
        foreach (var key in mine.Keys.Union(data.ConsolidatedByMaterial.Keys).Order())
        {
            mine.TryGetValue(key, out var fromSheets);
            data.ConsolidatedByMaterial.TryGetValue(key, out var consolidated);
            if (fromSheets != consolidated)
            {
                differences.Add($"{key}: nodos {fromSheets:N0} / consolidado {consolidated:N0}");
            }
        }

        if (differences.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "CONSOLIDADO ENTREGAS", null, "TOTAL ENTREGADO",
                $"La hoja consolidada no coincide con las hojas por nodo en {differences.Count} material(es) " +
                $"(se carga el detalle de las hojas por nodo, que trae fechas): {string.Join("; ", differences.Take(8))}" +
                (differences.Count > 8 ? "…" : string.Empty)));
        }
    }

    // ------------------------------------------------------------------ aplicar

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        if (projectId is null)
        {
            throw new BusinessRuleException("Elija el proyecto donde se carga la bodega.");
        }

        var data = (WarehouseData)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName, ProjectId = projectId };
        report.Issues.AddRange(analysis.Report.Issues);
        report.TotalChecks.AddRange(analysis.Report.TotalChecks);

        await using var db = factory.CreateDbContext();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new BusinessRuleException("El proyecto seleccionado no existe.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var materials = await MaterialResolver.LoadAsync(db, ct);
        var contractors = await ContractorResolver.LoadAsync(db, ct);
        var sites = await db.Sites.Where(s => s.ProjectId == project.Id).ToListAsync(ct);

        // Resolver todos los materiales primero para tener los ids.
        var receiptMaterial = new Dictionary<WarehouseReceipt, Material>();
        foreach (var receipt in data.Receipts)
        {
            var material = materials.Resolve(receipt.Material, receipt.Unit, receipt.Model, receipt.Description);
            if (receipt.HasSerial)
            {
                material.HasSerial = true;
            }

            receiptMaterial[receipt] = material;
        }

        var deliveryMaterial = new Dictionary<WarehouseDelivery, Material>();
        foreach (var delivery in data.Deliveries)
        {
            deliveryMaterial[delivery] = materials.Resolve(delivery.Material, delivery.Unit);
        }

        foreach (var brigade in data.Deliveries.Select(d => d.Brigade).Distinct())
        {
            contractors.Resolve(brigade);
        }

        await db.SaveChangesAsync(ct);

        var existing = await db.InventoryMovements
            .Where(m => m.ProjectId == project.Id && !m.IsVoided && m.Reference != null
                        && (m.Reference.StartsWith(ReceiptPrefix) || m.Reference.StartsWith(DeliveryPrefix)))
            .ToListAsync(ct);

        var receiptsCreated = 0;
        var deliveriesCreated = 0;
        var skippedSame = 0;

        foreach (var group in data.Receipts.GroupBy(r => (r.Section, Material: receiptMaterial[r].Id)))
        {
            var reference = ReceiptPrefix + group.Key.Section;
            var have = existing.Where(m => m.Type == MovementType.Receipt && m.Reference == reference && m.MaterialId == group.Key.Material).Sum(m => m.Quantity);
            var want = group.Sum(r => r.Quantity);
            if (have >= want)
            {
                skippedSame++;
                continue;
            }

            var date = group.Max(r => r.Date);
            db.InventoryMovements.Add(new InventoryMovement
            {
                ProjectId = project.Id, Type = MovementType.Receipt, MaterialId = group.Key.Material, Quantity = want - have,
                Date = date, Reference = reference, Notes = "Cargado desde el Excel de bodega",
                IsOpeningBalance = true,
            });
            receiptsCreated++;
        }

        foreach (var group in data.Deliveries.GroupBy(d => (d.Source, d.NodeCode, d.Brigade, Material: deliveryMaterial[d].Id, d.Date)))
        {
            var site = group.Key.NodeCode is null ? null : sites.FirstOrDefault(s => string.Equals(s.Code, group.Key.NodeCode, StringComparison.OrdinalIgnoreCase));
            if (group.Key.NodeCode is not null && site is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "Entregas", null, group.Key.NodeCode,
                    $"El nodo {group.Key.NodeCode} no existe en el proyecto; la entrega se guarda sin nodo (anotado en las notas)."));
            }

            var contractor = contractors.Resolve(group.Key.Brigade);
            var reference = DeliveryPrefix + group.Key.Source;
            var want = group.Sum(d => d.Quantity);
            var have = existing.Where(m => m.Type == MovementType.Delivery && m.Reference == reference && m.MaterialId == group.Key.Material
                                           && m.Date == group.Key.Date && m.ContractorId == contractor?.Id).Sum(m => m.Quantity);
            if (have >= want)
            {
                skippedSame++;
                continue;
            }

            var first = group.First();
            var notes = first.Note;
            if (group.Key.NodeCode is not null && site is null)
            {
                notes = string.Join(" · ", new[] { $"Nodo {group.Key.NodeCode}", notes }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            db.InventoryMovements.Add(new InventoryMovement
            {
                ProjectId = project.Id, Type = MovementType.Delivery, MaterialId = group.Key.Material, Quantity = want - have,
                Date = group.Key.Date, SiteId = site?.Id, ContractorId = contractor?.Id, Reference = reference,
                Batch = first.Origin is { Length: > 0 } o ? (o.Length > 100 ? o[..100] : o) : null,
                Notes = notes,
            });
            deliveriesCreated++;
        }

        // SFP con serial: serial único por proyecto.
        var knownSerials = (await db.Transceivers.Where(t => t.ProjectId == project.Id).Select(t => t.Serial).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sfpCreated = 0;
        foreach (var sfp in data.Sfps)
        {
            if (!knownSerials.Add(sfp.Serial))
            {
                continue;
            }

            int? wavelength = null;
            var nm = Regex.Match(sfp.Description ?? string.Empty, @"(\d{3,4})\s*nm");
            if (nm.Success)
            {
                wavelength = int.Parse(nm.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            db.Transceivers.Add(new Transceiver
            {
                ProjectId = project.Id, Serial = sfp.Serial, Model = sfp.Model, WavelengthNm = wavelength,
                Status = TransceiverStatus.InStock, Notes = $"Ingreso {sfp.Date:dd/MM/yyyy}",
            });
            sfpCreated++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Validación contra el Excel con lo que quedó en la base.
        var saved = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && !m.IsVoided && m.Reference != null)
            .Select(m => new { m.Type, m.Quantity, m.Reference })
            .ToListAsync(ct);
        report.TotalChecks.Clear();
        if (data.ExcelReceivedTotal > 0)
        {
            report.TotalChecks.Add(new TotalCheck("Unidades recibidas del proveedor", data.ExcelReceivedTotal,
                saved.Where(m => m.Type == MovementType.Receipt && m.Reference!.StartsWith(ReceiptPrefix)).Sum(m => m.Quantity)));
        }

        report.TotalChecks.Add(new TotalCheck("Unidades entregadas a brigadas (detalle por nodo)", data.Deliveries.Sum(d => d.Quantity),
            saved.Where(m => m.Type == MovementType.Delivery && m.Reference!.StartsWith(DeliveryPrefix)).Sum(m => m.Quantity)));

        var negatives = await db.InventoryMovements.AsNoTracking()
            .Where(m => m.ProjectId == project.Id && !m.IsVoided && m.Material!.TracksStock)
            .GroupBy(m => new { m.MaterialId, m.Material!.Name })
            .Select(g => new
            {
                g.Key.Name,
                Stock = g.Sum(m => m.Type == MovementType.Receipt || m.Type == MovementType.Return ? m.Quantity
                    : m.Type == MovementType.Delivery ? -m.Quantity
                    : m.Type == MovementType.Adjustment ? (m.IsIncrease ? m.Quantity : -m.Quantity) : 0),
            })
            .Where(x => x.Stock < 0)
            .ToListAsync(ct);
        if (negatives.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "Bodega", null, null,
                $"{negatives.Count} material(es) quedan con stock negativo porque el Excel registra entregas sin ingreso: " +
                string.Join("; ", negatives.OrderBy(n => n.Stock).Take(12).Select(n => $"{n.Name} ({n.Stock:N0})")) +
                (negatives.Count > 12 ? "…" : string.Empty) + ". Registre el ingreso que falta en Bodega."));
        }

        report.Counts.MaterialsCreated = materials.Created;
        report.Counts.ContractorsCreated = contractors.Created;
        report.Summary.Add(new("Ingresos nuevos", receiptsCreated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Entregas nuevas", deliveriesCreated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Ya estaban cargados", skippedSame.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("SFP nuevos", sfpCreated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Materiales nuevos", materials.Created.ToString(CultureInfo.InvariantCulture)));
        report.Applied = true;
        return report;
    }
}

internal static class StringExtensions
{
    public static string? NullIfEmpty(this string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
