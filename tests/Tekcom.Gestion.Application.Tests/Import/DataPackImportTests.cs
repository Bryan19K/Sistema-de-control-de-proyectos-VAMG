using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Import;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Import;

/// <summary>Los importadores de bodega, gastos, preciario y NextWave con Excel pequeños armados aquí (sin datos reales).</summary>
public class DataPackImportTests
{
    private static DataImportCoordinator Coordinator(TestDb db) =>
        RealFilesTests.Coordinator(db);

    private static byte[] Save(XLWorkbook book)
    {
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task<Project> NewProjectAsync(TestDb db, string code = "2601-2607")
    {
        await using var ctx = db.CreateDbContext();
        var type = await ctx.ProjectTypes.FirstAsync(t => t.Code == "FTTH");
        var project = new Project { Code = code, Name = "Proyecto de prueba", ProjectTypeId = type.Id };
        ctx.Projects.Add(project);
        ctx.Sites.Add(new Site { Project = project, Code = "SON19" });
        await ctx.SaveChangesAsync();
        return project;
    }

    private static byte[] WarehouseBook(decimal fc200 = 3)
    {
        using var book = new XLWorkbook();
        var catalog = book.AddWorksheet("CATALOGO MAESTRO DE MATERIALES");
        catalog.Cell(4, 2).Value = "1RA ENTREGA  (ODN)";
        catalog.Cell(5, 2).Value = 1;
        catalog.Cell(5, 3).Value = "Fastconnect SC/APC 200 m";
        catalog.Cell(5, 4).Value = "CABLE FC 200 M";
        catalog.Cell(5, 6).Value = "E0SDC034";
        catalog.Cell(5, 7).Value = "U";
        catalog.Cell(5, 8).Value = 10;
        catalog.Cell(5, 11).Value = new DateTime(2026, 9, 19);
        catalog.Cell(6, 2).Value = 2;
        catalog.Cell(6, 3).Value = "SFP PON Transceiver 1490 nm";
        catalog.Cell(6, 4).Value = "SFP PON Transceiver";
        catalog.Cell(6, 5).Value = "SERIAL-001";
        catalog.Cell(6, 6).Value = "OSX002003";
        catalog.Cell(6, 7).Value = "U";
        catalog.Cell(6, 8).Value = 1;
        catalog.Cell(6, 11).Value = new DateTime(2026, 9, 19);

        var node = book.AddWorksheet("ENTREGA DE MATERIALES SON19");
        node.Cell(3, 2).Value = "BRIGADA RESPONSABLE:";
        node.Cell(3, 4).Value = "HENRY BERRIOS";
        node.Cell(4, 2).Value = "MATERIAL";
        node.Cell(5, 2).Value = "CABLE FC 200 METROS";
        node.Cell(5, 3).Value = "U";
        node.Cell(5, 4).Value = fc200;
        node.Cell(5, 5).Value = new DateTime(2026, 9, 26);
        node.Cell(5, 6).Value = "ALMACEN LOURDES";
        node.Cell(6, 2).Value = "VIÑETAS";
        node.Cell(6, 3).Value = "U";
        node.Cell(6, 4).Value = 20;
        return Save(book);
    }

    [Fact]
    public async Task Un_excel_cualquiera_no_se_reconoce_como_ninguno_de_los_formatos()
    {
        using var db = await TestDb.CreateAsync();
        using var book = new XLWorkbook();
        book.AddWorksheet("Hoja1").Cell(1, 1).Value = "hola";
        var packs = await Coordinator(db).AnalyzeAsync(Save(book), "cualquiera.xlsx");
        packs.Should().BeEmpty();
    }

    [Fact]
    public async Task Bodega_carga_ingresos_entregas_y_serial_con_los_nombres_del_catalogo_y_no_duplica()
    {
        using var db = await TestDb.CreateAsync();
        var project = await NewProjectAsync(db);
        var coordinator = Coordinator(db);
        var data = WarehouseBook();

        var packs = await coordinator.AnalyzeAsync(data, "bodega.xlsx");
        var warehouse = packs.Single();
        warehouse.Kind.Should().Be(PackKind.Warehouse);
        warehouse.Report.Errors.Should().Be(0);

        await coordinator.ApplyAsync(packs, new ImportTarget(project.Id, null, null));
        await using var ctx = db.CreateDbContext();
        var moves = await ctx.InventoryMovements.Include(m => m.Material).ToListAsync();
        moves.Where(m => m.Type == MovementType.Receipt).Sum(m => m.Quantity).Should().Be(11);
        var delivery = moves.Single(m => m.Type == MovementType.Delivery && m.Material!.Name.StartsWith("Cable FC"));
        delivery.Material!.Name.Should().Be("Cable FC SC/APC 200 m", "CABLE FC 200 METROS es el cable del catálogo, no un material nuevo");
        delivery.Quantity.Should().Be(3);
        delivery.SiteId.Should().NotBeNull();
        delivery.ContractorId.Should().NotBeNull();
        moves.Should().Contain(m => m.Material!.Name == "VIÑETAS" && m.Type == MovementType.Delivery);
        (await ctx.Transceivers.SingleAsync()).Serial.Should().Be("SERIAL-001");
        (await ctx.Materials.FirstAsync(m => m.Name == "Cable FC SC/APC 200 m")).Model.Should().Be("E0SDC034");

        var count = moves.Count;
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "bodega.xlsx"), new ImportTarget(project.Id, null, null));
        (await ctx.InventoryMovements.CountAsync()).Should().Be(count);
        (await ctx.Transceivers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Bodega_si_el_excel_trae_mas_cantidad_en_una_segunda_carga_agrega_solo_la_diferencia()
    {
        using var db = await TestDb.CreateAsync();
        var project = await NewProjectAsync(db);
        var coordinator = Coordinator(db);
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(WarehouseBook(3), "a.xlsx"), new ImportTarget(project.Id, null, null));
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(WarehouseBook(5), "b.xlsx"), new ImportTarget(project.Id, null, null));

        await using var ctx = db.CreateDbContext();
        var delivered = await ctx.InventoryMovements.Where(m => m.Type == MovementType.Delivery && m.Material!.Name == "Cable FC SC/APC 200 m").SumAsync(m => m.Quantity);
        delivered.Should().Be(5);
    }

    [Fact]
    public async Task Bodega_sin_proyecto_pide_elegir_uno()
    {
        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(WarehouseBook(), "bodega.xlsx");
        var act = () => coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        await act.Should().ThrowAsync<Tekcom.Gestion.Application.Common.BusinessRuleException>();
    }

    private static byte[] ExpensesBook()
    {
        using var book = new XLWorkbook();
        var purchases = book.AddWorksheet("GASTOS");
        purchases.Cell(8, 2).Value = "N";
        purchases.Cell(8, 3).Value = "FECHA";
        purchases.Cell(9, 2).Value = 1;
        purchases.Cell(9, 3).Value = new DateTime(2026, 9, 18);
        purchases.Cell(9, 4).Value = "Transporte de bodega";
        purchases.Cell(9, 5).Value = "TRANSPORTE";
        purchases.Cell(9, 6).Value = "Proveedor";
        purchases.Cell(9, 10).Value = 100m;
        purchases.Cell(9, 11).Value = 13m;
        purchases.Cell(9, 12).Value = 113m;
        purchases.Cell(10, 2).Value = "TOTALES";
        purchases.Cell(10, 10).Value = 100m;
        purchases.Cell(10, 11).Value = 13m;
        purchases.Cell(10, 12).Value = 113m;

        var petty = book.AddWorksheet("CONTROL DE CAJA CHICA -JUAN");
        petty.Cell(2, 9).Value = "ASIGNADO: $500";
        petty.Cell(4, 1).Value = "ITEM";
        petty.Cell(5, 1).Value = 1;
        petty.Cell(5, 2).Value = new DateTime(2026, 9, 19);
        petty.Cell(5, 4).Value = "DTE-4987";
        petty.Cell(5, 5).Value = "POLLO CAMPERO";
        petty.Cell(5, 6).Value = "DESAYUNO";
        petty.Cell(5, 8).Value = "2601-2607";
        petty.Cell(5, 9).Value = 33.9m;
        petty.Cell(6, 1).Value = 2;
        petty.Cell(6, 2).Value = new DateTime(2026, 9, 20);
        petty.Cell(6, 4).Value = "DTE-";
        petty.Cell(6, 6).Value = "GASOLINA";
        petty.Cell(6, 8).Value = ".-.";
        petty.Cell(6, 9).Value = 25m;
        petty.Cell(7, 8).Value = "TOTAL GASTADO";
        petty.Cell(7, 9).Value = 58.9m;
        petty.Cell(8, 8).Value = "MONTO ASIGNADO";
        petty.Cell(8, 9).Value = 500m;
        petty.Cell(9, 8).Value = "DISPONIBLE";
        petty.Cell(9, 9).Value = 441.1m;
        return Save(book);
    }

    [Fact]
    public async Task Gastos_cargan_compras_y_caja_chica_con_totales_validados_y_no_duplican()
    {
        using var db = await TestDb.CreateAsync();
        await NewProjectAsync(db, "2601-2607");
        var coordinator = Coordinator(db);
        var data = ExpensesBook();

        var packs = await coordinator.AnalyzeAsync(data, "gastos.xlsx");
        packs.Single().Kind.Should().Be(PackKind.Expenses);
        packs.Single().Report.TotalChecks.Should().OnlyContain(c => c.Matches);
        packs.Single().Report.Issues.Should().Contain(i => i.Message.Contains("sin número de factura"));

        var applied = await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));
        applied.Single().Report.TotalChecks.Should().OnlyContain(c => c.Matches);

        await using var ctx = db.CreateDbContext();
        (await ctx.ExpenseEntries.CountAsync()).Should().Be(3);
        var fund = await ctx.ExpenseFunds.SingleAsync();
        fund.AssignedAmount.Should().Be(500);
        var linked = await ctx.ExpenseEntries.SingleAsync(e => e.Description == "DESAYUNO");
        linked.ProjectId.Should().NotBeNull("el centro de costos 2601-2607 es el código del proyecto");
        (await ctx.ExpenseEntries.SingleAsync(e => e.Description == "GASOLINA")).CostCenter.Should().BeNull();

        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "gastos.xlsx"), new ImportTarget(null, null, null));
        (await ctx.ExpenseEntries.CountAsync()).Should().Be(3);
        (await ctx.ExpenseFunds.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Finanzas_resume_gasto_por_tipo_y_saldo_de_fondos()
    {
        using var db = await TestDb.CreateAsync();
        var coordinator = Coordinator(db);
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(ExpensesBook(), "gastos.xlsx"), new ImportTarget(null, null, null));
        var summary = await new FinanceService(db).SummaryAsync();

        summary.GrandTotal.Should().Be(113m + 58.9m);
        summary.PurchasesVat.Should().Be(13m);
        summary.Funds.Single().Balance.Should().Be(500m - 58.9m);
        summary.ByCategory.Select(c => c.Category).Should().Contain(["TRANSPORTE", "CAJA CHICA"]);
    }

    [Fact]
    public async Task Preciario_carga_precios_ordenes_y_codigos_erp_y_enlaza_el_nodo()
    {
        using var db = await TestDb.CreateAsync();
        var project = await NewProjectAsync(db);
        using var book = new XLWorkbook();
        var prices = book.AddWorksheet("PRECIARIO FINAL ");
        prices.Cell(2, 2).Value = "ODN";
        prices.Cell(3, 2).Value = "Item";
        prices.Cell(4, 2).Value = "XBOX"; prices.Cell(4, 3).Value = "AERIAL"; prices.Cell(4, 4).Value = "Pcs"; prices.Cell(4, 5).Value = 10.2m;
        prices.Cell(6, 2).Value = "FEEDER";
        prices.Cell(7, 2).Value = "TRADITIONAL CABLE 12F"; prices.Cell(7, 3).Value = "AERIAL"; prices.Cell(7, 4).Value = "Mtrs"; prices.Cell(7, 5).Value = 0.55m;
        var orders = book.AddWorksheet("SON_GENERAL");
        orders.Cell(5, 6).Value = "Monto total"; orders.Cell(5, 7).Value = 5677.572m;
        orders.Cell(7, 3).Value = "HP";
        orders.Cell(8, 2).Value = "SON19"; orders.Cell(8, 3).Value = 1271; orders.Cell(8, 4).Value = 5024.4m; orders.Cell(8, 5).Value = 653.172m;
        orders.Cell(8, 6).Value = 5677.572m; orders.Cell(8, 7).Value = "OC2026-0492"; orders.Cell(8, 8).Value = "HENRY BERRIOS";
        orders.Cell(8, 9).Value = "ENTREGADO"; orders.Cell(8, 11).Value = "PENDIENTE";
        var material = book.AddWorksheet("MATERIAL");
        material.Cell(1, 1).Value = "N"; material.Cell(1, 2).Value = "ITEMS ERP";
        material.Cell(2, 1).Value = 1; material.Cell(2, 2).Value = "NW-EQ001"; material.Cell(2, 3).Value = "Fastconnect terminal";
        material.Cell(2, 4).Value = "XBOX"; material.Cell(2, 5).Value = "FAT2808-TX-4"; material.Cell(2, 6).Value = "U"; material.Cell(2, 7).Value = "SI";

        var file = Save(book);
        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(file, "preciario.xlsx");
        packs.Single().Kind.Should().Be(PackKind.PriceList);
        await coordinator.ApplyAsync(packs, new ImportTarget(project.Id, null, null));

        await using var ctx = db.CreateDbContext();
        (await ctx.PriceItems.CountAsync(p => p.Section == "ODN")).Should().Be(1);
        (await ctx.PriceItems.CountAsync(p => p.Section == "FEEDER")).Should().Be(1);
        var order = await ctx.ContractOrders.SingleAsync();
        order.Total.Should().Be(5677.572m);
        order.SiteId.Should().NotBeNull();
        order.ContractorName.Should().Be("Henry Berrios");
        var xbox = await ctx.Materials.FirstAsync(m => m.Code == "NW-EQ001");
        xbox.Model.Should().Be("FAT2808-TX-4");
        xbox.HasSerial.Should().BeTrue();

        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(file, "preciario.xlsx"), new ImportTarget(project.Id, null, null));
        (await ctx.ContractOrders.CountAsync()).Should().Be(1);
        (await ctx.PriceItems.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task NextWave_control_crea_el_proyecto_con_centro_de_costos_y_el_control_dedicado_reemplaza_al_resumen()
    {
        using var db = await TestDb.CreateAsync();
        using var book = new XLWorkbook();
        var main = book.AddWorksheet("Base de Datos Principal");
        main.Cell(2, 3).Value = "Proyecto";
        main.Cell(3, 3).Value = "FLORENCIA"; main.Cell(3, 4).Value = "NEXTWAVE"; main.Cell(3, 5).Value = "JUAN LANDAVERDE";
        main.Cell(3, 6).Value = "HENRRY BERRIOS"; main.Cell(3, 7).Value = new DateTime(2026, 7, 7); main.Cell(3, 9).Value = 0.5m; main.Cell(3, 10).Value = "En proceso";
        var dash = book.AddWorksheet("Dashboard General");
        dash.Cell(10, 1).Value = "Proyecto"; dash.Cell(10, 2).Value = "CENTRO DE COSTOS"; dash.Cell(10, 6).Value = "Trabajos por Ejecutar";
        dash.Cell(11, 1).Value = "FLORENCIA"; dash.Cell(11, 2).Value = "2601-2604"; dash.Cell(11, 6).Value = "PENDIENTE CERTIFICACION";
        var units = book.AddWorksheet("Detalle por Edificio");
        units.Cell(3, 2).Value = "Proyecto (Edificio)"; units.Cell(3, 3).Value = "Nivel"; units.Cell(3, 4).Value = "Apartamento"; units.Cell(3, 5).Value = "Canalizacion";
        units.Cell(4, 1).Value = 1; units.Cell(4, 2).Value = "FLORENCIA"; units.Cell(4, 3).Value = 4; units.Cell(4, 4).Value = 401; units.Cell(4, 5).Value = "FINALIZADO";
        units.Cell(4, 20).Value = 0.8m; units.Cell(4, 21).Value = "🟠 En proceso";
        var detail = book.AddWorksheet("Detalle de Materiales");
        detail.Cell(2, 1).Value = "Proyecto";
        detail.Cell(3, 1).Value = "FLORENCIA"; detail.Cell(3, 4).Value = "FO 12H"; detail.Cell(3, 6).Value = "m";
        detail.Cell(3, 7).Value = 100; detail.Cell(3, 8).Value = 60; detail.Cell(3, 10).Value = 30;
        var data = Save(book);

        var coordinator = Coordinator(db);
        var packs = await coordinator.AnalyzeAsync(data, "control.xlsx");
        packs.Single().Kind.Should().Be(PackKind.NextWaveControl);
        await coordinator.ApplyAsync(packs, new ImportTarget(null, null, null));

        await using var ctx = db.CreateDbContext();
        var project = await ctx.Projects.Include(p => p.ProjectType).SingleAsync();
        project.Code.Should().Be("2601-2604");
        project.ProjectType!.Code.Should().Be("EDIFICIOS");
        project.ReportedProgress.Should().Be(50m);
        project.Status.Should().Be(ProjectStatus.Active);
        project.PendingWork.Should().Be("PENDIENTE CERTIFICACION");
        var unit = await ctx.BuildingRecords.SingleAsync(r => r.Kind == BuildingRecordKind.Unit);
        unit.Name.Should().Be("401");
        unit.Status.Should().Be("En proceso", "se quita el emoji del semáforo");
        unit.ProgressPercent.Should().Be(80m);
        (await ctx.InventoryMovements.Where(m => !m.IsVoided && m.Type == MovementType.Delivery).SumAsync(m => m.Quantity)).Should().Be(100);

        // El Excel dedicado de materiales del proyecto reemplaza al resumen del control.
        using var materials = new XLWorkbook();
        var sheet = materials.AddWorksheet("FLORENCIA");
        sheet.Cell(4, 2).Value = "Proyecto"; sheet.Cell(4, 3).Value = "Material"; sheet.Cell(4, 4).Value = "Tipo"; sheet.Cell(4, 5).Value = "Unidad";
        sheet.Cell(4, 6).Value = "Cant. Asignada"; sheet.Cell(4, 7).Value = "Cant. Instalada"; sheet.Cell(4, 9).Value = "Cant. devuelta por contratista";
        sheet.Cell(5, 2).Value = "FLORENCIA"; sheet.Cell(5, 3).Value = "FO 12H"; sheet.Cell(5, 5).Value = "m";
        sheet.Cell(5, 6).Value = 4000; sheet.Cell(5, 7).Value = 2244; sheet.Cell(5, 9).Value = 1762;
        var packs2 = await coordinator.AnalyzeAsync(Save(materials), "materiales.xlsx");
        packs2.Single().Kind.Should().Be(PackKind.NextWaveMaterials);
        await coordinator.ApplyAsync(packs2, new ImportTarget(null, null, null));
        (await ctx.InventoryMovements.Where(m => !m.IsVoided && m.Type == MovementType.Delivery).SumAsync(m => m.Quantity)).Should().Be(4000);

        // Recargar el control no pisa el control dedicado ni duplica el proyecto.
        await coordinator.ApplyAsync(await coordinator.AnalyzeAsync(data, "control.xlsx"), new ImportTarget(null, null, null));
        (await ctx.InventoryMovements.Where(m => !m.IsVoided && m.Type == MovementType.Delivery).SumAsync(m => m.Quantity)).Should().Be(4000);
        (await ctx.Projects.CountAsync()).Should().Be(1);
        (await ctx.BuildingRecords.CountAsync(r => r.Kind == BuildingRecordKind.Unit)).Should().Be(1);

        var rows = await new BuildingService(db).MaterialsAsync(project.Id);
        rows.Single().Should().Match<Application.Models.BuildingMaterialRow>(r => r.Assigned == 4000 && r.Installed == 2244 && r.Returned == 1762 && r.PendingReturn == -6);
    }
}
