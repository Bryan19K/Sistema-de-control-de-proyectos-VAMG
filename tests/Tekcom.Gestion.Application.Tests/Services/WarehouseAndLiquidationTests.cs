using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class WarehouseAndLiquidationTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required InventoryService Inventory { get; init; }
        public required LiquidationService Liquidation { get; init; }
        public required ProjectionService Projection { get; init; }
        public required MeasurementService Measurements { get; init; }
        public required Project Project { get; init; }
        public required Site Site { get; init; }
        public required Material Nap { get; init; }
        public required Material Aro { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var projects = new ProjectService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var materials = new MaterialService(db);

        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project { Code = "W-1", Name = "Bodega", ProjectTypeId = ftth.Id });
        var contractor = await new ReferenceDataService(db).SaveContractorAsync(new Contractor { Name = "Brigada SON22" });
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON22", ContractorId = contractor.Id });

        return new Env
        {
            Db = db,
            Inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock),
            Liquidation = new LiquidationService(db, quantities, db.CurrentUser, db.Clock),
            Projection = new ProjectionService(db),
            Measurements = new MeasurementService(db),
            Project = project,
            Site = site,
            Nap = (await materials.ResolveAsync("NAP de paso"))!,
            Aro = (await materials.ResolveAsync("Aro de sujeción"))!,
        };
    }

    [Fact]
    public async Task El_stock_se_calcula_con_ingresos_entregas_devoluciones_y_ajustes()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 160, env.Today, "Lote 1", null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 142, env.Today, null, null, null);
        await env.Inventory.ReturnAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 2, env.Today, null, null);
        await env.Inventory.AdjustmentAsync(env.Project.Id, env.Nap.Id, 1, increase: false, env.Today, "Merma en bodega");

        var row = (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id);

        row.Received.Should().Be(160);
        row.Delivered.Should().Be(142);
        row.Stock.Should().Be(160 - 142 + 2 - 1);
    }

    [Fact]
    public async Task Una_entrega_mayor_al_stock_se_rechaza_mostrando_lo_disponible()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 10, env.Today, null, null, null);

        var act = () => env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 11, env.Today, null, null, null);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*hay 10 disponibles*");
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Nap.Id)).Should().Be(10, "no se guardó nada");
    }

    [Fact]
    public async Task Con_justificacion_explicita_se_permite_la_entrega_sin_stock_y_queda_registrada()
    {
        using var env = await SetupAsync();

        var movement = await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 3, env.Today, null, null, null,
            negativeStockJustification: "Material prestado por otro proyecto");

        movement.NegativeStockJustification.Should().Be("Material prestado por otro proyecto");
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Nap.Id)).Should().Be(-3);
        (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id).Status.Should().Be(StockStatus.Negative);
    }

    [Fact]
    public async Task Los_movimientos_anulados_no_cuentan_y_exigen_motivo()
    {
        using var env = await SetupAsync();
        var receipt = await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 50, env.Today, null, null, null);
        var delivery = await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 20, env.Today, null, null, null);

        var noReason = () => env.Inventory.VoidAsync(delivery.Id, " ");
        await noReason.Should().ThrowAsync<BusinessRuleException>();

        await env.Inventory.VoidAsync(delivery.Id, "Se entregó al nodo equivocado");
        (await env.Inventory.AvailableAsync(env.Project.Id, env.Nap.Id)).Should().Be(50);

        var twice = () => env.Inventory.VoidAsync(delivery.Id, "otra vez");
        await twice.Should().ThrowAsync<BusinessRuleException>();

        var movements = await env.Inventory.MovementsAsync(new Application.Models.MovementFilter { ProjectId = env.Project.Id });
        var voided = movements.Single(m => m.Id == delivery.Id);
        voided.IsVoided.Should().BeTrue();
        voided.VoidReason.Should().Be("Se entregó al nodo equivocado");
        voided.VoidedBy.Should().Be("bryan@tekcom.com");
        receipt.IsVoided.Should().BeFalse();
    }

    [Fact]
    public async Task No_se_puede_anular_un_ingreso_si_el_stock_quedaria_negativo()
    {
        using var env = await SetupAsync();
        var receipt = await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 10, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 8, env.Today, null, null, null);

        var act = () => env.Inventory.VoidAsync(receipt.Id, "Error de captura");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*negativo*");
    }

    [Fact]
    public async Task Un_ajuste_exige_motivo_y_no_puede_dejar_el_stock_negativo()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 5, env.Today, null, null, null);

        var noReason = () => env.Inventory.AdjustmentAsync(env.Project.Id, env.Nap.Id, 1, true, env.Today, "");
        await noReason.Should().ThrowAsync<BusinessRuleException>();

        var tooMuch = () => env.Inventory.AdjustmentAsync(env.Project.Id, env.Nap.Id, 6, false, env.Today, "Conteo físico");
        await tooMuch.Should().ThrowAsync<BusinessRuleException>().WithMessage("*negativo*");
    }

    [Fact]
    public async Task No_se_puede_devolver_mas_de_lo_entregado_a_un_nodo()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 20, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 5, env.Today, null, null, null);

        var act = () => env.Inventory.ReturnAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 6, env.Today, null, null);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*5*");
    }

    [Fact]
    public async Task Los_faltantes_salen_de_comparar_lo_proyectado_contra_lo_recibido_y_el_stock_bajo_avisa()
    {
        using var env = await SetupAsync();
        await env.Projection.AdjustAsync(env.Site.Id, env.Nap.Id, 9, "Proyección inicial", env.Today);
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 6, env.Today, null, null, null);

        var row = (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id);
        row.Shortage.Should().Be(3);
        row.Status.Should().Be(StockStatus.Shortage);

        await env.Inventory.AdjustmentAsync(env.Project.Id, env.Nap.Id, 3, true, env.Today, "Llegó lo que faltaba");
        (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id).Status
            .Should().Be(StockStatus.Shortage, "los ajustes no cuentan como lo recibido para cubrir el diseño");
    }

    [Fact]
    public async Task El_stock_bajo_avisa_cuando_llega_al_minimo_definido_en_el_material()
    {
        using var env = await SetupAsync();
        var materials = new MaterialService(env.Db);
        var nap = (await materials.GetAsync(env.Nap.Id))!;
        nap.MinimumStock = 5;
        await materials.SaveAsync(nap);

        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 10, env.Today, null, null, null);
        (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id).Status.Should().Be(StockStatus.Ok);

        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 6, env.Today, null, null, null);
        (await env.Inventory.StockAsync(env.Project.Id)).Single(r => r.Material.Id == env.Nap.Id).Status.Should().Be(StockStatus.Low);
    }

    [Fact]
    public async Task Liquidacion_con_120_entregados_102_instalados_y_10_devueltos_da_18_de_sobrante_y_8_pendientes_y_bloquea_el_cierre()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 200, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 120, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 102, env.Today, null, null);
        await env.Inventory.ReturnAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 10, env.Today, null, null);
        await env.Projection.AdjustAsync(env.Site.Id, env.Nap.Id, 110, "Proyección de diseño", env.Today);

        var view = await env.Liquidation.GetAsync(env.Site.Id);
        var row = view.Rows.Single(r => r.Quantity.Material.Id == env.Nap.Id).Quantity;

        row.Surplus.Should().Be(18);
        row.PendingReturn.Should().Be(8);
        row.Variation.Should().Be(-8);
        view.CanClose.Should().BeFalse();
        view.BlockingMessage.Should().Contain("8").And.Contain("NAP de paso");

        var close = () => env.Liquidation.CloseAsync(env.Site.Id);
        await close.Should().ThrowAsync<BusinessRuleException>().WithMessage("*No se puede cerrar el nodo*");
    }

    [Fact]
    public async Task Justificar_los_pendientes_permite_cerrar_y_un_nodo_liquidado_no_acepta_movimientos()
    {
        using var env = await SetupAsync();
        await env.Inventory.ReceiptAsync(env.Project.Id, env.Nap.Id, 50, env.Today, null, null, null);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 20, env.Today, null, null, null);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 15, env.Today, null, null);

        var tooMuch = () => env.Liquidation.AddJustificationAsync(env.Site.Id, env.Nap.Id, 6, "Merma");
        await tooMuch.Should().ThrowAsync<BusinessRuleException>();

        await env.Liquidation.AddJustificationAsync(env.Site.Id, env.Nap.Id, 5, "Se dañaron en la instalación");
        var view = await env.Liquidation.GetAsync(env.Site.Id);
        view.CanClose.Should().BeTrue();

        await env.Liquidation.CloseAsync(env.Site.Id);
        (await env.Liquidation.IsClosedAsync(env.Site.Id)).Should().BeTrue();

        var delivery = () => env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 1, env.Today, null, null, null);
        await delivery.Should().ThrowAsync<BusinessRuleException>().WithMessage("*liquidado*");

        await env.Liquidation.ReopenAsync(env.Site.Id);
        await env.Inventory.DeliveryAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 1, env.Today, null, null, null);
    }

    [Fact]
    public async Task Los_ajustes_de_proyeccion_cambian_el_proyectado_vigente_y_exigen_motivo()
    {
        using var env = await SetupAsync();
        var cruce = (await new MaterialService(env.Db).ResolveAsync("Cruce americano"))!;
        await env.Projection.AdjustAsync(env.Site.Id, cruce.Id, 5, "Diseño de construcción", env.Today);
        await env.Projection.AdjustAsync(env.Site.Id, cruce.Id, -2, "Se eliminaron dos cruces en campo", env.Today);

        var quantity = (await new QuantityService(env.Db).GetSiteMaterialsAsync(env.Site.Id)).Single(q => q.Material.Id == cruce.Id);
        quantity.Adjustments.Should().Be(3);
        quantity.Projected.Should().Be(3);

        var noReason = () => env.Projection.AdjustAsync(env.Site.Id, cruce.Id, 1, " ", env.Today);
        await noReason.Should().ThrowAsync<BusinessRuleException>();
        var zero = () => env.Projection.AdjustAsync(env.Site.Id, cruce.Id, 0, "nada", env.Today);
        await zero.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Las_mediciones_se_califican_solas_si_el_proyecto_define_el_rango_de_dBm()
    {
        using var env = await SetupAsync();

        // Sin rango definido hay que indicar el resultado a mano.
        var manual = () => env.Measurements.AddAsync(env.Site.Id, "f-01", env.Today, -20, null, null);
        await manual.Should().ThrowAsync<BusinessRuleException>();
        (await env.Measurements.AddAsync(env.Site.Id, "f-01", env.Today, -20, true, null)).IsAcceptable.Should().BeTrue();

        var projects = new ProjectService(env.Db);
        var project = (await projects.GetAsync(env.Project.Id))!;
        project.MinAcceptableDbm = -27;
        project.MaxAcceptableDbm = -8;
        await projects.SaveAsync(project);

        (await env.Measurements.AddAsync(env.Site.Id, "F-02", env.Today, -15, null, null)).IsAcceptable.Should().BeTrue();
        (await env.Measurements.AddAsync(env.Site.Id, "F-03", env.Today, -30, true, null)).IsAcceptable.Should().BeFalse("el rango del proyecto manda");
        (await env.Measurements.AddAsync(env.Site.Id, "F-04", env.Today, -5, null, null)).IsAcceptable.Should().BeFalse();
    }

    [Fact]
    public async Task El_resumen_de_mediciones_cuenta_cada_FAT_una_sola_vez_con_su_ultima_medicion()
    {
        using var env = await SetupAsync();
        await env.Measurements.AddAsync(env.Site.Id, "F-01", env.Today.AddDays(-2), -35, false, null);
        await env.Measurements.AddAsync(env.Site.Id, "F-01", env.Today, -18, true, "Se corrigió el conector");
        await env.Measurements.AddAsync(env.Site.Id, "F-02", env.Today, -40, false, null);

        var summary = await env.Measurements.SummaryAsync(env.Site.Id);

        summary.Measured.Should().Be(2);
        summary.Acceptable.Should().Be(1);
        summary.NotAcceptable.Should().Be(1);
    }

    [Fact]
    public async Task Lo_instalado_alimenta_el_avance_del_nodo_por_grupo()
    {
        using var env = await SetupAsync();
        await env.Projection.AdjustAsync(env.Site.Id, env.Nap.Id, 10, "Proyección", env.Today);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 4, env.Today, null, "Primer día");
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 3, env.Today.AddDays(1), null, "Segundo día");

        var totals = await new QuantityService(env.Db).GetGroupTotalsBySiteAsync(env.Project.Id);

        totals[env.Site.Id][QuantityGroup.SubBox].Installed.Should().Be(7);
        totals[env.Site.Id][QuantityGroup.SubBox].Projected.Should().Be(10);
    }

    [Fact]
    public async Task La_precision_de_diseno_solo_cuenta_materiales_con_proyeccion()
    {
        using var env = await SetupAsync();
        await env.Projection.AdjustAsync(env.Site.Id, env.Nap.Id, 100, "Proyección", env.Today);
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Nap.Id, 90, env.Today, null, null);
        // Material sin proyección: no debe arrastrar la precisión a cero.
        await env.Inventory.InstalledAsync(env.Project.Id, env.Site.Id, env.Aro.Id, 400, env.Today, null, null);

        var view = await env.Liquidation.GetAsync(env.Site.Id);

        view.DesignAccuracy.Should().Be(90);
        view.WithVariation.Should().Be(1);
    }
}
