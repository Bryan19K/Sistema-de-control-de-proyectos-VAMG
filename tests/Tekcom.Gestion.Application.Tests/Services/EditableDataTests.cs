using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

/// <summary>Lo que el administrador puede corregir desde el sistema: instalado, contratistas, gastos, órdenes y la plantilla del plan.</summary>
public class EditableDataTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required InventoryService Inventory { get; init; }
        public required SiteService Sites { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public required Material Cable { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await new ProjectService(db).SaveAsync(new Project { Code = "E-1", Name = "Edición", ProjectTypeId = ftth.Id });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON01" });
        var cable = (await new MaterialService(db).ResolveAsync("FO 100 M"))!;
        await new ProjectionService(db).AdjustAsync(site.Id, cable.Id, 10, "Diseño", db.Clock.Today);
        return new Env { Db = db, Inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock), Sites = sites, Project = project, Site = site, Cable = cable };
    }

    private static async Task<decimal> InstalledAsync(Env env) =>
        await env.Db.CreateDbContext().InventoryMovements
            .Where(m => m.SiteId == env.Site.Id && m.Type == MovementType.Installed && !m.IsVoided).SumAsync(m => m.Quantity);

    [Fact]
    public async Task Subir_el_total_instalado_registra_la_diferencia_y_se_refleja_en_el_avance_global_del_nodo()
    {
        using var env = await SetupAsync();
        (await env.Sites.GetSummaryAsync(env.Site.Id))!.GlobalPercent.Should().Be(0);

        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 4, env.Today, null);
        (await InstalledAsync(env)).Should().Be(4);
        (await env.Sites.GetSummaryAsync(env.Site.Id))!.GlobalPercent.Should().Be(40m);

        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 10, env.Today, null);
        (await InstalledAsync(env)).Should().Be(10);
        (await env.Sites.GetSummaryAsync(env.Site.Id))!.GlobalPercent.Should().Be(100m);
    }

    [Fact]
    public async Task Bajar_el_instalado_pide_motivo_y_anula_sin_borrar()
    {
        using var env = await SetupAsync();
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 6, env.Today.AddDays(-2), null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 3, env.Today, null, null);

        var sinMotivo = () => env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 5, env.Today, null);
        await sinMotivo.Should().ThrowAsync<BusinessRuleException>().WithMessage("*motivo*");

        // 9 -> 5: se anula el último (3) y se recorta el anterior (6 -> 2)
        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 5, env.Today, "Se contó mal");
        (await InstalledAsync(env)).Should().Be(5);
        await using var ctx = env.Db.CreateDbContext();
        (await ctx.InventoryMovements.CountAsync(m => m.IsVoided && m.VoidReason!.Contains("Se contó mal"))).Should().BeGreaterThan(0);
        (await ctx.InventoryMovements.CountAsync(m => m.SiteId == env.Site.Id && m.Type == MovementType.Installed)).Should().BeGreaterThan(2, "nada se borra");
    }

    [Fact]
    public async Task El_avance_por_piezas_no_se_pasa_de_lo_proyectado()
    {
        using var env = await SetupAsync();
        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 14, env.Today, null);
        (await env.Sites.GetSummaryAsync(env.Site.Id))!.GlobalPercent.Should().Be(100m);
    }

    [Fact]
    public async Task El_directorio_de_contratistas_lista_los_pendientes_de_poliza_o_afiliacion()
    {
        using var db = await TestDb.CreateAsync();
        var directory = new ContractorDirectoryService(db, db.Clock);
        var contractor = await directory.SaveAsync(new Contractor { Name = "Henry Berrios", IsActive = true });
        await directory.SavePersonAsync(new ContractorPerson { ContractorId = contractor.Id, FullName = "Ana", PolicyNumber = "104-1", InsuranceStatus = "Vigente" });
        await directory.SavePersonAsync(new ContractorPerson { ContractorId = contractor.Id, FullName = "Beto", InsuranceStatus = "PENDIENTE" });
        await directory.SavePersonAsync(new ContractorPerson { ContractorId = contractor.Id, FullName = "Carla", PolicyNumber = "9", InsuranceStatus = "Vigencia hasta 01/10/2026" });

        var pending = await directory.PeopleAsync(onlyNeedingAttention: true);
        pending.Select(p => p.Person.FullName).Should().BeEquivalentTo(["Beto", "Carla"]);
        pending.Single(p => p.Person.FullName == "Carla").State.Should().Be(CoverageState.Expired);
        var view = (await directory.ListAsync()).Single();
        view.People.Should().Be(3);
        view.Covered.Should().Be(1);

        var duplicate = () => directory.SaveAsync(new Contractor { Name = "HENRY  BERRIOS" });
        await duplicate.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Los_gastos_se_pueden_agregar_corregir_y_quitar_con_iva_calculado()
    {
        using var db = await TestDb.CreateAsync();
        var finance = new FinanceService(db);
        var entry = await finance.SaveExpenseAsync(new ExpenseEntry
        {
            Kind = ExpenseKind.Purchase, Date = new DateOnly(2026, 11, 3), Description = "Estantes", Category = "mobiliario", Subtotal = 100m,
        });
        entry.Vat.Should().Be(13m);
        entry.Total.Should().Be(113m);
        entry.Category.Should().Be("MOBILIARIO");

        entry.Subtotal = 200m;
        entry.Vat = null;
        var edited = await finance.SaveExpenseAsync(entry);
        edited.Total.Should().Be(226m);
        (await finance.SummaryAsync()).PurchasesTotal.Should().Be(226m);

        await finance.DeleteExpenseAsync(edited.Id);
        (await finance.SummaryAsync()).Entries.Should().Be(0);

        var invalid = () => finance.SaveExpenseAsync(new ExpenseEntry { Kind = ExpenseKind.PettyCash, Date = new DateOnly(2026, 11, 3), Description = " ", Total = 5 });
        await invalid.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Un_fondo_con_gastos_no_se_puede_quitar()
    {
        using var db = await TestDb.CreateAsync();
        var finance = new FinanceService(db);
        var fund = await finance.SaveFundAsync(new ExpenseFund { Name = "Caja chica", Kind = ExpenseKind.PettyCash, AssignedAmount = 500 });
        await finance.SaveExpenseAsync(new ExpenseEntry { Kind = ExpenseKind.PettyCash, FundId = fund.Id, Date = new DateOnly(2026, 11, 3), Description = "Gasolina", Total = 25 });

        var act = () => finance.DeleteFundAsync(fund.Id);
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*tiene gastos*");
    }

    [Fact]
    public async Task Una_orden_calcula_su_total_y_se_enlaza_al_nodo_por_el_codigo()
    {
        using var env = await SetupAsync();
        var finance = new FinanceService(env.Db);
        var order = await finance.SaveOrderAsync(new ContractOrder { ProjectId = env.Project.Id, Code = "son01", Subtotal = 1000m, Vat = 130m });
        order.Total.Should().Be(1130m);
        order.Code.Should().Be("SON01");
        order.SiteId.Should().Be(env.Site.Id);

        var duplicate = () => finance.SaveOrderAsync(new ContractOrder { ProjectId = env.Project.Id, Code = "SON01" });
        await duplicate.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task La_plantilla_completa_del_plan_trae_feeder_odn_fusiones_y_una_actividad_por_nodo()
    {
        using var db = await TestDb.CreateAsync();
        await using var ctx = db.CreateDbContext();
        var template = await ctx.PlanTemplates.SingleAsync(t => t.Name == PlanTemplateDefinition.FtthCompleteName);
        template.IsSystem.Should().BeTrue();

        var definition = PlanTemplateDefinition.FromJson(template.DefinitionJson);
        var names = definition.Phases.SelectMany(p => p.Activities.Select(a => a.Name)).ToList();
        names.Should().Contain(["6.1 Instalación del ODF", "7.1 Tendido de cable de 96 hilos", "10.1 Montaje del X-box", "Fusiones en X-box", "Fusiones en NAP final", "11.4 Informe de certificación con mediciones"]);
        definition.Phases.Select(p => p.Name).Should().Contain(["ODN por nodo", "10b. ODN – Fusiones"]);
        definition.Phases.SelectMany(p => p.Activities).Should().Contain(a => a.PerSite && a.Name.Contains("{nodo}"));
        definition.Phases.SelectMany(p => p.Activities).Should().Contain(a => a.Milestone);
        (await ctx.PlanTemplates.CountAsync(t => t.IsSystem)).Should().BeGreaterThanOrEqualTo(3, "las plantillas estándar siguen existiendo");
    }

    [Fact]
    public async Task La_verificacion_de_carga_sin_datos_dice_que_faltan_los_seis_excel()
    {
        using var db = await TestDb.CreateAsync();
        var result = await new LoadVerificationService(db).VerifyAsync();

        result.Files.Should().HaveCount(6);
        result.AllLoaded.Should().BeFalse();
        result.Missing.Should().HaveCount(6);
        result.Files.SelectMany(f => f.Checks).Should().Contain(c => c.State == Application.Models.LoadCheckState.Missing);
    }

    [Fact]
    public async Task Editar_proyectado_entregado_y_un_movimiento_se_refleja_y_respeta_el_stock()
    {
        using var env = await SetupAsync();
        var projection = new ProjectionService(env.Db);
        await projection.SetProjectedTotalAsync(env.Site.Id, env.Cable.Id, 25, "Venía mal del diseño", env.Today);
        (await new QuantityService(env.Db).GetSiteMaterialsAsync(env.Site.Id)).Single(m => m.Material.Id == env.Cable.Id).Projected.Should().Be(25);

        await env.Inventory.ReceiptAsync(env.Project.Id, env.Cable.Id, 20, env.Today, null, null, null);
        await env.Inventory.SetDeliveredTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 12, env.Today, null);
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Cable.Id)).Should().Be(8);
        await env.Inventory.SetDeliveredTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 5, env.Today, "Se entregó de menos");
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Cable.Id)).Should().Be(15);

        var receipt = (await env.Inventory.MovementsAsync(new Tekcom.Gestion.Application.Models.MovementFilter { ProjectId = env.Project.Id, Type = MovementType.Receipt })).Single();
        var tooLow = () => env.Inventory.UpdateMovementAsync(receipt.Id, 2, env.Today, null, null, null, null);
        await tooLow.Should().ThrowAsync<BusinessRuleException>().WithMessage("*negativo*");
        var edited = await env.Inventory.UpdateMovementAsync(receipt.Id, 30, env.Today.AddDays(-1), null, "Factura 55", "Lote B", "Corregido");
        edited.Quantity.Should().Be(30);
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Cable.Id)).Should().Be(25);
    }

    [Fact]
    public async Task Las_fechas_de_una_etapa_se_editan_y_se_validan()
    {
        using var env = await SetupAsync();
        await env.Sites.SetStageDatesAsync(env.Site.Id, StageCode.FcCable, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4));
        var stage = (await env.Sites.StagesAsync(env.Site.Id)).Single(s => s.Code == StageCode.FcCable);
        stage.ActualEnd.Should().Be(new DateOnly(2026, 11, 4));

        var invalid = () => env.Sites.SetStageDatesAsync(env.Site.Id, StageCode.FcCable, null, null, new DateOnly(2026, 11, 5), new DateOnly(2026, 11, 1));
        await invalid.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Los_clientes_se_editan_sin_repetir_y_no_se_quitan_si_tienen_proyectos()
    {
        using var db = await TestDb.CreateAsync();
        var reference = new ReferenceDataService(db);
        var client = await reference.SaveClientAsync(0, "NextWave");
        await reference.SaveClientAsync(client.Id, "NEXTWAVE S.A.");
        (await reference.ClientsAsync()).Single().Name.Should().Be("NEXTWAVE S.A.");

        await reference.SaveClientAsync(0, "Otro");
        var duplicate = () => reference.SaveClientAsync(client.Id, "otro");
        await duplicate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Ya existe*");

        await using var ctx = db.CreateDbContext();
        var ftth = await ctx.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        await new ProjectService(db).SaveAsync(new Project { Code = "C-1", Name = "Con cliente", ProjectTypeId = ftth.Id, ClientId = client.Id });
        var inUse = () => reference.DeleteClientAsync(client.Id);
        await inUse.Should().ThrowAsync<BusinessRuleException>().WithMessage("*proyecto*");
    }

    [Fact]
    public async Task El_ritmo_del_nodo_usa_el_avance_por_piezas_instaladas()
    {
        using var env = await SetupAsync();
        var site = (await env.Sites.GetAsync(env.Site.Id))!;
        site.PlannedStart = env.Today.AddDays(-20);
        site.PlannedEnd = env.Today.AddDays(-1); // el tiempo ya terminó
        await env.Sites.SaveAsync(site);

        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 3, env.Today, null);
        var behind = (await env.Sites.GetSummaryAsync(env.Site.Id))!;
        behind.GlobalPercent.Should().Be(30m);
        behind.Pace.Status.Should().Be(Domain.Enums.PaceStatus.Behind);

        await env.Inventory.SetInstalledTotalAsync(env.Project.Id, env.Site.Id, env.Cable.Id, 10, env.Today, null);
        (await env.Sites.GetSummaryAsync(env.Site.Id))!.Pace.Status.Should().Be(Domain.Enums.PaceStatus.Completed,
            "con todas las piezas instaladas el nodo está completo aunque sus etapas no estén marcadas");
    }
}
