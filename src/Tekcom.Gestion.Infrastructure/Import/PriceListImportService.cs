using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record PlPrice(string Section, string Item, string? Type, string? Unit, decimal Price, int Order);

internal sealed record PlOrder(string Code, string? OrderNumber, int? HomesPassed, decimal Subtotal, decimal Vat, decimal Total,
    string? Contractor, string? Material, string? Construction, string? Liquidation);

internal sealed record PlCatalog(string Erp, string Name, string? Description, string? Model, string? Unit, bool HasSerial);

internal sealed class PriceListData
{
    public List<PlPrice> Prices { get; } = [];
    public List<PlOrder> Orders { get; } = [];
    public List<PlCatalog> Catalog { get; } = [];
    public decimal? ExcelTotalAmount { get; set; }
}

/// <summary>
/// Preciario de contratistas (precios target de ODN y feeder), órdenes por nodo (valor, brigada y liquidación)
/// y catálogo con códigos ERP. Es del proyecto FTTH; el catálogo completa los materiales que ya existen.
/// </summary>
public sealed class PriceListImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.PriceList;

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        var prices = book is null ? null : book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name) == "PRECIARIO FINAL");
        var orders = book is null ? null : Xl.Sheet(book, "SON_GENERAL");
        if (book is null || (prices is null && orders is null))
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var data = new PriceListData();
        if (prices is not null)
        {
            ReadPrices(prices, data);
        }

        if (orders is not null)
        {
            ReadOrders(orders, data, report);
        }

        var material = Xl.Sheet(book, "MATERIAL");
        if (material is not null)
        {
            ReadCatalog(material, data);
        }

        if (data.ExcelTotalAmount is { } excelTotal)
        {
            report.TotalChecks.Add(new TotalCheck("Monto total de las órdenes", Math.Round(excelTotal, 2), Math.Round(data.Orders.Sum(o => o.Total), 2)));
        }

        var unpriced = data.Prices.Where(p => p.Price == 0).Select(p => p.Item).ToList();
        if (unpriced.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Info, "PRECIARIO FINAL", null, "PRECIO TARGET", $"Sin precio (0): {string.Join(", ", unpriced)}."));
        }

        report.Summary.Add(new("Precios target", $"{data.Prices.Count(p => p.Section == "ODN")} de ODN y {data.Prices.Count(p => p.Section == "FEEDER")} de feeder"));
        report.Summary.Add(new("Órdenes por nodo", $"{data.Orders.Count} · ${data.Orders.Sum(o => o.Total):N2} con IVA"));
        report.Summary.Add(new("Catálogo con código ERP", data.Catalog.Count.ToString(CultureInfo.InvariantCulture)));
        report.Issues.Add(new ImportIssue(ImportSeverity.Info, "(archivo)", null, null,
            "Las hojas de balance por brigada (E-C, H-B, O-P) y los BOM por nodo no se cargan: lo entregado ya sale de la bodega y la proyección de cada nodo ya está en el sistema."));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.PriceList,
            Title = "Preciario, órdenes por nodo y códigos ERP",
            Description = "Precios target por contratista, valor de la orden de cada nodo y el catálogo de materiales con su código.",
            Report = report,
            NeedsProject = true,
            Payload = data,
        });
    }

    private static void ReadPrices(IXLWorksheet sheet, PriceListData data)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var section = string.Empty;
        var order = 0;
        for (var r = 1; r <= last; r++)
        {
            var b = Xl.Text(sheet, r, 2);
            var key = NameNormalizer.Key(b);
            if (key == "ITEM")
            {
                continue;
            }

            if (b.Length > 0 && Xl.Text(sheet, r, 3).Length == 0 && Xl.Text(sheet, r, 5).Length == 0)
            {
                section = key.Contains("FEEDER", StringComparison.Ordinal) ? "FEEDER" : "ODN";
                continue;
            }

            if (section.Length == 0 || b.Length == 0 || Xl.Number(sheet, r, 5) is not { } price)
            {
                continue;
            }

            data.Prices.Add(new PlPrice(section, b.Trim(), Xl.Text(sheet, r, 3).NullIfEmpty(), Xl.Text(sheet, r, 4).NullIfEmpty(), price, order++));
        }
    }

    private static void ReadOrders(IXLWorksheet sheet, PriceListData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 1; r <= 6; r++)
        {
            if (NameNormalizer.Key(Xl.Text(sheet, r, 6)).Contains("MONTO TOTAL", StringComparison.Ordinal))
            {
                data.ExcelTotalAmount = Xl.Number(sheet, r, 7);
            }
        }

        var header = 0;
        for (var r = 1; r <= last; r++)
        {
            if (NameNormalizer.Key(Xl.Text(sheet, r, 3)) == "HP")
            {
                header = r;
                break;
            }
        }

        if (header == 0)
        {
            return;
        }

        for (var r = header + 1; r <= last; r++)
        {
            var code = Xl.Text(sheet, r, 2);
            if (code.Length == 0)
            {
                continue;
            }

            var total = Xl.Number(sheet, r, 6);
            if (total is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, "TOTAL", $"'{code}' no trae total; se omite."));
                continue;
            }

            var hp = Xl.Number(sheet, r, 3);
            var contractor = Xl.Text(sheet, r, 8).NullIfEmpty();
            data.Orders.Add(new PlOrder(code, Xl.Text(sheet, r, 7).NullIfEmpty(), hp is null ? null : (int)hp.Value,
                Xl.Number(sheet, r, 4) ?? 0, Xl.Number(sheet, r, 5) ?? 0, total.Value, contractor is null ? null : Xl.TitleCase(contractor),
                Xl.Text(sheet, r, 9).NullIfEmpty(), Xl.Text(sheet, r, 10).NullIfEmpty(), Xl.Text(sheet, r, 11).NullIfEmpty()));
        }

        // "SON17 / 18" trae un HP que no es de un nodo (17778): se avisa.
        foreach (var combined in data.Orders.Where(o => o.Code.Contains('/')))
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, null, "HP",
                $"La orden '{combined.Code}' agrupa dos nodos y trae HP {combined.HomesPassed:N0}, que parece un error de captura; se guarda tal cual."));
        }
    }

    private static void ReadCatalog(IXLWorksheet sheet, PriceListData data)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var r = 2; r <= last; r++)
        {
            var erp = Xl.Text(sheet, r, 2);
            var name = Xl.Text(sheet, r, 4);
            if (erp.Length == 0 || name.Length == 0)
            {
                continue;
            }

            data.Catalog.Add(new PlCatalog(erp, name, Xl.Text(sheet, r, 3).NullIfEmpty(), Xl.Text(sheet, r, 5).NullIfEmpty(),
                Xl.Text(sheet, r, 6).NullIfEmpty(), NameNormalizer.Key(Xl.Text(sheet, r, 7)) == "SI"));
        }
    }

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        if (projectId is null)
        {
            throw new BusinessRuleException("Elija el proyecto FTTH al que pertenecen los precios y las órdenes.");
        }

        var data = (PriceListData)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName, ProjectId = projectId };
        report.Issues.AddRange(analysis.Report.Issues);

        await using var db = factory.CreateDbContext();
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct)
            ?? throw new BusinessRuleException("El proyecto seleccionado no existe.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Precios: si el proyecto ya tiene precios (cargados o editados) no se tocan.
        var pricesLoaded = false;
        if (!await db.PriceItems.AnyAsync(p => p.ProjectId == project.Id, ct))
        {
            pricesLoaded = true;
            foreach (var price in data.Prices)
            {
                db.PriceItems.Add(new PriceItem
                {
                    ProjectId = project.Id, Section = price.Section, Item = price.Item, Type = price.Type, Unit = price.Unit,
                    TargetPrice = price.Price, SortOrder = price.Order,
                });
            }
        }

        // Órdenes: las nuevas se crean; las que ya existen solo completan lo que esté vacío (el sistema manda).
        var sites = await db.Sites.Where(s => s.ProjectId == project.Id).ToListAsync(ct);
        var orders = await db.ContractOrders.Where(o => o.ProjectId == project.Id).ToListAsync(ct);
        var created = 0;
        foreach (var item in data.Orders)
        {
            var order = orders.FirstOrDefault(o => string.Equals(o.Code, item.Code, StringComparison.OrdinalIgnoreCase));
            if (order is null)
            {
                order = new ContractOrder { ProjectId = project.Id, Code = item.Code, Subtotal = item.Subtotal, Vat = item.Vat, Total = item.Total };
                db.ContractOrders.Add(order);
                orders.Add(order);
                created++;
            }

            order.SiteId ??= sites.FirstOrDefault(s => string.Equals(s.Code, item.Code, StringComparison.OrdinalIgnoreCase))?.Id;
            order.OrderNumber ??= item.OrderNumber;
            order.HomesPassed ??= item.HomesPassed;
            order.ContractorName ??= item.Contractor;
            order.MaterialStatus ??= item.Material;
            order.ConstructionStatus ??= item.Construction;
            order.LiquidationStatus ??= item.Liquidation;
        }

        // Catálogo ERP: completa código, modelo y descripción de los materiales existentes (o los crea).
        var materials = await MaterialResolver.LoadAsync(db, ct);
        foreach (var item in data.Catalog)
        {
            var material = materials.Resolve(item.Name, item.Unit, item.Model, item.Description, item.Erp);
            if (item.HasSerial)
            {
                material.HasSerial = true;
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var savedTotal = await db.ContractOrders.AsNoTracking().Where(o => o.ProjectId == project.Id).SumAsync(o => o.Total, ct);
        if (data.ExcelTotalAmount is { } excelTotal)
        {
            report.TotalChecks.Clear();
            report.TotalChecks.Add(new TotalCheck("Monto total de las órdenes", Math.Round(excelTotal, 2), Math.Round(savedTotal, 2)));
        }

        report.Counts.MaterialsCreated = materials.Created;
        report.Summary.Add(new("Precios guardados", pricesLoaded ? data.Prices.Count.ToString(CultureInfo.InvariantCulture) : "ya había precios; no se tocan"));
        report.Summary.Add(new("Órdenes nuevas", created.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Órdenes actualizadas", (data.Orders.Count - created).ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Materiales con código ERP", data.Catalog.Count.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Materiales nuevos en el catálogo", materials.Created.ToString(CultureInfo.InvariantCulture)));
        report.Applied = true;
        return report;
    }
}
