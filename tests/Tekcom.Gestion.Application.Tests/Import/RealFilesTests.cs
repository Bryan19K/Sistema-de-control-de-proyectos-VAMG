using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;

using Tekcom.Gestion.Infrastructure.Import;
using Tekcom.Gestion.Infrastructure.Services;
using Xunit.Abstractions;

namespace Tekcom.Gestion.Application.Tests.Import;

/// <summary>
/// Pruebas con los Excel reales de TEK-COM. No viajan en el repositorio (datos del negocio): se leen de la carpeta
/// indicada en TEKCOM_REAL_XLS y, si no existe, las pruebas se omiten.
/// </summary>
public class RealFilesTests(ITestOutputHelper output)
{
    private static string? Dir => Environment.GetEnvironmentVariable("TEKCOM_REAL_XLS");

    private static byte[]? Read(string startsWith)
    {
        if (Dir is null || !Directory.Exists(Dir))
        {
            return null;
        }

        var file = Directory.GetFiles(Dir, "*.xlsx").FirstOrDefault(f => Path.GetFileName(f).Contains(startsWith, StringComparison.OrdinalIgnoreCase));
        return file is null ? null : File.ReadAllBytes(file);
    }

    internal static DataImportCoordinator Coordinator(TestDb db)
    {
        var ftth = new ExcelImportService(db, new QuantityService(db), db.Clock);
        return new DataImportCoordinator(ftth, new IDataPackImporter[] { new WarehouseImportService(db), new NextWaveControlImportService(db), new NextWaveMaterialsImportService(db), new ExpensesImportService(db), new PriceListImportService(db), new ContractorsImportService(db), new NodeAssignmentImportService(db) });
    }

    private void Print(ImportReport report)
    {
        foreach (var s in report.Summary) { output.WriteLine($"  {s.Key}: {s.Value}"); }
        foreach (var c in report.TotalChecks) { output.WriteLine($"  [{(c.Matches ? "OK" : "DIF")}] {c.Name}: excel {c.ExcelValue} sistema {c.SystemValue}"); }
        foreach (var i in report.Issues.Take(40)) { output.WriteLine($"  {i.Severity} {i.Sheet} r{i.Row} {i.Column}: {i.Message}"); }
    }

    [Fact]
    public async Task Ftth62_carga_nodos_y_bodega_completa_y_no_duplica_al_repetir()
    {
        var data = Read("FTTH_2026_62");
        if (data is null) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(data, "PROYECTO_FTTH_2026_62.xlsx");
        foreach (var p in packs) { output.WriteLine($"== {p.Title} (errores {p.Report.Errors}, avisos {p.Report.Warnings})"); Print(p.Report); }
        packs.Select(p => p.Kind).Should().Contain([PackKind.FtthControl, PackKind.Warehouse]);

        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(null, "2601-2607", "Construcción de FTTH - Sonsonate"));
        foreach (var p in applied) { output.WriteLine($"== APLICADO {p.Title}"); Print(p.Report); }
        applied.Single(p => p.Kind == PackKind.Warehouse).Report.TotalChecks.Should().OnlyContain(c => c.Matches);

        await using var ctx = db.CreateDbContext();
        var projectId = (await ctx.Projects.SingleAsync()).Id;
        var stock = await new InventoryService(db, new QuantityService(db), db.CurrentUser, db.Clock).StockAsync(projectId);
        foreach (var row in stock.Where(r => r.Stock < 0)) { output.WriteLine($"NEGATIVO {row.Material.Name}: {row.Stock}"); }
        output.WriteLine($"materiales con stock: {stock.Count}, total en bodega: {stock.Sum(r => r.Stock)}");

        var moves = await ctx.InventoryMovements.CountAsync();
        var transceivers = await ctx.Transceivers.CountAsync();
        var packs2 = await coordinator.AnalyzeAsync(data, "PROYECTO_FTTH_2026_62.xlsx");
        var again = await coordinator.ApplyAsync(packs2, new ImportTarget(projectId, null, null));
        (await ctx.InventoryMovements.CountAsync()).Should().Be(moves, "reimportar no debe duplicar bodega");
        (await ctx.Transceivers.CountAsync()).Should().Be(transceivers);
        again.Single(p => p.Kind == PackKind.Warehouse).Report.TotalChecks.Should().OnlyContain(c => c.Matches);
    }

    [Fact]
    public async Task NextWave_carga_cinco_proyectos_con_fases_edificios_y_materiales_sin_duplicar()
    {
        var control = Read("CONTROL_DE_PROYECTOS");
        var materials = Read("NEXT-WAVE");
        if (control is null || materials is null) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(control, "control.xlsx");
        foreach (var p in packs) { output.WriteLine($"== {p.Title} (errores {p.Report.Errors}, avisos {p.Report.Warnings})"); Print(p.Report); }
        packs.Should().ContainSingle(p => p.Kind == PackKind.NextWaveControl);

        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        foreach (var p in applied) { Print(p.Report); }

        await using var ctx = db.CreateDbContext();
        var projects = await ctx.Projects.Include(p => p.ProjectType).OrderBy(p => p.Code).ToListAsync();
        projects.Select(p => p.Code).Should().BeEquivalentTo(["2601-2602", "2601-2603", "2601-2604", "2601-2605", "2601-2606"]);
        projects.Should().OnlyContain(p => p.ProjectType!.Code == "EDIFICIOS");
        projects.Single(p => p.Name == "SAN BENITO 53").ReportedProgress.Should().Be(87.1m);
        (await ctx.ProjectPhases.CountAsync()).Should().BeGreaterThan(10);
        (await ctx.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Unit)).Should().BeGreaterThan(200);
        var movements = await ctx.InventoryMovements.CountAsync();
        movements.Should().BeGreaterThan(100);

        // El segundo archivo trae lo mismo: no debe duplicar.
        var packs2 = await coordinator.AnalyzeAsync(materials, "materiales.xlsx");
        foreach (var p in packs2) { output.WriteLine($"== {p.Title}"); Print(p.Report); }
        var applied2 = await coordinator.ApplyAsync(packs2, new ImportTarget(null, null, null));
        foreach (var p in applied2) { output.WriteLine("-- aplicado"); Print(p.Report); }
        var afterControl = await ctx.InventoryMovements.CountAsync(m => !m.IsVoided);
        // Reimportar el control no vuelve a cargar los materiales de los proyectos que ya tienen su control dedicado.
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(control, "control.xlsx"), new ImportTarget(null, null, null));
        (await ctx.InventoryMovements.CountAsync(m => !m.IsVoided)).Should().Be(afterControl);
        (await ctx.Projects.CountAsync()).Should().Be(5);
        var florencia = (await ctx.Projects.SingleAsync(p => p.Name == "FLORENCIA")).Id;
        var assigned = await ctx.InventoryMovements.Where(m => m.ProjectId == florencia && !m.IsVoided && m.Type == MovementType.Delivery && m.Material!.Name == "FO 12H").SumAsync(m => m.Quantity);
        assigned.Should().Be(4000);
        (await ctx.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Unit)).Should().BeLessThan(400);
    }

    [Fact]
    public async Task Gastos_cargan_compras_caja_chica_y_viaticos_con_totales_que_coinciden()
    {
        var data = Read("GASTOS");
        if (data is null) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(data, "gastos.xlsx");
        foreach (var p in packs) { output.WriteLine($"== {p.Title}"); Print(p.Report); }
        packs.Should().ContainSingle(p => p.Kind == PackKind.Expenses);
        packs[0].Report.TotalChecks.Should().OnlyContain(c => c.Matches);

        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        foreach (var p in applied) { output.WriteLine("-- aplicado"); Print(p.Report); }
        applied[0].Report.TotalChecks.Should().OnlyContain(c => c.Matches);

        await using var ctx = db.CreateDbContext();
        var count = await ctx.ExpenseEntries.CountAsync();
        count.Should().BeGreaterThan(80);
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "gastos.xlsx"), new ImportTarget(null, null, null));
        (await ctx.ExpenseEntries.CountAsync()).Should().Be(count);
        (await ctx.ExpenseFunds.CountAsync()).Should().BeGreaterThan(5);
    }

    [Fact]
    public async Task Preciario_carga_precios_ordenes_y_codigos_erp()
    {
        var ftth = Read("FTTH_2026_62");
        var price = Read("PRECIARIO");
        if (ftth is null || price is null) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(ftth, "ftth.xlsx"), new ImportTarget(null, "2601-2607", "Construcción de FTTH - Sonsonate"));
        var packs = await coordinator.AnalyzeAsync(price, "preciario.xlsx");
        foreach (var p in packs) { output.WriteLine($"== {p.Title}"); Print(p.Report); }
        packs.Should().ContainSingle(p => p.Kind == PackKind.PriceList);

        await using var ctx = db.CreateDbContext();
        var projectId = (await ctx.Projects.SingleAsync()).Id;
        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(projectId, null, null));
        foreach (var p in applied) { output.WriteLine("-- aplicado"); Print(p.Report); }
        applied[0].Report.TotalChecks.Should().OnlyContain(c => c.Matches);
        (await ctx.ContractOrders.CountAsync()).Should().Be(24);
        (await ctx.ContractOrders.CountAsync(o => o.SiteId != null)).Should().BeGreaterThan(10);
        (await ctx.PriceItems.CountAsync()).Should().BeGreaterThan(30);
        (await ctx.Materials.CountAsync(m => m.Code != null && m.Code.StartsWith("NW-"))).Should().BeGreaterThan(20);
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(price, "preciario.xlsx"), new ImportTarget(projectId, null, null));
        (await ctx.ContractOrders.CountAsync()).Should().Be(24);
    }

    [Fact]
    public async Task Contratistas_cargan_sin_optical_y_marcan_los_pendientes()
    {
        var ftth = Read("FTTH_2026_62");
        var data = Read("BASE_DE_DATOS_CONTRATISTAS");
        if (data is null) { return; }

        async Task<TestDb> CreateAsync() => await TestDb.CreateAsync();
        var tdb = await CreateAsync();
        var coordinator = Coordinator(tdb);
        if (ftth is not null)
        {
            await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(ftth, "ftth.xlsx"), new ImportTarget(null, "2601-2607", "FTTH"));
        }

        var packs = await coordinator.AnalyzeAsync(data, "contratistas.xlsx");
        foreach (var p in packs) { output.WriteLine($"== {p.Title}"); Print(p.Report); }
        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        foreach (var p in applied) { output.WriteLine("-- aplicado"); Print(p.Report); }

        await using var ctx = tdb.CreateDbContext();
        var contractors = await ctx.Contractors.ToListAsync();
        foreach (var c in contractors) { output.WriteLine($"{c.Name} | {c.Stage} | activo {c.IsActive} | {c.Nrc}"); }
        contractors.Where(c => c.Name.Contains("Optical", StringComparison.OrdinalIgnoreCase)).Should().OnlyContain(c => !c.IsActive);
        (await ctx.ContractorPeople.CountAsync()).Should().Be(54);
        (await ctx.ContractorPeople.Include(p => p.Contractor).AnyAsync(p => p.Contractor!.Name.Contains("Optical"))).Should().BeFalse();
        var before = await ctx.ContractorPeople.CountAsync();
        var beforeContractors = contractors.Count;
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "contratistas.xlsx"), new ImportTarget(null, null, null));
        (await ctx.ContractorPeople.CountAsync()).Should().Be(before);
        (await ctx.Contractors.CountAsync()).Should().Be(beforeContractors);
    }

    [Fact]
    public async Task La_verificacion_de_carga_confirma_que_los_seis_excel_reales_quedaron_cargados()
    {
        var files = new[] { "FTTH_2026_62", "CONTROL_DE_PROYECTOS", "NEXT-WAVE", "GASTOS", "PRECIARIO", "BASE_DE_DATOS_CONTRATISTAS" };
        var data = files.Select(Read).ToList();
        if (data.Any(d => d is null)) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var verification = new LoadVerificationService(db);
        (await verification.VerifyAsync()).AllLoaded.Should().BeFalse();

        int? projectId = null;
        for (var i = 0; i < files.Length; i++)
        {
            var packs = await coordinator.AnalyzeAsync(data[i]!, files[i]);
            var target = i == 0 ? new ImportTarget(null, "2601-2607", "Construcción de FTTH - Sonsonate") : new ImportTarget(projectId, null, null);
            await coordinator.ApplyAsync(packs, target);
            if (i == 0)
            {
                await using var ctx = db.CreateDbContext();
                projectId = (await ctx.Projects.SingleAsync()).Id;
            }
        }

        var result = await verification.VerifyAsync();
        foreach (var check in result.Files.SelectMany(f => f.Checks)) { output.WriteLine($"[{check.State}] {check.File} · {check.Name}: Excel {check.Expected} / sistema {check.Actual}"); }
        result.AllLoaded.Should().BeTrue(string.Join(", ", result.Missing));
        result.Differences.Should().Be(0, "recién cargados, todas las cifras coinciden con los Excel");
    }

    [Fact]
    public async Task Asignacion_de_nodos_real_agrega_los_nodos_que_faltan_y_el_proyecto_de_izalco()
    {
        var data = Read("ASIGNACION_DE_NODOS");
        if (data is null) { return; }

        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(data, "ASIGNACION_DE_NODOS_SONSONATE_4.xlsx");
        var pack = packs.Should().ContainSingle(p => p.Kind == PackKind.NodeAssignment).Subject;
        Print(pack.Report);
        pack.Report.TotalChecks.Should().OnlyContain(c => c.Matches, "las sumas de los nodos coinciden con la fila TOTAL del Excel");

        var done = await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        Print(done.Single().Report);

        await using var ctx = db.CreateDbContext();
        (await ctx.Sites.CountAsync(s => s.Zone == "SONSONATE")).Should().Be(24);
        (await ctx.Sites.CountAsync(s => s.Zone == "IZALCO")).Should().Be(5);
        (await ctx.Sites.Where(s => s.Zone == "SONSONATE").SumAsync(s => s.HomesPassed ?? 0)).Should().Be(26031);
        (await ctx.Projects.CountAsync(p => p.Name.Contains("Izalco"))).Should().Be(1);

        // Repetir no duplica ni cambia nada.
        var again = await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "ASIGNACION_DE_NODOS_SONSONATE_4.xlsx"), new ImportTarget(null, null, null));
        again.Single().Report.Counts.SitesCreated.Should().Be(0);
        (await ctx.Sites.CountAsync()).Should().Be(29);
    }
}
