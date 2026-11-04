using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class WorkPlanServiceTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required WorkPlanService Plan { get; init; }
        public required SiteService Sites { get; init; }
        public required Project Project { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync(params string[] siteCodes)
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await new ProjectService(db).SaveAsync(new Project { Code = "PL-1", Name = "Plan", ProjectTypeId = ftth.Id });
        foreach (var code in siteCodes)
        {
            await sites.SaveAsync(new Site { ProjectId = project.Id, Code = code });
        }

        return new Env { Db = db, Plan = new WorkPlanService(db, sites, db.Clock), Sites = sites, Project = project };
    }

    private static async Task<int> TemplateIdAsync(Env env, string name) =>
        (await env.Plan.TemplatesAsync()).Single(t => t.Name == name).Id;

    [Fact]
    public async Task La_plantilla_FTTH_estandar_crea_3_fases_con_una_actividad_por_nodo_y_un_hito()
    {
        using var env = await SetupAsync("SON19", "SON22", "SON23");
        var created = await env.Plan.ApplyTemplateAsync(env.Project.Id, await TemplateIdAsync(env, "Plan FTTH estándar"), env.Today, replace: false);

        var plan = await env.Plan.GetAsync(env.Project.Id);

        created.Should().Be(2 + 3 + 2);
        plan.Phases.Select(p => p.Phase.Name).Should().Equal("Fase 1 · Preparación", "Fase 2 · Construcción ODN", "Fase 3 · Cierre");
        plan.Phases[1].Activities.Select(a => a.Activity.Name).Should().Equal("Nodo SON19", "Nodo SON22", "Nodo SON23");
        plan.Phases[1].Activities.Should().OnlyContain(a => a.Activity.SiteId != null);
        var milestone = plan.Activities.Single(a => a.Activity.IsMilestone);
        milestone.Activity.Name.Should().Be("Entrega al cliente");
        milestone.Activity.PlannedStart.Should().Be(env.Today.AddDays(55));
        plan.End.Should().Be(env.Today.AddDays(55));
    }

    [Fact]
    public async Task Aplicar_una_plantilla_sobre_un_plan_existente_exige_reemplazar()
    {
        using var env = await SetupAsync("SON19");
        var id = await TemplateIdAsync(env, "Plan general");
        await env.Plan.ApplyTemplateAsync(env.Project.Id, id, env.Today, replace: false);

        var again = () => env.Plan.ApplyTemplateAsync(env.Project.Id, id, env.Today, replace: false);
        await again.Should().ThrowAsync<BusinessRuleException>().WithMessage("*reemplazar*");

        await env.Plan.ApplyTemplateAsync(env.Project.Id, await TemplateIdAsync(env, "Plan FTTH estándar"), env.Today, replace: true);
        (await env.Plan.GetAsync(env.Project.Id)).Phases.Should().HaveCount(3);
    }

    [Fact]
    public async Task El_avance_de_una_actividad_vinculada_a_un_nodo_es_el_del_nodo()
    {
        using var env = await SetupAsync("SON19");
        await env.Plan.ApplyTemplateAsync(env.Project.Id, await TemplateIdAsync(env, "Plan FTTH estándar"), env.Today, false);
        var site = (await env.Sites.ListAsync(env.Project.Id)).Single().Site;
        await env.Sites.SetStageAsync(site.Id, StageCode.Layout, StageStatus.Finished, null, null);
        await env.Sites.SetStageAsync(site.Id, StageCode.FcCable, StageStatus.Finished, null, null);

        var nodeActivity = (await env.Plan.GetAsync(env.Project.Id)).Activities.Single(a => a.Activity.SiteId == site.Id);

        nodeActivity.Progress.Should().BeApproximately(200m / 12, 0.1m, "2 de 12 etapas con el mismo peso");
    }

    [Fact]
    public async Task El_avance_de_una_fase_y_del_plan_se_pondera_por_la_duracion_de_las_actividades()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        var longTask = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Larga", PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(8) }, []);   // 9 días
        var shortTask = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Corta", PlannedStart = env.Today, PlannedEnd = env.Today }, []);       // 1 día
        await env.Plan.SetProgressAsync(shortTask.Id, 100);
        await env.Plan.SetProgressAsync(longTask.Id, 0);

        var plan = await env.Plan.GetAsync(env.Project.Id);

        plan.Progress.Should().Be(10, "1 de 10 días de trabajo está hecho");
        plan.Phases.Single().Progress.Should().Be(10);
    }

    [Fact]
    public async Task Marcar_avance_completa_las_fechas_reales()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        var activity = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "A", PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(5) }, []);

        await env.Plan.SetProgressAsync(activity.Id, 40);
        var started = (await env.Plan.GetAsync(env.Project.Id)).Activities.Single().Activity;
        started.ActualStart.Should().Be(env.Today);
        started.ActualEnd.Should().BeNull();

        await env.Plan.SetProgressAsync(activity.Id, 100);
        (await env.Plan.GetAsync(env.Project.Id)).Activities.Single().Activity.ActualEnd.Should().Be(env.Today);

        await env.Plan.SetProgressAsync(activity.Id, 60);
        (await env.Plan.GetAsync(env.Project.Id)).Activities.Single().Activity.ActualEnd.Should().BeNull();
    }

    [Fact]
    public async Task Las_dependencias_no_pueden_formar_ciclos_ni_apuntar_a_si_mismas()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        WorkPlanActivity New(string name) => new() { PhaseId = phase.Id, Name = name, PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(2) };
        var a = await env.Plan.SaveActivityAsync(New("A"), []);
        var b = await env.Plan.SaveActivityAsync(New("B"), [a.Id]);
        var c = await env.Plan.SaveActivityAsync(New("C"), [b.Id]);

        var cycle = () => env.Plan.SaveActivityAsync(new WorkPlanActivity { Id = a.Id, PhaseId = phase.Id, Name = "A", PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(2) }, [c.Id]);
        await cycle.Should().ThrowAsync<BusinessRuleException>().WithMessage("*ciclo*");

        var self = () => env.Plan.SaveActivityAsync(new WorkPlanActivity { Id = b.Id, PhaseId = phase.Id, Name = "B", PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(2) }, [b.Id]);
        await self.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Una_actividad_que_empieza_antes_de_que_termine_su_predecesora_se_marca()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        var first = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Primera", PlannedStart = env.Today, PlannedEnd = env.Today.AddDays(9) }, []);
        await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Temprana", PlannedStart = env.Today.AddDays(5), PlannedEnd = env.Today.AddDays(12) }, [first.Id]);
        await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "A tiempo", PlannedStart = env.Today.AddDays(10), PlannedEnd = env.Today.AddDays(14) }, [first.Id]);

        var plan = await env.Plan.GetAsync(env.Project.Id);

        plan.Activities.Single(a => a.Activity.Name == "Temprana").StartsBeforePredecessor.Should().BeTrue();
        plan.Activities.Single(a => a.Activity.Name == "A tiempo").StartsBeforePredecessor.Should().BeFalse();
    }

    [Fact]
    public async Task Un_hito_vencido_sin_cumplir_aparece_atrasado_y_cumplido_aparece_finalizado()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Cierre" });
        var late = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Hito tarde", IsMilestone = true, PlannedStart = env.Today.AddDays(-5), PlannedEnd = env.Today.AddDays(-5) }, []);
        var done = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Hito listo", IsMilestone = true, PlannedStart = env.Today.AddDays(-5), PlannedEnd = env.Today.AddDays(-5), ActualEnd = env.Today.AddDays(-6) }, []);

        var plan = await env.Plan.GetAsync(env.Project.Id);

        var lateView = plan.Activities.Single(a => a.Activity.Id == late.Id);
        lateView.Pace.Status.Should().Be(PaceStatus.Behind);
        lateView.Pace.DaysDelta.Should().Be(-5);
        plan.Activities.Single(a => a.Activity.Id == done.Id).Pace.Status.Should().Be(PaceStatus.Completed);
    }

    [Fact]
    public async Task El_plan_atrasado_calcula_el_ritmo_y_el_cierre_proyectado()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        var activity = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "Única", PlannedStart = env.Today.AddDays(-30), PlannedEnd = env.Today.AddDays(29) }, []); // 60 días
        await env.Plan.SetProgressAsync(activity.Id, 20);

        var plan = await env.Plan.GetAsync(env.Project.Id);

        plan.Pace.Status.Should().Be(PaceStatus.Behind);
        plan.Pace.ElapsedPercent.Should().Be(50);
        plan.Pace.ProjectedEnd.Should().NotBeNull();
        plan.Pace.ProjectedEnd!.Value.Should().BeAfter(env.Today.AddDays(30));
    }

    [Fact]
    public async Task Guardar_el_plan_como_plantilla_y_aplicarla_a_otro_proyecto_reproduce_la_estructura()
    {
        using var env = await SetupAsync("SON19", "SON22");
        await env.Plan.ApplyTemplateAsync(env.Project.Id, await TemplateIdAsync(env, "Plan FTTH estándar"), env.Today, false);

        var saved = await env.Plan.SaveAsTemplateAsync(env.Project.Id, "Mi plan", "Basado en Sonsonate");

        saved.IsSystem.Should().BeFalse();
        await using var context = env.Db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var other = await new ProjectService(env.Db).SaveAsync(new Project { Code = "PL-2", Name = "Otro", ProjectTypeId = ftth.Id });
        await env.Sites.SaveAsync(new Site { ProjectId = other.Id, Code = "AAA01" });
        await env.Sites.SaveAsync(new Site { ProjectId = other.Id, Code = "AAA02" });
        await env.Sites.SaveAsync(new Site { ProjectId = other.Id, Code = "AAA03" });

        await env.Plan.ApplyTemplateAsync(other.Id, saved.Id, env.Today.AddDays(100), false);

        var plan = await env.Plan.GetAsync(other.Id);
        plan.Phases.Select(p => p.Phase.Name).Should().Equal("Fase 1 · Preparación", "Fase 2 · Construcción ODN", "Fase 3 · Cierre");
        plan.Phases[1].Activities.Select(a => a.Activity.Name).Should().Equal("Nodo AAA01", "Nodo AAA02", "Nodo AAA03");
        plan.Start.Should().Be(env.Today.AddDays(100));
    }

    [Fact]
    public async Task Eliminar_una_fase_elimina_sus_actividades_y_dependencias_sin_borrar_fisicamente()
    {
        using var env = await SetupAsync();
        var phase = await env.Plan.SavePhaseAsync(new WorkPlanPhase { ProjectId = env.Project.Id, Name = "Fase" });
        var a = await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "A", PlannedStart = env.Today, PlannedEnd = env.Today }, []);
        await env.Plan.SaveActivityAsync(new WorkPlanActivity { PhaseId = phase.Id, Name = "B", PlannedStart = env.Today, PlannedEnd = env.Today }, [a.Id]);

        await env.Plan.DeletePhaseAsync(phase.Id);

        (await env.Plan.GetAsync(env.Project.Id)).IsEmpty.Should().BeTrue();
        await using var context = env.Db.CreateDbContext();
        (await context.WorkPlanActivities.IgnoreQueryFilters().CountAsync(x => x.IsDeleted)).Should().Be(2);
    }
}
