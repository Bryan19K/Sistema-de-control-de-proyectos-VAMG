using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class SiteAndProjectServiceTests
{
    private static async Task<(TestDb Db, ProjectService Projects, SiteService Sites, Project Project)> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var projects = new ProjectService(db);
        var sites = new SiteService(db, new QuantityService(db), db.Clock);

        int ftthId;
        await using (var context = db.CreateDbContext())
        {
            ftthId = await context.ProjectTypes.Where(t => t.Code == "FTTH").Select(t => t.Id).SingleAsync();
        }

        var project = await projects.SaveAsync(new Project
        {
            Code = "P-1",
            Name = "Proyecto de prueba",
            ProjectTypeId = ftthId,
            PlannedStart = new DateOnly(2026, 10, 1),
            PlannedEnd = new DateOnly(2026, 12, 1),
        });
        return (db, projects, sites, project);
    }

    [Fact]
    public async Task Crear_un_nodo_genera_las_etapas_del_flujo()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;

        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "son19" });

        site.Code.Should().Be("SON19");
        var stages = await sites.StagesAsync(site.Id);
        stages.Select(s => s.Code).Should().Contain([StageCode.Layout, StageCode.FcCable, StageCode.Atp, StageCode.FinalReception]);
        stages.Should().NotContain(s => s.Code == StageCode.Caes, "CAES es una casilla informativa, no una etapa");
        stages.Should().OnlyContain(s => s.Status == StageStatus.Pending);
    }

    [Fact]
    public async Task No_permite_dos_nodos_con_el_mismo_codigo_en_un_proyecto()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;
        await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON01" });

        var act = () => sites.SaveAsync(new Site { ProjectId = project.Id, Code = "son01" });

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*SON01*");
    }

    [Fact]
    public async Task Las_etapas_se_pueden_registrar_en_cualquier_orden()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON02", PlannedStart = new DateOnly(2026, 10, 1), PlannedEnd = new DateOnly(2026, 10, 31) });

        // Se salta la secuencia: ATP finalizada sin replanteo ni entrega.
        await sites.SetStageAsync(site.Id, StageCode.Atp, StageStatus.Finished, null, null);

        var stages = await sites.StagesAsync(site.Id);
        stages.Single(s => s.Code == StageCode.Atp).Status.Should().Be(StageStatus.Finished);
        stages.Single(s => s.Code == StageCode.Atp).ActualEnd.Should().Be(new DateOnly(2026, 11, 4));
        stages.Single(s => s.Code == StageCode.Layout).Status.Should().Be(StageStatus.Pending);
    }

    [Fact]
    public async Task En_correccion_y_NA_solo_aplican_a_las_correcciones()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON03" });

        var invalid = () => sites.SetStageAsync(site.Id, StageCode.Atp, StageStatus.InCorrection, null, null);
        await invalid.Should().ThrowAsync<BusinessRuleException>();

        await sites.SetStageAsync(site.Id, StageCode.Corrections, StageStatus.InCorrection, null, null);
        (await sites.StagesAsync(site.Id)).Single(s => s.Code == StageCode.Corrections).Status.Should().Be(StageStatus.InCorrection);
    }

    [Fact]
    public async Task El_avance_del_nodo_pondera_por_dias_y_un_nodo_listo_necesita_ODN_y_feeder_finalizados()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;
        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON04" });

        await sites.SetStageAsync(site.Id, StageCode.Layout, StageStatus.Finished, null, null);
        var summary = (await sites.GetSummaryAsync(site.Id))!;
        summary.ProgressPercent.Should().BeApproximately(100m / 12, 0.1m, "1 de 12 etapas con el mismo peso");
        summary.Site.IsReadyToDeliver.Should().BeFalse();

        await sites.SetOdnFinalizedAsync(site.Id, true);
        (await sites.GetAsync(site.Id))!.IsReadyToDeliver.Should().BeFalse("falta el feeder");

        await sites.SetFeederStatusAsync(site.Id, FeederStatus.InProgress);
        (await sites.GetAsync(site.Id))!.IsReadyToDeliver.Should().BeFalse();

        await sites.SetFeederStatusAsync(site.Id, FeederStatus.Finished);
        (await sites.GetAsync(site.Id))!.IsReadyToDeliver.Should().BeTrue();
    }

    [Fact]
    public async Task Los_dias_de_la_plantilla_cambian_el_peso_de_cada_etapa()
    {
        var (db, _, sites, project) = await SetupAsync();
        using var _ = db;
        var reference = new ReferenceDataService(db);
        var type = (await reference.ProjectTypesAsync()).Single(t => t.Code == "FTTH");
        var definitions = await reference.StageDefinitionsAsync(type.Id);

        var layout = definitions.Single(d => d.Code == StageCode.Layout);
        layout.PlannedDays = 11; // las otras 11 etapas pesan 1 día cada una
        await reference.SaveStageDefinitionAsync(layout);

        var site = await sites.SaveAsync(new Site { ProjectId = project.Id, Code = "SON05" });
        await sites.SetStageAsync(site.Id, StageCode.Layout, StageStatus.Finished, null, null);

        (await sites.GetSummaryAsync(site.Id))!.ProgressPercent.Should().Be(50);
    }

    [Fact]
    public async Task Valida_las_reglas_del_proyecto()
    {
        var (db, projects, _, project) = await SetupAsync();
        using var _ = db;

        var duplicate = () => projects.SaveAsync(new Project { Code = "P-1", Name = "Otro", ProjectTypeId = project.ProjectTypeId });
        await duplicate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*P-1*");

        var dates = () => projects.SaveAsync(new Project
        {
            Code = "P-2", Name = "Fechas", ProjectTypeId = project.ProjectTypeId,
            PlannedStart = new DateOnly(2026, 5, 1), PlannedEnd = new DateOnly(2026, 4, 1),
        });
        await dates.Should().ThrowAsync<BusinessRuleException>();

        var dbm = () => projects.SaveAsync(new Project
        {
            Code = "P-3", Name = "dBm", ProjectTypeId = project.ProjectTypeId,
            MinAcceptableDbm = -10, MaxAcceptableDbm = -20,
        });
        await dbm.Should().ThrowAsync<BusinessRuleException>().WithMessage("*dBm*");
    }
}
