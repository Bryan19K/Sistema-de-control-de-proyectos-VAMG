using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Reports;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Reports;

public class ReportTests
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
        public required ReportService Reports { get; init; }
        public required ReportExporter Exporter { get; init; }
        public required ExportTemplateService Templates { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var projects = new ProjectService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock);
        var liquidation = new LiquidationService(db, quantities, db.CurrentUser, db.Clock);
        var projection = new ProjectionService(db);
        var sprints = new SprintService(db, db.Clock);

        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project { Code = "R-1", Name = "FTTH Nextwave Sonsonate", ProjectTypeId = ftth.Id,
            PlannedStart = db.Clock.Today.AddDays(-10), PlannedEnd = db.Clock.Today.AddDays(20) });
        var contractor = await new ReferenceDataService(db).SaveContractorAsync(new Contractor { Name = "Brigada SON22" });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON22", ContractorId = contractor.Id });

        var nap = (await new MaterialService(db).ResolveAsync("NAP de paso"))!;
        await inventory.ReceiptAsync(project.Id, nap.Id, 160, db.Clock.Today, "Lote 1", null, null);
        await inventory.DeliveryAsync(project.Id, site.Id, nap.Id, 142, db.Clock.Today, null, "HE-001", null);
        await projection.AdjustAsync(site.Id, nap.Id, 150, "Proyección", db.Clock.Today);
        await inventory.InstalledAsync(project.Id, site.Id, nap.Id, 120, db.Clock.Today, null, null);
        await sprints.SaveAsync(new Sprint { ProjectId = project.Id, Name = "Sprint 1", StartDate = db.Clock.Today.AddDays(-5), EndDate = db.Clock.Today.AddDays(9) });

        var reports = new ReportService(projects, inventory, sites, sprints, liquidation, new LiquidationFormatService(db, projects, sites, quantities, new ConfigurationBuilder().Build(), new FakeEnvironment()), projection, new WorkPlanService(db, sites, db.Clock),
            new EquipmentService(db, db.Clock), new MinuteService(db, db.Clock), new DocumentService(db, new FakeFileStorage()),
            new MeasurementService(db), new FakeUserAdmin(), db.Clock);
        var exporter = new ReportExporter(new ConfigurationBuilder().Build(), new FakeEnvironment());
        return new Env { Db = db, Reports = reports, Exporter = exporter, Templates = new ExportTemplateService(db), Project = project, Site = site };
    }

    private static Task<ReportDocument> BuildAsync(Env env, string key, int? siteId = null) =>
        env.Reports.BuildAsync(new ReportRequest { Key = key, ProjectId = env.Project.Id, SiteId = siteId, PreparedBy = "Bryan Calderón" });

    [Fact]
    public async Task Todo_reporte_trae_el_encabezado_fijo_con_proyecto_fecha_de_corte_y_elaborado_por()
    {
        using var env = await SetupAsync();

        foreach (var definition in env.Reports.Definitions)
        {
            var document = await BuildAsync(env, definition.Key, definition.NeedsSite ? env.Site.Id : null);

            document.ProjectName.Should().Be("FTTH Nextwave Sonsonate");
            document.CutoffDate.Should().Be(env.Db.Clock.Today);
            document.PreparedBy.Should().Be("Bryan Calderón");
            document.Title.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task El_consolidado_de_materiales_calcula_el_stock_de_cada_material()
    {
        using var env = await SetupAsync();

        var document = await BuildAsync(env, "materiales");

        var row = document.Sections.Single().Rows.Single(r => (string?)r[0] == "NAP de paso");
        row[3].Should().Be(160m, "recibido");
        row[4].Should().Be(142m, "entregado");
        row[7].Should().Be(18m, "stock");
    }

    [Fact]
    public async Task La_fecha_de_corte_deja_fuera_los_movimientos_posteriores()
    {
        using var env = await SetupAsync();
        var document = await env.Reports.BuildAsync(new ReportRequest
        {
            Key = "materiales", ProjectId = env.Project.Id, PreparedBy = "x", CutoffDate = env.Db.Clock.Today.AddDays(-3),
        });

        document.Sections.Single().Rows.Should().NotContain(r => (string?)r[0] == "NAP de paso" && (decimal)r[3]! > 0);
    }

    [Fact]
    public async Task El_excel_trae_logo_o_marca_titulo_corte_elaborado_por_filtro_y_configuracion_de_impresion_carta()
    {
        using var env = await SetupAsync();
        var document = await BuildAsync(env, "materiales");
        var template = await env.Templates.GetAsync(null);

        var bytes = env.Exporter.ToExcel(document, template);

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        sheet.Cell(1, 1).GetString().Should().Be("TEK-COM");
        var text = string.Join(" | ", sheet.CellsUsed().Select(c => c.GetString()));
        text.Should().Contain("Consolidado de materiales").And.Contain("FTTH Nextwave Sonsonate")
            .And.Contain("Fecha de corte:").And.Contain("Elaborado por: Bryan Calderón");
        sheet.AutoFilter.IsEnabled.Should().BeTrue();
        sheet.PageSetup.PaperSize.Should().Be(XLPaperSize.LetterPaper);
        sheet.PageSetup.PageOrientation.Should().Be(XLPageOrientation.Landscape);
        sheet.PageSetup.PagesWide.Should().Be(1);
        sheet.PageSetup.PrintAreas.Should().NotBeEmpty();
    }

    [Fact]
    public async Task El_acta_de_liquidacion_lleva_firmas_y_los_numeros_del_nodo()
    {
        using var env = await SetupAsync();
        var document = await BuildAsync(env, "liquidacion", env.Site.Id);

        document.Signatures.Should().HaveCount(3);
        document.Sections[0].Columns.Select(c => c.Header).Should().Equal(
            "ITEMS", "NOMBRE DE MATERIAL", "GRUPO", "UM", "ENTREGADO POR ALMACEN", "UTILIZADO", "TOTAL A DEVOLVER", "DEVOLUCION", "DEBE", "OBSERVACION");
        var row = document.Sections[0].Rows.Single(r => (string?)r[1] == "NAP DE PASO");
        row[4].Should().Be(142m, "entregado por almacén");
        row[5].Should().Be(120m, "utilizado");
        row[6].Should().Be(22m, "total a devolver");
        row[8].Should().Be(22m, "debe");

        var bytes = env.Exporter.ToExcel(document, await env.Templates.GetAsync(null));
        using var book = new XLWorkbook(new MemoryStream(bytes));
        string.Join("|", book.Worksheet(1).CellsUsed().Select(c => c.GetString())).Should().Contain("Entregó (TEK-COM)").And.Contain("Brigada SON22");
    }

    [Fact]
    public async Task Una_plantilla_puede_ocultar_columnas_y_cambiar_el_color_del_encabezado()
    {
        using var env = await SetupAsync();
        var document = await BuildAsync(env, "materiales");
        var saved = await env.Templates.SaveAsync(new ExportTemplate
        {
            Name = "Sin proyectado", HeaderColor = "#aa0000", AccentColor = "#e8742c", Landscape = false,
            ZebraRows = false, HiddenColumns = "Projected,Returned", ReportKey = "materiales",
        });

        var bytes = env.Exporter.ToExcel(document, saved);

        using var book = new XLWorkbook(new MemoryStream(bytes));
        var sheet = book.Worksheet(1);
        var headers = sheet.CellsUsed().Select(c => c.GetString()).ToList();
        headers.Should().NotContain("Proyectado").And.NotContain("Devuelto").And.Contain("Stock");
        sheet.PageSetup.PageOrientation.Should().Be(XLPageOrientation.Portrait);
        var headerCell = sheet.CellsUsed().First(c => c.GetString() == "Stock");
        var color = headerCell.Style.Fill.BackgroundColor.Color;
        (color.R, color.G, color.B).Should().Be(((byte)0xAA, (byte)0, (byte)0), "usa el color de la plantilla");
    }

    [Fact]
    public async Task El_pdf_se_genera_con_contenido_valido_para_todos_los_reportes()
    {
        using var env = await SetupAsync();
        var template = await env.Templates.GetAsync(null);

        foreach (var definition in env.Reports.Definitions)
        {
            var document = await BuildAsync(env, definition.Key, definition.NeedsSite ? env.Site.Id : null);

            var pdf = env.Exporter.ToPdf(document, template);

            pdf.Length.Should().BeGreaterThan(1000, definition.Key);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", definition.Key);
        }
    }

    [Fact]
    public async Task Todo_excel_de_todos_los_reportes_abre_sin_errores()
    {
        using var env = await SetupAsync();
        var template = await env.Templates.GetAsync(null);

        foreach (var definition in env.Reports.Definitions)
        {
            var document = await BuildAsync(env, definition.Key, definition.NeedsSite ? env.Site.Id : null);
            var bytes = env.Exporter.ToExcel(document, template);

            var act = () => new XLWorkbook(new MemoryStream(bytes));
            act.Should().NotThrow(definition.Key);
        }
    }

    [Fact]
    public async Task La_plantilla_estandar_no_se_modifica_ni_se_elimina_y_los_colores_se_validan()
    {
        using var env = await SetupAsync();
        var standard = await env.Templates.GetAsync(null);

        var delete = () => env.Templates.DeleteAsync(standard.Id);
        await delete.Should().ThrowAsync<Tekcom.Gestion.Application.Common.BusinessRuleException>();

        var edit = () => env.Templates.SaveAsync(new ExportTemplate { Id = standard.Id, Name = "Otro", HeaderColor = "#000000", AccentColor = "#000000" });
        await edit.Should().ThrowAsync<Tekcom.Gestion.Application.Common.BusinessRuleException>();

        var badColor = () => env.Templates.SaveAsync(new ExportTemplate { Name = "Mala", HeaderColor = "rojo", AccentColor = "#000000" });
        await badColor.Should().ThrowAsync<Tekcom.Gestion.Application.Common.BusinessRuleException>().WithMessage("*#RRGGBB*");
    }

    [Fact]
    public async Task El_reporte_de_liquidacion_exige_elegir_un_nodo()
    {
        using var env = await SetupAsync();

        var act = () => BuildAsync(env, "liquidacion");

        await act.Should().ThrowAsync<Tekcom.Gestion.Application.Common.BusinessRuleException>().WithMessage("*nodo*");
    }
}
