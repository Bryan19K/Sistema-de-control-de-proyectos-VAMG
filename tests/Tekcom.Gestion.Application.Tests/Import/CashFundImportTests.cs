using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Import;

namespace Tekcom.Gestion.Application.Tests.Import;

/// <summary>Caja chica y viáticos con un fondo por hoja (CC - ddmmaa / CCV - ddmmaa), armados aquí con la misma forma del Excel real.</summary>
public class CashFundImportTests
{
    private static IXLWorksheet Fund(XLWorkbook book, string name, string title, decimal assigned, params (int Item, DateTime? Date, string? Invoice, string? Commerce, string Desc, string Cost, decimal? Amount)[] rows)
    {
        var s = book.AddWorksheet(name);
        s.Cell(1, 1).Value = title; s.Cell(1, 7).Value = "ASIGNADO"; s.Cell(2, 7).Value = assigned;
        string[] heads = ["ITEM", "FECHA", "NUMERO DE FACTURA", "COMERCIO", "DESCRIPCION", "CENTRO DE COSTO", "MONTO"];
        for (var c = 0; c < heads.Length; c++) { s.Cell(3, c + 1).Value = heads[c]; }
        var r = 4;
        foreach (var row in rows)
        {
            s.Cell(r, 1).Value = row.Item;
            if (row.Date is not null) { s.Cell(r, 2).Value = row.Date.Value; }
            if (row.Invoice is not null) { s.Cell(r, 3).Value = row.Invoice; }
            if (row.Commerce is not null) { s.Cell(r, 4).Value = row.Commerce; }
            if (row.Desc.Length > 0) { s.Cell(r, 5).Value = row.Desc; }
            s.Cell(r, 6).Value = row.Cost;
            if (row.Amount is not null) { s.Cell(r, 7).Value = row.Amount.Value; }
            r++;
        }

        s.Cell(r, 5).Value = "TOTAL "; s.Cell(r, 7).Value = rows.Sum(x => x.Amount ?? 0);
        return s;
    }

    private static byte[] Book(decimal secondAmount = 25m, bool withThird = true)
    {
        using var book = new XLWorkbook();
        Fund(book, "CC - 210926", "CONTROL DE CAJA CHICA - JUAN LANDAVERDE", 500,
            (1, new DateTime(2026, 9, 19), "DTE-4987", "POLLO CAMPERO ", "DESAYUNO PERSONAL TEKCOM ", "2601-2607", 33.9m),
            (2, new DateTime(2026, 9, 21), "DTE-32574", "DLC ", "GASOLINA JC - SONSONATE (RECEPCION DE EQUIPOS)", "2601-2607", secondAmount),
            (3, null, null, null, string.Empty, "-", null));
        if (withThird)
        {
            Fund(book, "CCV - 031026", "CONTROL DE CAJA CHICA PARA VIATICOS - JUAN LANDAVERDE", 150,
                (1, new DateTime(2026, 10, 10), "-", "-", "Viatico - Doris Yamileth Lopez Garcia  06 - 07 - 09/10/2026", "2601-2607", 9m));
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task Lee_un_fondo_por_hoja_con_su_asignado_tipo_y_total()
    {
        using var db = await TestDb.CreateAsync();
        var pack = await new ExpensesImportService(db).AnalyzeAsync(Book(), "caja.xlsx");

        pack.Should().NotBeNull();
        pack!.Kind.Should().Be(PackKind.Expenses);
        pack.Report.TotalChecks.Should().Contain(c => c.Name.Contains("21/09/2026")).Which.Matches.Should().BeTrue();
        pack.Report.Issues.Should().NotContain(i => i.Severity == ImportSeverity.Warning, "el renglón numerado sin llenar no es un error");
        pack.Report.Issues.Should().Contain(i => i.Message.Contains("reemplaza lo cargado antes"));
    }

    [Fact]
    public async Task Carga_los_fondos_y_gastos_y_repetirla_no_duplica()
    {
        using var db = await TestDb.CreateAsync();
        var importer = new ExpensesImportService(db);
        await importer.ApplyAsync((await importer.AnalyzeAsync(Book(), "caja.xlsx"))!, null);
        await importer.ApplyAsync((await importer.AnalyzeAsync(Book(), "caja.xlsx"))!, null);

        await using var ctx = db.CreateDbContext();
        var funds = await ctx.ExpenseFunds.OrderBy(f => f.Name).ToListAsync();
        funds.Select(f => (f.Name, f.Kind, f.AssignedAmount)).Should().Equal(
            ("Caja chica · 21/09/2026", ExpenseKind.PettyCash, 500m),
            ("Viáticos · 03/10/2026", ExpenseKind.Travel, 150m));
        (await ctx.ExpenseEntries.CountAsync()).Should().Be(3);
        (await ctx.ExpenseEntries.Where(e => e.Kind == ExpenseKind.PettyCash).SumAsync(e => e.Total)).Should().Be(58.9m);
        var travel = await ctx.ExpenseEntries.SingleAsync(e => e.Kind == ExpenseKind.Travel);
        (travel.Supplier, travel.InvoiceNumber).Should().Be((null, null), "el guion del Excel no es un dato");
    }

    [Fact]
    public async Task Actualiza_lo_que_cambio_en_el_Excel_con_el_mismo_registro()
    {
        using var db = await TestDb.CreateAsync();
        var importer = new ExpensesImportService(db);
        await importer.ApplyAsync((await importer.AnalyzeAsync(Book(secondAmount: 25m), "caja.xlsx"))!, null);
        int idBefore;
        await using (var ctx = db.CreateDbContext()) { idBefore = (await ctx.ExpenseEntries.SingleAsync(e => e.Description.StartsWith("GASOLINA JC"))).Id; }

        await importer.ApplyAsync((await importer.AnalyzeAsync(Book(secondAmount: 30m), "caja.xlsx"))!, null);

        await using var after = db.CreateDbContext();
        var entry = await after.ExpenseEntries.SingleAsync(e => e.Description.StartsWith("GASOLINA JC"));
        (entry.Id, entry.Total).Should().Be((idBefore, 30m));
        (await after.ExpenseEntries.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Reconoce_lo_cargado_antes_en_el_formato_viejo_lo_actualiza_y_retira_lo_que_ya_no_esta()
    {
        using var db = await TestDb.CreateAsync();
        int keptId;
        int movedId;
        await using (var ctx = db.CreateDbContext())
        {
            var oldFund = new ExpenseFund { Name = "Caja chica · fondo 1", Kind = ExpenseKind.PettyCash, AssignedAmount = 500, SourceKey = "CAJA:1" };
            ctx.ExpenseFunds.Add(oldFund);
            var same = new ExpenseEntry { Kind = ExpenseKind.PettyCash, Fund = oldFund, Date = new DateOnly(2026, 9, 19), Description = "DESAYUNO PERSONAL TEKCOM", Total = 33.9m, SourceKey = "CAJA:1:1:20260919:33.9" };
            var changed = new ExpenseEntry { Kind = ExpenseKind.PettyCash, Fund = oldFund, Date = new DateOnly(2026, 9, 21), Description = "GASOLINA JC - SONSONATE (RECEPCION DE EQUIPOS)", Total = 20m, SourceKey = "CAJA:1:2:20260921:20" };
            var gone = new ExpenseEntry { Kind = ExpenseKind.PettyCash, Fund = oldFund, Date = new DateOnly(2026, 11, 3), Description = "GASOLINA YA NO ESTA EN EL EXCEL", Total = 40m, SourceKey = "CAJA:1:3:20261103:40" };
            var manual = new ExpenseEntry { Kind = ExpenseKind.PettyCash, Date = new DateOnly(2026, 11, 1), Description = "Gasto que escribió una persona", Total = 5m, SourceKey = "MANUAL:abc" };
            ctx.ExpenseEntries.AddRange(same, changed, gone, manual);
            await ctx.SaveChangesAsync();
            (keptId, movedId) = (same.Id, changed.Id);
        }

        var importer = new ExpensesImportService(db);
        var report = await importer.ApplyAsync((await importer.AnalyzeAsync(Book(secondAmount: 25m), "caja.xlsx"))!, null);

        await using var after = db.CreateDbContext();
        var entries = await after.ExpenseEntries.Include(e => e.Fund).ToListAsync();
        entries.Single(e => e.Id == keptId).Fund!.Name.Should().Be("Caja chica · 21/09/2026");
        entries.Single(e => e.Id == movedId).Total.Should().Be(25m, "el monto se corrigió en el Excel");
        entries.Should().NotContain(e => e.Description.Contains("YA NO ESTA"), "ya no está en el Excel: se retira");
        entries.Should().Contain(e => e.SourceKey == "MANUAL:abc", "lo que escribió una persona no se toca");
        (await after.ExpenseEntries.IgnoreQueryFilters().CountAsync(e => e.IsDeleted)).Should().Be(1, "queda como borrado lógico");
        (await after.ExpenseFunds.AnyAsync(f => f.SourceKey == "CAJA:1")).Should().BeFalse("el fondo viejo quedó vacío");
        report.Summary.Should().Contain(s => s.Key.Contains("retirados") && s.Value == "1");
        report.TotalChecks.Should().OnlyContain(c => c.Matches);
    }
}
