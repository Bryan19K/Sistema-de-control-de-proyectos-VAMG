using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Models;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class InsightsServicesTests
{
    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required SnapshotService Snapshots { get; init; }
        public required AttentionService Attention { get; init; }
        public required PresentationService Presentation { get; init; }
        public required DashboardService Dashboard { get; init; }
        public required DocumentService Documents { get; init; }
        public required TaskService Tasks { get; init; }
        public required SiteService Sites { get; init; }
        public required InventoryService Inventory { get; init; }
        public required Project Project { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var quantities = new QuantityService(db);
        var projects = new ProjectService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var sprints = new SprintService(db, db.Clock);
        var inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock);
        var plan = new WorkPlanService(db, sites, db.Clock);
        var feeder = new FeederService(db);
        var dashboard = new DashboardService(db, projects, sites, plan, sprints, inventory, quantities, feeder, db.Clock);
        var storage = new FakeFileStorage();
        var tasks = new TaskService(db, storage, db.Clock);
        var documents = new DocumentService(db, storage);
        var minutes = new MinuteService(db, db.Clock);
        var measurements = new MeasurementService(db);
        var snapshots = new SnapshotService(db, dashboard, projects, db.Clock);
        var attention = new AttentionService(sites, tasks, inventory, minutes, documents, measurements, sprints, db.Clock);
        var presentation = new PresentationService(projects, new ReferenceDataService(db), plan, sites, dashboard, inventory, tasks, new FakeUserAdmin(), sprints,
            measurements, new LiquidationService(db, quantities, db.CurrentUser, db.Clock), documents, snapshots, attention, db.Clock);

        await using var context = db.CreateDbContext();
        var ftth = await context.ProjectTypes.SingleAsync(t => t.Code == "FTTH");
        var project = await projects.SaveAsync(new Project
        {
            Code = "I-1", Name = "Insights", ProjectTypeId = ftth.Id, Status = ProjectStatus.Active,
            PlannedStart = db.Clock.Today.AddDays(-20), PlannedEnd = db.Clock.Today.AddDays(10),
        });
        return new Env
        {
            Db = db, Snapshots = snapshots, Attention = attention, Presentation = presentation, Dashboard = dashboard, Documents = documents,
            Tasks = tasks, Sites = sites, Inventory = inventory, Project = project,
        };
    }

    [Fact]
    public async Task La_foto_diaria_se_guarda_una_vez_por_dia_y_se_actualiza()
    {
        using var env = await SetupAsync();
        await env.Sites.SaveAsync(new Site { ProjectId = env.Project.Id, Code = "S1" });

        var first = await env.Snapshots.CaptureAsync(env.Project.Id);
        await env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Hecha", Status = TaskItemStatus.Done });
        var second = await env.Snapshots.CaptureAsync(env.Project.Id);

        (await env.Snapshots.ListAsync(env.Project.Id)).Should().ContainSingle();
        second!.Id.Should().Be(first!.Id);
        second.TasksDone.Should().Be(1);
        second.TotalSites.Should().Be(1);
        second.ElapsedPercent.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Capturar_todos_ignora_los_proyectos_cerrados()
    {
        using var env = await SetupAsync();
        var projects = new ProjectService(env.Db);
        await using var context = env.Db.CreateDbContext();
        var general = await context.ProjectTypes.SingleAsync(t => t.Code == "GENERAL");
        await projects.SaveAsync(new Project { Code = "C-1", Name = "Cerrado", ProjectTypeId = general.Id, Status = ProjectStatus.Closed });

        (await env.Snapshots.CaptureAllAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Atencion_reune_tareas_vencidas_y_nodos_finalizados_sin_ATP_firmado()
    {
        using var env = await SetupAsync();
        var site = await env.Sites.SaveAsync(new Site { ProjectId = env.Project.Id, Code = "SON19" });
        await env.Sites.SetOdnFinalizedAsync(site.Id, true);
        await env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Atrasada", DueDate = env.Today.AddDays(-3) });

        var items = await env.Attention.GetAsync(env.Project.Id);

        items.Should().Contain(i => i.Title.Contains("tarea(s) vencida") && i.Severity == AttentionSeverity.Critical);
        items.Should().Contain(i => i.Title.Contains("sin ATP firmado") && i.Detail.Contains("SON19"));

        await env.Documents.UploadAsync(new Application.Services.DocumentUpload(env.Project.Id, site.Id, null, DocumentKind.Atp, "ATP", env.Today, true, null, "atp.pdf", "application/pdf"),
            new MemoryStream([1]));
        (await env.Attention.GetAsync(env.Project.Id)).Should().NotContain(i => i.Title.Contains("sin ATP firmado"));
    }

    [Fact]
    public async Task Atencion_vacia_cuando_todo_esta_en_orden()
    {
        using var env = await SetupAsync();
        (await env.Attention.GetAsync(env.Project.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task La_presentacion_trae_evidencias_brigadas_y_comparativo()
    {
        using var env = await SetupAsync();
        var contractor = await new ReferenceDataService(env.Db).SaveContractorAsync(new Contractor { Name = "Brigada A" });
        var site = await env.Sites.SaveAsync(new Site { ProjectId = env.Project.Id, Code = "SON22", ContractorId = contractor.Id });
        await env.Sites.SetOdnFinalizedAsync(site.Id, true);
        await env.Documents.UploadAsync(new Application.Services.DocumentUpload(env.Project.Id, site.Id, null, DocumentKind.Photo, "Foto", env.Today, false, null, "foto.jpg", "image/jpeg"),
            new MemoryStream([1]));

        var data = await env.Presentation.GetAsync(env.Project.Id);

        data.Evidence.Should().ContainSingle().Which.Photos.Should().Be(1);
        data.Evidence[0].PhotoIds.Should().HaveCount(1);
        data.Evidence[0].SignedAtp.Should().BeFalse();
        data.Brigades.Should().ContainSingle().Which.Name.Should().Be("Brigada A");
        data.WeeklyInstalled.Should().HaveCount(8);
        (await env.Snapshots.ListAsync(env.Project.Id)).Should().NotBeEmpty("abrir la presentación guarda la foto de hoy");

        var all = await env.Dashboard.CompareAllAsync();
        all.Should().ContainSingle().Which.Name.Should().Be("Insights");
    }
}
