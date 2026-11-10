using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

/// <summary>Borrar con control: lo que se ingresó mal se quita, con motivo y sin destruir el historial.</summary>
public class DeletionTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required InventoryService Inventory { get; init; }
        public required QuantityService Quantities { get; init; }
        public required SiteService Sites { get; init; }
        public required ProjectService Projects { get; init; }
        public required DeletionService Deletion { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public required Material Cable { get; init; }
        public required Material Aletas { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var projects = new ProjectService(db);
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project { Code = "D-1", Name = "Borrado", ProjectTypeId = ftth.Id });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON01" });
        var materials = new MaterialService(db);
        var cable = (await materials.ResolveAsync("FO 100 M"))!;
        var aletas = await materials.SaveAsync(new Material { Name = "ALETAS PARA NAPS", Unit = "PAR" });
        var inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock);
        return new Env
        {
            Db = db, Inventory = inventory, Quantities = quantities, Sites = sites, Projects = projects,
            Deletion = new DeletionService(db, db.CurrentUser, db.Clock), Project = project, Site = site, Cable = cable, Aletas = aletas,
        };
    }

    [Fact]
    public async Task Quitar_un_material_del_nodo_borra_su_proyectado_y_anula_lo_entregado_e_instalado()
    {
        using var env = await SetupAsync();
        var projection = new ProjectionService(env.Db);
        await projection.AdjustAsync(env.Site.Id, env.Aletas.Id, 100, "Ingresado por error", env.Today);
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Aletas.Id, 50, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Aletas.Id, 20, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Aletas.Id, 5, env.Today, null, null);
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Aletas.Id)).Should().Be(30);

        var sinMotivo = () => env.Deletion.RemoveMaterialFromSiteAsync(env.Site.Id, env.Aletas.Id, " ");
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>().WithMessage("*motivo*");

        var touched = await env.Deletion.RemoveMaterialFromSiteAsync(env.Site.Id, env.Aletas.Id, "Se ingresó por error");
        touched.Should().BeGreaterThan(0);

        (await env.Quantities.GetSiteMaterialsAsync(env.Site.Id)).Should().NotContain(q => q.Material.Id == env.Aletas.Id);
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Aletas.Id)).Should().Be(50, "la entrega anulada vuelve a bodega");

        await using var ctx = env.Db.CreateDbContext();
        var voided = await ctx.InventoryMovements.Where(m => m.SiteId == env.Site.Id && m.MaterialId == env.Aletas.Id && m.IsVoided).ToListAsync();
        voided.Should().HaveCount(2).And.OnlyContain(m => m.VoidReason!.Contains("Se ingresó por error"));
    }

    [Fact]
    public async Task Quitar_un_material_que_no_esta_en_el_nodo_avisa()
    {
        using var env = await SetupAsync();
        var action = () => env.Deletion.RemoveMaterialFromSiteAsync(env.Site.Id, env.Aletas.Id, "x");
        await action.Should().ThrowAsync<BusinessRuleException>().WithMessage("*no tiene registros*");
    }

    [Fact]
    public async Task Un_material_sin_uso_se_borra_y_uno_con_uso_se_rechaza()
    {
        using var env = await SetupAsync();
        var materials = new MaterialService(env.Db);
        await new ProjectionService(env.Db).AdjustAsync(env.Site.Id, env.Aletas.Id, 10, "Prueba", env.Today);

        var enUso = () => env.Deletion.DeleteMaterialAsync(env.Aletas.Id);
        await enUso.Should().ThrowAsync<BusinessRuleException>().WithMessage("*todavía se usa*");

        await env.Deletion.RemoveMaterialFromSiteAsync(env.Site.Id, env.Aletas.Id, "Error");
        await env.Deletion.DeleteMaterialAsync(env.Aletas.Id);
        (await materials.ListAsync(includeInactive: true)).Should().NotContain(m => m.Id == env.Aletas.Id);
    }

    [Fact]
    public async Task Borrar_un_nodo_lo_oculta_y_anula_sus_movimientos_sin_tocar_los_demas()
    {
        using var env = await SetupAsync();
        var other = await env.Sites.SaveAsync(new Site { ProjectId = env.Project.Id, Code = "SON02" });
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Cable.Id, 40, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 10, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, other.Id, env.Cable.Id, 5, env.Today, null, null, null);

        var sinMotivo = () => env.Deletion.DeleteSiteAsync(env.Site.Id, "");
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>();
        var impact = await env.Deletion.SiteImpactAsync(env.Site.Id);
        impact.Movements.Should().Be(1);

        await env.Deletion.DeleteSiteAsync(env.Site.Id, "Nodo duplicado");

        (await env.Sites.ListAsync(env.Project.Id)).Select(s => s.Site.Code).Should().BeEquivalentTo(["SON02"]);
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Cable.Id)).Should().Be(35, "40 recibidos − 5 entregados a SON02");
        await using var ctx = env.Db.CreateDbContext();
        (await ctx.InventoryMovements.CountAsync(m => m.IsVoided && m.VoidReason!.Contains("Nodo duplicado"))).Should().Be(1);
        (await ctx.Sites.IgnoreQueryFilters().SingleAsync(s => s.Code == "SON01")).IsDeleted.Should().BeTrue("queda en la base para auditoría");
    }

    [Fact]
    public async Task Borrar_un_proyecto_lo_oculta_con_sus_nodos_y_anula_sus_movimientos()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Cable.Id, 40, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 10, env.Today, null, null, null);
        var impact = await env.Deletion.ProjectImpactAsync(env.Project.Id);
        (impact.Nodes, impact.Movements).Should().Be((1, 2));

        await env.Deletion.DeleteProjectAsync(env.Project.Id, "Proyecto de prueba");

        (await env.Projects.ListAsync()).Should().NotContain(p => p.Id == env.Project.Id);
        await using var ctx = env.Db.CreateDbContext();
        (await ctx.Sites.CountAsync(s => s.ProjectId == env.Project.Id)).Should().Be(0);
        (await ctx.InventoryMovements.Where(m => m.ProjectId == env.Project.Id).AllAsync(m => m.IsVoided)).Should().BeTrue();
    }

    [Fact]
    public async Task Unir_dos_materiales_pasa_todo_al_que_se_conserva_y_suma_el_proyectado()
    {
        using var env = await SetupAsync();
        var materials = new MaterialService(env.Db);
        var projection = new ProjectionService(env.Db);
        var keep = await materials.SaveAsync(new Material { Id = env.Aletas.Id, Name = env.Aletas.Name, Code = "NW-AC002", Unit = "PAR" });
        var dupe = await materials.SaveAsync(new Material { Name = "ALETAS DE NAPS", Unit = "UND" });

        var candidates = await materials.DuplicateCandidatesAsync();
        var pair = candidates.Should().ContainSingle(c => c.Keep.Id == keep.Id).Subject;
        pair.Remove.Id.Should().Be(dupe.Id);

        await env.Inventory.ReceiptAsync(env.Project.Id, keep.Id, 100, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, keep.Id, 68, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, dupe.Id, 68, env.Today, null, null);
        await projection.AdjustAsync(env.Site.Id, keep.Id, 50, "Diseño", env.Today);
        await projection.AdjustAsync(env.Site.Id, dupe.Id, 10, "Diseño", env.Today);

        await env.Deletion.MergeMaterialsAsync(dupe.Id, keep.Id);

        var row = (await env.Quantities.GetSiteMaterialsAsync(env.Site.Id)).Should().ContainSingle(q => q.Material.Id == keep.Id).Subject;
        (row.Delivered, row.Installed, row.Projected).Should().Be((68m, 68m, 60m));
        (await materials.ListAsync(includeInactive: true)).Should().NotContain(m => m.Id == dupe.Id);
        (await materials.ResolveAsync("ALETAS DE NAPS"))!.Id.Should().Be(keep.Id, "el nombre unido queda como alias");
        (await env.Inventory.AvailableAsync(env.Project.Id, keep.Id)).Should().Be(32);
    }

    [Fact]
    public async Task No_se_puede_unir_un_material_consigo_mismo()
    {
        using var env = await SetupAsync();
        var action = () => env.Deletion.MergeMaterialsAsync(env.Aletas.Id, env.Aletas.Id);
        await action.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Un_contratista_con_nodos_no_se_borra_y_uno_libre_si()
    {
        using var env = await SetupAsync();
        var reference = new ReferenceDataService(env.Db);
        var busy = await reference.SaveContractorAsync(new Contractor { Name = "Brigada con nodo" });
        var free = await reference.SaveContractorAsync(new Contractor { Name = "Brigada libre" });
        var site = await env.Sites.GetAsync(env.Site.Id);
        site!.ContractorId = busy.Id;
        await env.Sites.SaveAsync(site);

        var enUso = () => env.Deletion.DeleteContractorAsync(busy.Id);
        await enUso.Should().ThrowAsync<BusinessRuleException>().WithMessage("*desactívelo*");

        await env.Deletion.DeleteContractorAsync(free.Id);
        (await reference.ContractorsAsync(includeInactive: true)).Select(c => c.Name).Should().Contain("Brigada con nodo").And.NotContain("Brigada libre");
    }
}
