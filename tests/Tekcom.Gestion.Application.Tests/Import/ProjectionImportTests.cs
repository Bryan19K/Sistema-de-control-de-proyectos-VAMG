using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Infrastructure.Import;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Import;

public class ProjectionImportTests
{
    private static async Task<(TestDb Db, ProjectionImportService Service, Project Project)> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await new ProjectService(db).SaveAsync(new Project { Code = "PR-1", Name = "Proyección", ProjectTypeId = ftth.Id });
        var sites = new SiteService(db, new QuantityService(db), db.Clock);
        await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON19" });
        await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON22" });
        return (db, new ProjectionImportService(db, db.Clock), project);
    }

    private static MemoryStream Book(params (string Node, string Material, object Quantity)[] rows)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Proyección");
        sheet.Cell(1, 1).Value = "NODO";
        sheet.Cell(1, 2).Value = "MATERIAL";
        sheet.Cell(1, 3).Value = "CANTIDAD";
        var r = 2;
        foreach (var (node, material, quantity) in rows)
        {
            sheet.Cell(r, 1).Value = node;
            sheet.Cell(r, 2).Value = material;
            sheet.Cell(r, 3).Value = XLCellValue.FromObject(quantity);
            r++;
        }

        var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Importa_resolviendo_alias_y_crea_una_version_por_nodo()
    {
        var (db, service, project) = await SetupAsync();
        using var _ = db;
        using var file = Book(("SON19", "SUB-BOX", 51), ("SON19", "Cable MPO 400 m", 1), ("SON22", "sub box", 48));

        var analysis = await service.AnalyzeAsync(project.Id, file, "diseño.xlsx");
        analysis.Report.Errors.Should().Be(0);
        var report = await service.ApplyAsync(analysis);

        report.Counts.ProjectionVersionsCreated.Should().Be(2);
        await using var context = db.CreateDbContext();
        var nap = await context.Materials.SingleAsync(m => m.Name == "NAP de paso");
        var projections = await context.DesignProjections.Include(p => p.Lines).ToListAsync();
        projections.SelectMany(p => p.Lines).Where(l => l.MaterialId == nap.Id).Sum(l => l.Quantity).Should().Be(99);
    }

    [Fact]
    public async Task Importar_otra_vez_lo_mismo_no_crea_version_y_un_cambio_crea_la_version_2()
    {
        var (db, service, project) = await SetupAsync();
        using var _ = db;
        var first = await service.AnalyzeAsync(project.Id, Book(("SON19", "NAP de paso", 51)), "v1.xlsx");
        await service.ApplyAsync(first);

        var same = await service.AnalyzeAsync(project.Id, Book(("SON19", "NAP de paso", 51)), "v1b.xlsx");
        (await service.ApplyAsync(same)).Counts.ProjectionVersionsCreated.Should().Be(0);

        var changed = await service.AnalyzeAsync(project.Id, Book(("SON19", "NAP de paso", 55)), "v2.xlsx");
        (await service.ApplyAsync(changed)).Counts.ProjectionVersionsCreated.Should().Be(1);

        var versions = await new ProjectionService(db).VersionsAsync((await new SiteService(db, new QuantityService(db), db.Clock).ListAsync(project.Id)).First(s => s.Site.Code == "SON19").Site.Id);
        versions.Select(v => v.Projection.Version).Should().Equal(2, 1);

        // La proyección vigente es la última versión.
        var siteId = versions[0].Projection.SiteId;
        (await new QuantityService(db).GetSiteMaterialsAsync(siteId)).Single().Projected.Should().Be(55);
    }

    [Fact]
    public async Task Las_filas_con_nodo_o_material_desconocido_no_se_importan_y_quedan_listadas()
    {
        var (db, service, project) = await SetupAsync();
        using var _ = db;
        using var file = Book(("SON19", "NAP de paso", 10), ("SON99", "NAP de paso", 5), ("SON19", "Material inventado", 3), ("SON22", "NAP final", "mucho"));

        var analysis = await service.AnalyzeAsync(project.Id, file, "malo.xlsx");

        analysis.Report.Errors.Should().Be(3);
        analysis.Rows.Should().ContainSingle();
        analysis.Report.Issues.Should().Contain(i => i.Message.Contains("SON99"));
        analysis.Report.Issues.Should().Contain(i => i.Message.Contains("Material inventado"));
    }

    [Fact]
    public async Task La_plantilla_generada_se_puede_leer_de_vuelta()
    {
        var (db, service, project) = await SetupAsync();
        using var _ = db;
        using var template = new MemoryStream(service.BuildTemplate());

        var analysis = await service.AnalyzeAsync(project.Id, template, "plantilla.xlsx");

        analysis.Report.Errors.Should().Be(0);
        analysis.Rows.Should().HaveCount(2);
    }
}
