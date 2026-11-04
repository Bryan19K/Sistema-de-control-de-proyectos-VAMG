using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Import;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Import;

public class ExcelImportTests
{
    private const string RealFile = "docs/excel-origen/PROYECTO_FTTH_2026_61.xlsx";
    private static readonly ImportTarget NewProject = new(null, "2601-2607", "Construcción de FTTH - Sonsonate");

    private static ExcelImportService Service(TestDb db) =>
        new(db, new QuantityService(db), db.Clock);

    private static async Task<ImportAnalysis> AnalyzeRealAsync(ExcelImportService service)
    {
        await using var stream = File.OpenRead(TestDb.RepoFile(RealFile));
        return await service.AnalyzeAsync(stream, "PROYECTO_FTTH_2026_61.xlsx");
    }

    [Fact]
    public async Task Analizar_el_excel_real_lee_los_20_nodos_sin_errores()
    {
        using var db = await TestDb.CreateAsync();
        var analysis = await AnalyzeRealAsync(Service(db));

        analysis.Report.Errors.Should().Be(0, string.Join("; ", analysis.Report.Issues.Where(i => i.Severity == ImportSeverity.Error).Select(i => i.Message)));
        analysis.Workbook.Nodes.Should().HaveCount(20);
        analysis.Workbook.Nodes.Select(n => n.Code).Should().Contain(["SON19", "SON22", "SON10", "SON06"]);
        analysis.Report.SuggestedProjectCode.Should().Be("2601-2607");
    }

    [Fact]
    public async Task Analizar_reporta_la_columna_de_mangas_como_no_importada()
    {
        using var db = await TestDb.CreateAsync();
        var analysis = await AnalyzeRealAsync(Service(db));

        analysis.Report.Issues.Should().Contain(i =>
            i.Severity == ImportSeverity.Warning && i.Column != null && i.Column.Contains("MANGAS"));
    }

    [Fact]
    public async Task Analizar_los_totales_leidos_coinciden_con_la_fila_TOTAL_del_excel()
    {
        using var db = await TestDb.CreateAsync();
        var analysis = await AnalyzeRealAsync(Service(db));

        analysis.Report.TotalChecks.Should().NotBeEmpty();
        analysis.Report.TotalChecks.Where(c => !c.Matches)
            .Should().BeEmpty(string.Join("; ", analysis.Report.TotalChecks.Where(c => !c.Matches).Select(c => $"{c.Name}: {c.ExcelValue} vs {c.SystemValue}")));

        Total(analysis.Report, "Cable FC · proyectado").Should().Be(886);
        Total(analysis.Report, "Cable FC · instalado").Should().Be(535);
        Total(analysis.Report, "Cable MPO · proyectado").Should().Be(76);
        Total(analysis.Report, "Cable MPO · instalado").Should().Be(37);
        Total(analysis.Report, "Fusiones · proyectado").Should().Be(146);
        Total(analysis.Report, "Fusiones · instalado").Should().Be(76);
        // El Excel no trae total de cruces proyectados: se comprueba contra la suma de los nodos leídos.
        analysis.Workbook.Nodes.SelectMany(n => n.Quantities).Where(q => q.Group == QuantityGroup.Crossing).Sum(q => q.Projected ?? 0).Should().Be(31);
        Total(analysis.Report, "Cruces americanos · instalado").Should().Be(16);
        Total(analysis.Report, "Feeder · metros proyectados").Should().Be(41000);
        Total(analysis.Report, "Feeder · metros instalados").Should().Be(15052);
    }

    private static decimal Total(ImportReport report, string name) =>
        report.TotalChecks.Single(c => c.Name == name).SystemValue;

    [Fact]
    public async Task Importar_crea_proyecto_nodos_contratistas_y_valida_los_totales_contra_la_base()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        var analysis = await AnalyzeRealAsync(service);

        var report = await service.ApplyAsync(analysis, NewProject);

        report.Applied.Should().BeTrue();
        report.Counts.SitesCreated.Should().Be(20);
        report.Counts.ContractorsCreated.Should().Be(9, "el feeder usa la misma brigada Optical Prescision, sin duplicarla");
        report.Counts.SupervisorsCreated.Should().Be(3);
        report.TotalsMatch.Should().BeTrue(string.Join("; ", report.TotalChecks.Where(c => !c.Matches).Select(c => $"{c.Name}: {c.ExcelValue} vs {c.SystemValue}")));

        await using var context = db.CreateDbContext();
        (await context.Sites.CountAsync()).Should().Be(20);
        (await context.Contractors.CountAsync()).Should().Be(9);
        var project = await context.Projects.Include(p => p.Client).SingleAsync();
        project.Client!.Name.Should().Be("Nextwave");
        project.Code.Should().Be("2601-2607");
    }

    [Fact]
    public async Task Importar_asocia_SUB_BOX_con_NAP_de_paso_sin_duplicar_materiales()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        await service.ApplyAsync(await AnalyzeRealAsync(service), NewProject);

        await using var context = db.CreateDbContext();
        var materials = await context.Materials.ToListAsync();
        materials.Should().NotContain(m => m.Name.Contains("SUB", StringComparison.OrdinalIgnoreCase));
        materials.Count(m => m.Name == "NAP de paso").Should().Be(1);

        var nap = materials.Single(m => m.Name == "NAP de paso");
        var installed = await context.InventoryMovements
            .Where(m => m.MaterialId == nap.Id && m.Type == MovementType.Installed)
            .Select(m => m.Quantity).ToListAsync();
        installed.Sum().Should().Be(211);
    }

    [Fact]
    public async Task Importar_separa_aros_y_treboles_leyendo_el_comentario_de_la_celda()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        await service.ApplyAsync(await AnalyzeRealAsync(service), NewProject);

        await using var context = db.CreateDbContext();
        var site = await context.Sites.SingleAsync(s => s.Code == "SON19");
        var aro = await context.Materials.SingleAsync(m => m.Name == "Aro de sujeción");
        var trebol = await context.Materials.SingleAsync(m => m.Name == "Trébol");

        var moves = await context.InventoryMovements.Where(m => m.SiteId == site.Id && m.Type == MovementType.Installed).ToListAsync();
        moves.Where(m => m.MaterialId == aro.Id).Sum(m => m.Quantity).Should().Be(54);
        moves.Where(m => m.MaterialId == trebol.Id).Sum(m => m.Quantity).Should().Be(222);
    }

    [Fact]
    public async Task Importar_deja_el_feeder_global_y_los_estados_de_las_etapas()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        await service.ApplyAsync(await AnalyzeRealAsync(service), NewProject);

        await using var context = db.CreateDbContext();
        var lines = await context.FeederLines.Include(l => l.Entries).ToListAsync();
        lines.Where(l => l.Kind <= FeederItemKind.Cable96).Sum(l => l.PlannedQuantity).Should().Be(41000);
        lines.Single(l => l.Kind == FeederItemKind.Cable96).Entries.Sum(e => e.Quantity).Should().Be(7000);
        lines.Single(l => l.Kind == FeederItemKind.Odf96).PlannedQuantity.Should().Be(3);

        var son19 = await context.Sites.SingleAsync(s => s.Code == "SON19");
        son19.OdnFinalized.Should().BeTrue("el Excel lo marca FINALIZADO");
        var stages = await context.SiteStages.Where(s => s.SiteId == son19.Id).ToDictionaryAsync(s => s.Code, s => s.Status);
        stages[StageCode.Atp].Should().Be(StageStatus.Finished);
        stages[StageCode.FatMeasurements].Should().Be(StageStatus.Finished);
        stages[StageCode.FcCable].Should().Be(StageStatus.Finished);
        stages[StageCode.Layout].Should().Be(StageStatus.Pending, "el Excel no trae replanteo: queda pendiente");

        var son24 = await context.Sites.SingleAsync(s => s.Code == "SON24");
        son24.PlannedStart.Should().Be(new DateOnly(2026, 10, 17));
        var son11 = await context.Sites.SingleAsync(s => s.Code == "SON11");
        son11.PlannedStart.Should().BeNull("'POR DEFINIR' no es una fecha");
    }

    [Fact]
    public async Task Importar_dos_veces_no_duplica_nada()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        await service.ApplyAsync(await AnalyzeRealAsync(service), NewProject);

        await using (var context = db.CreateDbContext())
        {
            context.ChangeTracker.Clear();
        }

        var second = await service.ApplyAsync(await AnalyzeRealAsync(service), NewProject);

        second.Counts.SitesCreated.Should().Be(0);
        second.Counts.SitesUpdated.Should().Be(20);
        second.Counts.InstalledMovementsCreated.Should().Be(0);
        second.Counts.ProjectionVersionsCreated.Should().Be(0);
        second.Counts.ContractorsCreated.Should().Be(0);
        second.TotalsMatch.Should().BeTrue();

        await using var check = db.CreateDbContext();
        (await check.Sites.CountAsync()).Should().Be(20);
        (await check.Projects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Una_fila_con_texto_en_una_cantidad_no_se_importa_y_queda_en_el_reporte()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);

        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("AVANCE ODN");
        sheet.Cell(5, 4).Value = "NODO";
        sheet.Cell(5, 13).Value = "FO 50 M PROYECTADO";
        sheet.Cell(5, 14).Value = "FO 50 M INSTALADO";
        sheet.Cell(6, 4).Value = "SON01";
        sheet.Cell(6, 13).Value = 10;
        sheet.Cell(6, 14).Value = 4;
        sheet.Cell(7, 4).Value = "SON02";
        sheet.Cell(7, 13).Value = 10;
        sheet.Cell(7, 14).Value = "N/A";
        using var memory = new MemoryStream();
        book.SaveAs(memory);
        memory.Position = 0;

        var analysis = await service.AnalyzeAsync(memory, "mini.xlsx");

        analysis.Report.Errors.Should().Be(1);
        analysis.Report.Issues.Should().Contain(i => i.Severity == ImportSeverity.Error && i.Row == 7 && i.Message.Contains("SON02"));

        var report = await service.ApplyAsync(analysis, new ImportTarget(null, "T-1", "Prueba"));
        report.Counts.SitesCreated.Should().Be(1);
        report.Counts.RowsSkipped.Should().Be(1);

        await using var context = db.CreateDbContext();
        (await context.Sites.Select(s => s.Code).ToListAsync()).Should().Equal("SON01");
    }

    [Fact]
    public async Task Un_archivo_que_no_es_excel_se_reporta_como_error()
    {
        using var db = await TestDb.CreateAsync();
        var service = Service(db);
        using var stream = new MemoryStream("esto no es un excel"u8.ToArray());

        var analysis = await service.AnalyzeAsync(stream, "falso.xlsx");

        analysis.Report.Errors.Should().Be(1);
        analysis.Workbook.Nodes.Should().BeEmpty();
    }
}
