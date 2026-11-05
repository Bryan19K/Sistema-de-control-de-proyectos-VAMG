using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Import;

internal sealed record ExFund(string Key, string Name, ExpenseKind Kind, decimal Assigned, decimal? ExcelSpent);

internal sealed record ExEntry(string Key, string? FundKey, ExpenseKind Kind, DateOnly Date, string Description, string? Category,
    string? Supplier, string? Invoice, string? Unit, decimal? Quantity, decimal? UnitCost, decimal? Subtotal, decimal? Vat, decimal Total,
    string? Status, string? PaidBy, string? CostCenter);

internal sealed class ExpenseData
{
    public List<ExFund> Funds { get; } = [];
    public List<ExEntry> Entries { get; } = [];
    public decimal? ExcelPurchaseSubtotal { get; set; }
    public decimal? ExcelPurchaseVat { get; set; }
    public decimal? ExcelPurchaseTotal { get; set; }
}

/// <summary>
/// Carga los gastos del proyecto: compras con proveedor (con IVA), caja chica con facturas y viáticos.
/// Cada fila se identifica por una clave, así que importar otra vez actualiza sin duplicar.
/// </summary>
public sealed class ExpensesImportService(IDbContextFactory<ApplicationDbContext> factory) : IDataPackImporter
{
    public PackKind Kind => PackKind.Expenses;

    public Task<PackAnalysis?> AnalyzeAsync(byte[] file, string fileName, CancellationToken ct = default)
    {
        using var book = Xl.Open(file);
        if (book is null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var purchases = book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name) == "GASTOS");
        var petty = book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name).StartsWith("CONTROL DE CAJA CHICA", StringComparison.Ordinal));
        var travel = book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name).StartsWith("VIATICOS", StringComparison.Ordinal));
        if (purchases is null && petty is null && travel is null)
        {
            return Task.FromResult<PackAnalysis?>(null);
        }

        var report = new ImportReport { FileName = fileName };
        var data = new ExpenseData();

        if (purchases is not null)
        {
            ReadPurchases(purchases, data, report);
        }

        if (petty is not null)
        {
            ReadFunds(petty, "CAJA", "Caja chica", ExpenseKind.PettyCash, itemCol: 1, dateCol: 2, invoiceCol: 4, commerceCol: 5, descCol: 6, costCol: 8, amountCol: 9, data, report);
        }

        if (travel is not null)
        {
            ReadFunds(travel, "VIAT", "Viáticos", ExpenseKind.Travel, itemCol: 2, dateCol: 3, invoiceCol: 4, commerceCol: 5, descCol: 6, costCol: 7, amountCol: 8, data, report);
        }

        var incomplete = data.Entries.Where(e => e.Kind == ExpenseKind.PettyCash && (e.Invoice is null || e.Invoice.TrimEnd('-', ' ', 'D', 'T', 'E') == string.Empty)).ToList();
        if (incomplete.Count > 0)
        {
            report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "Caja chica", null, "NUMERO DE FACTURA",
                $"{incomplete.Count} gasto(s) de caja chica sin número de factura completo: " +
                string.Join("; ", incomplete.Take(6).Select(e => $"{e.Date:dd/MM} {e.Description[..Math.Min(40, e.Description.Length)]} (${e.Total:N2})")) +
                (incomplete.Count > 6 ? "…" : string.Empty)));
        }

        foreach (var fund in data.Funds)
        {
            var spent = data.Entries.Where(e => e.FundKey == fund.Key).Sum(e => e.Total);
            if (fund.Assigned > 0 && spent > fund.Assigned)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, fund.Name, null, null,
                    $"El fondo se pasó del monto asignado: gastado ${spent:N2} de ${fund.Assigned:N2} (saldo ${fund.Assigned - spent:N2})."));
            }

            if (fund.ExcelSpent is { } excelSpent)
            {
                report.TotalChecks.Add(new TotalCheck($"{fund.Name} · gastado", excelSpent, spent));
            }
        }

        if (data.ExcelPurchaseTotal is { } excelTotal)
        {
            var mine = data.Entries.Where(e => e.Kind == ExpenseKind.Purchase).ToList();
            report.TotalChecks.Add(new TotalCheck("Compras · subtotal sin IVA", Math.Round(data.ExcelPurchaseSubtotal ?? 0, 2), Math.Round(mine.Sum(e => e.Subtotal ?? 0), 2)));
            report.TotalChecks.Add(new TotalCheck("Compras · total con IVA", Math.Round(excelTotal, 2), Math.Round(mine.Sum(e => e.Total), 2)));
        }

        report.Summary.Add(new("Compras", $"{data.Entries.Count(e => e.Kind == ExpenseKind.Purchase)} · ${data.Entries.Where(e => e.Kind == ExpenseKind.Purchase).Sum(e => e.Total):N2} con IVA"));
        report.Summary.Add(new("Caja chica", $"{data.Entries.Count(e => e.Kind == ExpenseKind.PettyCash)} gastos en {data.Funds.Count(f => f.Kind == ExpenseKind.PettyCash)} fondos · ${data.Entries.Where(e => e.Kind == ExpenseKind.PettyCash).Sum(e => e.Total):N2}"));
        report.Summary.Add(new("Viáticos", $"{data.Entries.Count(e => e.Kind == ExpenseKind.Travel)} registros en {data.Funds.Count(f => f.Kind == ExpenseKind.Travel)} fondos · ${data.Entries.Where(e => e.Kind == ExpenseKind.Travel).Sum(e => e.Total):N2}"));

        return Task.FromResult<PackAnalysis?>(new PackAnalysis
        {
            Kind = PackKind.Expenses,
            Title = "Gastos: compras, caja chica y viáticos",
            Description = "Compras con IVA, gastos de caja chica con factura y viáticos, con el saldo de cada fondo.",
            Report = report,
            Payload = data,
        });
    }

    private static string? CostCenter(string text)
    {
        var t = text.Trim();
        return t.Length == 0 || t.All(c => c is '.' or '-' or ' ') ? null : t;
    }

    private static void ReadPurchases(IXLWorksheet sheet, ExpenseData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var header = 0;
        for (var r = 1; r <= last; r++)
        {
            if (NameNormalizer.Key(Xl.Text(sheet, r, 2)) == "N" && NameNormalizer.Key(Xl.Text(sheet, r, 3)) == "FECHA")
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
            var label = NameNormalizer.Key(Xl.Text(sheet, r, 2));
            if (label == "TOTALES")
            {
                data.ExcelPurchaseSubtotal = Xl.Number(sheet, r, 10);
                data.ExcelPurchaseVat = Xl.Number(sheet, r, 11);
                data.ExcelPurchaseTotal = Xl.Number(sheet, r, 12);
                break;
            }

            if (Xl.Number(sheet, r, 2) is not { } n)
            {
                continue;
            }

            var date = Xl.Date(sheet, r, 3);
            var description = Xl.Text(sheet, r, 4);
            var total = Xl.Number(sheet, r, 12);
            var subtotal = Xl.Number(sheet, r, 10);
            var vat = Xl.Number(sheet, r, 11);
            if (date is null || description.Length == 0 || (total is null && subtotal is null))
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, null, "Compra incompleta (fecha, concepto o monto): se omite."));
                continue;
            }

            total ??= subtotal + (vat ?? 0);
            data.Entries.Add(new ExEntry($"GASTOS:{(int)n}:{date:yyyyMMdd}:{total:0.####}", null, ExpenseKind.Purchase, date.Value, description,
                Xl.Text(sheet, r, 5).NullIfEmpty(), Xl.Text(sheet, r, 6).NullIfEmpty(), null, Xl.Text(sheet, r, 7).NullIfEmpty(),
                Xl.Number(sheet, r, 8), Xl.Number(sheet, r, 9), subtotal, vat, total!.Value, Xl.Text(sheet, r, 13).NullIfEmpty(),
                Xl.Text(sheet, r, 14).NullIfEmpty(), null));
        }
    }

    private static void ReadFunds(IXLWorksheet sheet, string keyPrefix, string label, ExpenseKind kind, int itemCol, int dateCol,
        int invoiceCol, int commerceCol, int descCol, int costCol, int amountCol, ExpenseData data, ImportReport report)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var block = 1;
        var blockEntries = new List<ExEntry>();
        decimal? spent = null;
        decimal? assigned = null;

        // "ASIGNADO: $500" en el encabezado de la hoja de caja chica sirve de respaldo.
        decimal? headerAssigned = null;
        for (var r = 1; r <= 3 && headerAssigned is null; r++)
        {
            for (var c = 1; c <= 12; c++)
            {
                var text = Xl.Text(sheet, r, c);
                if (NameNormalizer.Key(text).StartsWith("ASIGNADO", StringComparison.Ordinal))
                {
                    var digits = new string(text.Where(ch => char.IsDigit(ch) || ch == '.').ToArray());
                    headerAssigned = decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : Xl.Number(sheet, r, c + 1);
                }
            }
        }

        void CloseBlock()
        {
            if (blockEntries.Count == 0)
            {
                spent = null;
                assigned = null;
                return;
            }

            var key = $"{keyPrefix}:{block}";
            data.Funds.Add(new ExFund(key, $"{label} · fondo {block}", kind, assigned ?? headerAssigned ?? 0, spent));
            foreach (var entry in blockEntries)
            {
                data.Entries.Add(entry with { FundKey = key });
            }

            blockEntries.Clear();
            spent = null;
            assigned = null;
            block++;
        }

        for (var r = 1; r <= last; r++)
        {
            var rowLabel = string.Empty;
            for (var c = 1; c < amountCol; c++)
            {
                var key = NameNormalizer.Key(Xl.Text(sheet, r, c));
                if (key.StartsWith("TOTAL GASTADO", StringComparison.Ordinal) || key.StartsWith("TOTAL VIATICOS", StringComparison.Ordinal)
                    || key.StartsWith("MONTO ASIGNADO", StringComparison.Ordinal) || key.StartsWith("DISPONIBLE", StringComparison.Ordinal)
                    || key.StartsWith("SALDO DISPONIBLE", StringComparison.Ordinal) || key.StartsWith("N DE REGISTROS", StringComparison.Ordinal))
                {
                    rowLabel = key;
                    break;
                }
            }

            if (rowLabel.Length > 0)
            {
                var value = Xl.Number(sheet, r, amountCol);
                if (rowLabel.StartsWith("TOTAL", StringComparison.Ordinal)) { spent = value; }
                else if (rowLabel.StartsWith("MONTO ASIGNADO", StringComparison.Ordinal)) { assigned = value; }
                else if (rowLabel.Contains("DISPONIBLE", StringComparison.Ordinal)) { CloseBlock(); }
                continue;
            }

            if (Xl.Number(sheet, r, itemCol) is not { } item || Xl.Text(sheet, r, descCol).Length == 0)
            {
                continue;
            }

            var date = Xl.Date(sheet, r, dateCol);
            var amount = Xl.Number(sheet, r, amountCol);
            if (date is null || amount is null)
            {
                report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet.Name, r, null,
                    $"Gasto sin fecha o monto ('{Xl.Text(sheet, r, descCol)}'): se omite."));
                continue;
            }

            var invoice = Xl.Text(sheet, r, invoiceCol).NullIfEmpty();
            var commerce = Xl.Text(sheet, r, commerceCol).NullIfEmpty();
            blockEntries.Add(new ExEntry($"{keyPrefix}:{block}:{(int)item}:{date:yyyyMMdd}:{amount:0.####}", null, kind, date.Value,
                Xl.Text(sheet, r, descCol), null, commerce is "-" ? null : commerce, invoice is "-" ? null : invoice, null, null, null, null, null,
                amount.Value, null, null, CostCenter(Xl.Text(sheet, r, costCol))));
        }

        // Si el último bloque no trae fila "DISPONIBLE", igual se guarda.
        CloseBlock();
    }

    public async Task<ImportReport> ApplyAsync(PackAnalysis analysis, int? projectId, CancellationToken ct = default)
    {
        var data = (ExpenseData)analysis.Payload!;
        var report = new ImportReport { FileName = analysis.Report.FileName };
        report.Issues.AddRange(analysis.Report.Issues);

        await using var db = factory.CreateDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var projects = await db.Projects.AsNoTracking().ToListAsync(ct);
        var funds = await db.ExpenseFunds.ToListAsync(ct);
        var created = 0;
        var updated = 0;
        foreach (var item in data.Funds)
        {
            var fund = funds.FirstOrDefault(f => f.SourceKey == item.Key);
            if (fund is null)
            {
                fund = new ExpenseFund { SourceKey = item.Key };
                db.ExpenseFunds.Add(fund);
                funds.Add(fund);
            }

            if (fund.Id != 0)
            {
                continue;
            }

            fund.Name = item.Name;
            fund.Kind = item.Kind;
            fund.AssignedAmount = item.Assigned;
        }

        await db.SaveChangesAsync(ct);

        var existing = (await db.ExpenseEntries.ToListAsync(ct)).ToDictionary(e => e.SourceKey);
        foreach (var item in data.Entries)
        {
            var project = item.CostCenter is null ? null : projects.FirstOrDefault(p => string.Equals(p.Code, item.CostCenter, StringComparison.OrdinalIgnoreCase));
            var fundId = item.FundKey is null ? (int?)null : funds.First(f => f.SourceKey == item.FundKey).Id;
            if (existing.ContainsKey(item.Key))
            {
                updated++;
                continue;
            }

            var entry = new ExpenseEntry { SourceKey = item.Key };
            db.ExpenseEntries.Add(entry);
            existing[item.Key] = entry;
            created++;

            entry.Kind = item.Kind;
            entry.FundId = fundId;
            entry.Date = item.Date;
            entry.Description = item.Description.Length > 400 ? item.Description[..400] : item.Description;
            entry.Category = item.Category;
            entry.Supplier = item.Supplier;
            entry.InvoiceNumber = item.Invoice;
            entry.Unit = item.Unit;
            entry.Quantity = item.Quantity;
            entry.UnitCost = item.UnitCost;
            entry.Subtotal = item.Subtotal;
            entry.Vat = item.Vat;
            entry.Total = item.Total;
            entry.Status = item.Status;
            entry.PaidBy = item.PaidBy;
            entry.CostCenter = item.CostCenter;
            entry.ProjectId = project?.Id;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // Validar contra el Excel con lo guardado.
        report.TotalChecks.Clear();
        var keys = data.Entries.Select(e => e.Key).ToHashSet();
        var saved = await db.ExpenseEntries.AsNoTracking().ToListAsync(ct);
        foreach (var fund in data.Funds.Where(f => f.ExcelSpent is not null))
        {
            var id = funds.First(f => f.SourceKey == fund.Key).Id;
            report.TotalChecks.Add(new TotalCheck($"{fund.Name} · gastado", fund.ExcelSpent!.Value, saved.Where(e => e.FundId == id).Sum(e => e.Total)));
        }

        if (data.ExcelPurchaseTotal is { } total)
        {
            var purchases = saved.Where(e => e.Kind == ExpenseKind.Purchase && keys.Contains(e.SourceKey)).ToList();
            report.TotalChecks.Add(new TotalCheck("Compras · subtotal sin IVA", Math.Round(data.ExcelPurchaseSubtotal ?? 0, 2), Math.Round(purchases.Sum(e => e.Subtotal ?? 0), 2)));
            report.TotalChecks.Add(new TotalCheck("Compras · total con IVA", Math.Round(total, 2), Math.Round(purchases.Sum(e => e.Total), 2)));
        }

        var linked = saved.Count(e => e.ProjectId is not null);
        report.Summary.Add(new("Gastos nuevos", created.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Gastos que ya estaban (no se tocan)", updated.ToString(CultureInfo.InvariantCulture)));
        report.Summary.Add(new("Enlazados a un proyecto por centro de costos", $"{linked} de {saved.Count}"));
        report.Applied = true;
        return report;
    }
}
