using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Reports;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Reports;

/// <summary>La liquidación sale en el formato oficial de la empresa, con las cantidades que ya registró el sistema.</summary>
public class LiquidationFormatTests
{
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required LiquidationFormatService Format { get; init; }
        public required InventoryService Inventory { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public required MaterialService Materials { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var projects = new ProjectService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project { Code = "L-1", Name = "Construccion de FTTH - Sonsonate", ProjectTypeId = ftth.Id });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON22" });
        return new Env
        {
            Db = db, Project = project, Site = site, Materials = new MaterialService(db),
            Inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock),
            Format = new LiquidationFormatService(db, projects, sites, quantities, new ConfigurationBuilder().Build(), new FakeEnvironment()),
        };
    }

    [Fact]
    public async Task Arma_la_hoja_del_nodo_con_entregado_utilizado_devolucion_y_grupo()
    {
        using var env = await SetupAsync();
        var fc200 = (await env.Materials.ResolveAsync("FO 200 M"))!;
        var cinchas = await env.Materials.SaveAsync(new Material { Name = "Cinchas plásticos negro pequeño", Code = "TSV-MA004", Unit = "UND", Category = MaterialCategory.Consumable });
        await env.Inventory.ReceiptAsync(env.Project.Id, fc200.Id, 20, env.Today, null, null, null);
        await env.Inventory.ReceiptAsync(env.Project.Id, cinchas.Id, 500, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, fc200.Id, 5, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, fc200.Id, 3, env.Today, null, null);
        await env.Inventory.ReturnAsync(env.Project.Id, env.Site.Id, fc200.Id, 1, env.Today, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, cinchas.Id, 200, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, cinchas.Id, 400, env.Today, null, null);

        var sheet = (await env.Format.SheetsAsync(env.Project.Id, env.Site.Id)).Should().ContainSingle().Subject;

        var cable = sheet.Lines.Single(l => l.Name == "CABLE FC 200 METROS");
        (cable.Group, cable.Unit, cable.Delivered, cable.Used, cable.Returned).Should().Be(("CL - NEXTWAVE", "Unidad", 5m, 3m, 1m));
        (cable.ToReturn, cable.Owed).Should().Be((2m, 1m));

        var cincha = sheet.Lines.Single(l => l.Code == "TSV-MA004");
        cincha.Group.Should().Be("TEKCOM");
        (cincha.ToReturn, cincha.Owed).Should().Be((-200m, -200m), "se utilizó más de lo entregado");
        sheet.Lines.First().Group.Should().Be("CL - NEXTWAVE", "lo del cliente va primero, como en el formato oficial");
    }

    [Fact]
    public async Task El_excel_oficial_tiene_una_hoja_por_nodo_con_las_columnas_y_formulas_del_formato()
    {
        using var env = await SetupAsync();
        var nap = (await env.Materials.ResolveAsync("NAP de paso"))!;
        await env.Inventory.ReceiptAsync(env.Project.Id, nap.Id, 100, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, nap.Id, 48, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, nap.Id, 48, env.Today, null, null);

        var bytes = env.Format.ToOfficialExcel(await env.Format.SheetsAsync(env.Project.Id, null));

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet("SON22");
        Enumerable.Range(1, 10).Select(c => sheet.Cell(5, c).GetString()).Should().Equal(
            "ITEMS", "NOMBRE DE MATERIAL", "GRUPO", "UM", "ENTREGADO POR ALMACEN", "UTILIZADO", "TOTAL A DEVOLVER", "DEVOLUCION", "DEBE", "OBSERVACION");
        sheet.Cell(6, 2).GetString().Should().Be("NAP DE PASO");
        sheet.Cell(6, 5).GetDouble().Should().Be(48);
        sheet.Cell(6, 6).GetDouble().Should().Be(48);
        sheet.Cell(6, 7).FormulaA1.Should().Be("E6-F6");
        sheet.Cell(6, 9).FormulaA1.Should().Be("G6-H6");
        sheet.Cell(6, 7).Value.GetNumber().Should().Be(0);
    }

    [Fact]
    public async Task Sin_materiales_avisa_y_los_nodos_sin_movimientos_no_salen_en_el_libro_completo()
    {
        using var env = await SetupAsync();
        (await env.Format.SheetsAsync(env.Project.Id, null)).Should().BeEmpty();
        var vacio = () => env.Format.ToOfficialExcel([]);
        vacio.Should().Throw<BusinessRuleException>();
    }
}
