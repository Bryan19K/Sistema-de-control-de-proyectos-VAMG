using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Import;

namespace Tekcom.Gestion.Application.Tests.Import;

/// <summary>El Excel de asignación de nodos (orden de construcción) armado aquí con la misma forma que el real.</summary>
public class NodeAssignmentImportTests
{
    private static byte[] AssignmentBook()
    {
        using var book = new XLWorkbook();
        var s = book.AddWorksheet("ASIG. NODOS SONSONATE");
        string[] heads = ["NODO", "ESTATUS DE PERMISOS ELECTRICOS", "BRIGADA", "SUPERVISOR ", "HOMEPASS", "HUB-BOX", "SUB BOX", "END BOX", "TOTAL DE FAT",
            "ESTATUS DE ENTREGA DE MATERIALES", "KMZ", "CONSOLIDADO DE UM", "REPORTE FOTOGRAFICO", "FECHA DE INCIO", "ESTADO ", "ATP"];
        for (var c = 0; c < heads.Length; c++) { s.Cell(4, c + 2).Value = heads[c]; }

        void Row(int r, string node, string brigade, string supervisor, int hp, int hub, int sub, int end, string delivered, string kmz, DateTime? start, string? state, string? atp)
        {
            s.Cell(r, 2).Value = node; s.Cell(r, 3).Value = "APROBADO"; s.Cell(r, 4).Value = brigade; s.Cell(r, 5).Value = supervisor;
            s.Cell(r, 6).Value = hp; s.Cell(r, 7).Value = hub; s.Cell(r, 8).Value = sub; s.Cell(r, 9).Value = end; s.Cell(r, 10).Value = hub + sub + end;
            s.Cell(r, 11).Value = delivered; s.Cell(r, 12).Value = kmz; s.Cell(r, 13).Value = kmz; s.Cell(r, 14).Value = kmz;
            if (start is not null) { s.Cell(r, 15).Value = start.Value; }
            if (state is not null) { s.Cell(r, 16).Value = state; }
            if (atp is not null) { s.Cell(r, 17).Value = atp; }
        }

        Row(5, "SON19", "HENRY", "VICTOR ", 1271, 6, 51, 14, "SI", "VALIDADA", new DateTime(2026, 9, 28), "FINALIZADO ", "SI ");
        Row(6, "SON20", "RUBIKITE", "CHINCO ", 846, 3, 34, 10, "SI", "", new DateTime(2026, 10, 19), null, null);
        Row(7, "SON07", "HENRY", "ORLANDO ", 912, 4, 36, 10, "NO", "", null, null, null);
        Row(8, "SON16", "RAYMUNDO", "CHINCO ", 1222, 6, 63, 21, "NO", "", null, null, null);
        Row(9, "SON23", "OPTICAL ", "CHINCO ", 545, 2, 21, 7, "SI", "PENDIENTE POR CONTRATISTA", new DateTime(2026, 10, 7), "FINALIZADO ", "SI ");
        s.Cell(10, 2).Value = "TOTAL"; s.Cell(10, 6).Value = 1271 + 846 + 912 + 1222 + 545;
        s.Cell(10, 7).Value = 6 + 3 + 4 + 6 + 2; s.Cell(10, 8).Value = 51 + 34 + 36 + 63 + 21; s.Cell(10, 9).Value = 14 + 10 + 10 + 21 + 7; s.Cell(10, 10).Value = 71 + 47 + 50 + 90 + 30;

        s.Cell(13, 2).Value = "SEGUIMIENTO DE ACTUALIZACIÓN DE DRIVE POR SUPERVISOR";
        s.Cell(14, 2).Value = "SUPERVISOR"; s.Cell(14, 3).Value = "TOTAL NODOS"; s.Cell(14, 4).Value = "NODOS RESPONSABLES"; s.Cell(14, 9).Value = "NODOS PENDIENTES DE ACTUALIZAR";
        s.Cell(15, 2).Value = "CHINCO"; s.Cell(15, 4).Value = "SON23, SON20, SON16"; s.Cell(15, 9).Value = "SON20, SON16";
        s.Cell(16, 2).Value = "VICTOR"; s.Cell(16, 4).Value = "SON19"; s.Cell(16, 9).Value = "";
        s.Cell(17, 2).Value = "TOTAL";

        var iza = book.AddWorksheet("ASIG. NODOS IZALCO ");
        for (var c = 0; c < heads.Length; c++) { iza.Cell(2, c + 2).Value = heads[c]; }
        iza.Cell(3, 2).Value = "IZA 01"; iza.Cell(3, 3).Value = "APROBADOS "; iza.Cell(3, 4).Value = "RUBIKITE"; iza.Cell(3, 5).Value = "PENDIENTE "; iza.Cell(3, 6).Value = 1323;
        iza.Cell(4, 2).Value = "IZA 02"; iza.Cell(4, 6).Value = 1099;
        iza.Cell(5, 6).Value = 2422;
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static async Task SeedAsync(TestDb db)
    {
        await using var ctx = db.CreateDbContext();
        var type = await ctx.ProjectTypes.FirstAsync(t => t.Code == "FTTH");
        var project = new Project { Code = "2601-2607", Name = "Construccion de FTTH - Sonsonate", ProjectTypeId = type.Id };
        ctx.Projects.Add(project);
        var henry = new Contractor { Name = "Henry Berrios" };
        var rubikite = new Contractor { Name = "Rubikite" };
        var raimundo = new Contractor { Name = "Alexander Raimundo" };
        ctx.Contractors.AddRange(henry, rubikite, raimundo);
        var victor = new Supervisor { Name = "Victor Merino" };
        var chinco = new Supervisor { Name = "Alexander Chinco" };
        var orlando = new Supervisor { Name = "Orlando Castellanos" };
        ctx.Supervisors.AddRange(victor, chinco, orlando);
        ctx.Sites.Add(new Site { Project = project, Code = "SON19", Zone = "SONSONATE", Contractor = henry, OdnFinalized = true });
        ctx.Sites.Add(new Site { Project = project, Code = "SON20", Zone = "SONSONATE", Contractor = rubikite, PlannedStart = new DateOnly(2026, 10, 20) });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Lee_las_hojas_de_cada_zona_y_comprueba_los_totales()
    {
        using var db = await TestDb.CreateAsync();
        var pack = await new NodeAssignmentImportService(db).AnalyzeAsync(AssignmentBook(), "asignacion.xlsx");

        pack.Should().NotBeNull();
        pack!.Kind.Should().Be(PackKind.NodeAssignment);
        pack.Report.NodeCodes.Should().BeEquivalentTo(["SON19", "SON20", "SON07", "SON16", "SON23", "IZA01", "IZA02"]);
        pack.Report.TotalChecks.Should().NotBeEmpty().And.OnlyContain(c => c.Matches);
        (await new NodeAssignmentImportService(db).AnalyzeAsync(new XLWorkbook().SaveToBytes(), "vacio.xlsx")).Should().BeNull();
    }

    [Fact]
    public async Task Agrega_nodos_y_proyecto_nuevos_completa_lo_vacio_y_no_pisa_lo_que_ya_hay()
    {
        using var db = await TestDb.CreateAsync();
        await SeedAsync(db);
        var importer = new NodeAssignmentImportService(db);
        var pack = (await importer.AnalyzeAsync(AssignmentBook(), "asignacion.xlsx"))!;

        var report = await importer.ApplyAsync(pack, null);

        await using var ctx = db.CreateDbContext();
        var sites = await ctx.Sites.Include(s => s.Contractor).Include(s => s.Supervisor).ToListAsync();

        var son07 = sites.Single(s => s.Code == "SON07");
        (son07.Contractor?.Name, son07.Supervisor?.Name, son07.HomesPassed, son07.MaterialsDelivered).Should().Be(("Henry Berrios", "Orlando Castellanos", 912, false));
        (await ctx.DesignProjectionLines.CountAsync(l => l.DesignProjection!.SiteId == son07.Id)).Should().Be(3, "hub-box, sub-box y end-box del Excel");
        (await ctx.SiteStages.CountAsync(st => st.SiteId == son07.Id)).Should().BeGreaterThan(0);

        sites.Single(s => s.Code == "SON16").Contractor?.Name.Should().Be("Alexander Raimundo", "RAYMUNDO es Raimundo mal escrito");

        var son20 = sites.Single(s => s.Code == "SON20");
        son20.PlannedStart.Should().Be(new DateOnly(2026, 10, 20), "la fecha del sistema no se pisa");
        son20.HomesPassed.Should().Be(846);
        son20.Supervisor?.Name.Should().Be("Alexander Chinco");
        son20.LogsUpToDate.Should().BeFalse("está en los pendientes de actualizar");

        var son19 = sites.Single(s => s.Code == "SON19");
        (son19.KmzStatus, son19.AtpDelivered, son19.LogsUpToDate, son19.ElectricalPermitStatus).Should().Be((ClosureDocStatus.Validated, true, true, "Aprobado"));

        var son23 = sites.Single(s => s.Code == "SON23");
        son23.Contractor.Should().BeNull("Optical ya no trabaja con TEK-COM");
        report.Issues.Should().Contain(i => i.Message.Contains("Optical"));
        report.Issues.Should().Contain(i => i.Message.Contains("SON20: inicio del Excel 19/10/2026, en el sistema 20/10/2026"));

        var izalco = await ctx.Projects.SingleAsync(p => p.Name.Contains("Izalco"));
        (await ctx.Sites.CountAsync(s => s.ProjectId == izalco.Id)).Should().Be(2);
        report.Counts.SitesCreated.Should().Be(5);
    }

    [Fact]
    public async Task Repetir_la_carga_no_duplica_nada()
    {
        using var db = await TestDb.CreateAsync();
        await SeedAsync(db);
        var importer = new NodeAssignmentImportService(db);
        await importer.ApplyAsync((await importer.AnalyzeAsync(AssignmentBook(), "a.xlsx"))!, null);
        var second = await importer.ApplyAsync((await importer.AnalyzeAsync(AssignmentBook(), "a.xlsx"))!, null);

        second.Counts.SitesCreated.Should().Be(0);
        second.Counts.SitesUpdated.Should().Be(0);
        await using var ctx = db.CreateDbContext();
        (await ctx.Sites.CountAsync()).Should().Be(7);
        (await ctx.Projects.CountAsync()).Should().Be(2);
        (await ctx.DesignProjections.CountAsync()).Should().Be(3, "una proyección por cada nodo nuevo con cajas (SON07, SON16 e IZA sin cajas no)");
    }
}

internal static class WorkbookExtensions
{
    public static byte[] SaveToBytes(this XLWorkbook book)
    {
        book.AddWorksheet("Hoja1");
        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }
}
