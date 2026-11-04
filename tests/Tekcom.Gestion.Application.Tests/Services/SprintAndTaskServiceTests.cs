using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class SprintAndTaskServiceTests
{
    private sealed record Env(TestDb Db, SprintService Sprints, TaskService Tasks, FakeFileStorage Storage, Project Project) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync()
    {
        var db = await TestDb.CreateAsync();
        var projects = new ProjectService(db);
        await using var context = db.CreateDbContext();
        var typeId = await context.ProjectTypes.Where(t => t.Code == "GENERAL").Select(t => t.Id).SingleAsync();
        var project = await projects.SaveAsync(new Project { Code = "G-1", Name = "Proyecto general", ProjectTypeId = typeId });
        var storage = new FakeFileStorage();
        return new Env(db, new SprintService(db, db.Clock), new TaskService(db, storage, db.Clock), storage, project);
    }

    private static async Task<Sprint> NewSprintAsync(Env env, string name = "Sprint 1", int startOffset = -12, int length = 20)
    {
        var today = env.Db.Clock.Today;
        return await env.Sprints.SaveAsync(new Sprint
        {
            ProjectId = env.Project.Id, Name = name,
            StartDate = today.AddDays(startOffset), EndDate = today.AddDays(startOffset + length),
        });
    }

    private static Task<TaskItem> NewTaskAsync(Env env, string title, int? sprintId, TaskItemStatus status = TaskItemStatus.ToDo) =>
        env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = title, SprintId = sprintId, Status = status });

    [Fact]
    public async Task Un_sprint_con_15_por_ciento_hecho_y_60_por_ciento_del_tiempo_aparece_atrasado()
    {
        using var env = await SetupAsync();
        var sprint = await NewSprintAsync(env); // 20 días, hoy es el día 12 = 60 %
        for (var i = 0; i < 20; i++)
        {
            await NewTaskAsync(env, $"Tarea {i}", sprint.Id, i < 3 ? TaskItemStatus.Done : TaskItemStatus.ToDo);
        }

        var summary = (await env.Sprints.GetAsync(sprint.Id))!;

        summary.ProgressPercent.Should().Be(15);
        summary.Pace.ElapsedPercent.Should().Be(60);
        summary.Pace.Status.Should().Be(PaceStatus.Behind);
    }

    [Fact]
    public async Task Solo_puede_haber_un_sprint_activo_por_proyecto()
    {
        using var env = await SetupAsync();
        var first = await NewSprintAsync(env, "Sprint 1");
        var second = await NewSprintAsync(env, "Sprint 2");
        await env.Sprints.StartAsync(first.Id);

        var act = () => env.Sprints.StartAsync(second.Id);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Sprint 1*");
    }

    [Fact]
    public async Task Al_cerrar_un_sprint_las_tareas_sin_terminar_pasan_al_siguiente_y_las_hechas_se_quedan()
    {
        using var env = await SetupAsync();
        var first = await NewSprintAsync(env, "Sprint 1");
        var next = await NewSprintAsync(env, "Sprint 2", startOffset: 8);
        await env.Sprints.StartAsync(first.Id);
        var done = await NewTaskAsync(env, "Hecha", first.Id, TaskItemStatus.Done);
        var open = await NewTaskAsync(env, "En proceso", first.Id, TaskItemStatus.InProgress);
        var todo = await NewTaskAsync(env, "Por hacer", first.Id);

        var result = await env.Sprints.CloseAsync(first.Id, next.Id);

        result.Moved.Should().Be(2);
        (await env.Tasks.GetAsync(done.Id))!.SprintId.Should().Be(first.Id);
        (await env.Tasks.GetAsync(open.Id))!.SprintId.Should().Be(next.Id);
        (await env.Tasks.GetAsync(todo.Id))!.SprintId.Should().Be(next.Id);
        (await env.Sprints.GetAsync(first.Id))!.Sprint.Status.Should().Be(SprintStatus.Closed);
    }

    [Fact]
    public async Task Al_cerrar_sin_sprint_de_destino_las_pendientes_vuelven_al_backlog()
    {
        using var env = await SetupAsync();
        var sprint = await NewSprintAsync(env);
        var pending = await NewTaskAsync(env, "Pendiente", sprint.Id);

        await env.Sprints.CloseAsync(sprint.Id, null);

        (await env.Tasks.GetAsync(pending.Id))!.SprintId.Should().BeNull();
    }

    [Fact]
    public async Task No_se_puede_agregar_tareas_a_un_sprint_cerrado_ni_cerrarlo_dos_veces()
    {
        using var env = await SetupAsync();
        var sprint = await NewSprintAsync(env);
        await env.Sprints.CloseAsync(sprint.Id, null);

        var add = () => NewTaskAsync(env, "Tarde", sprint.Id);
        await add.Should().ThrowAsync<BusinessRuleException>();

        var close = () => env.Sprints.CloseAsync(sprint.Id, null);
        await close.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task Mover_una_tarea_a_hecho_registra_la_fecha_y_al_sacarla_la_limpia()
    {
        using var env = await SetupAsync();
        var sprint = await NewSprintAsync(env);
        var task = await NewTaskAsync(env, "Tarea", sprint.Id);

        await env.Tasks.MoveAsync(task.Id, TaskItemStatus.Done, 0);
        var moved = (await env.Tasks.GetAsync(task.Id))!;
        moved.Status.Should().Be(TaskItemStatus.Done);
        moved.CompletedOn.Should().Be(env.Db.Clock.Today);

        await env.Tasks.MoveAsync(task.Id, TaskItemStatus.InReview, 0);
        (await env.Tasks.GetAsync(task.Id))!.CompletedOn.Should().BeNull();
    }

    [Fact]
    public async Task Mover_respeta_la_posicion_dentro_de_la_columna()
    {
        using var env = await SetupAsync();
        var sprint = await NewSprintAsync(env);
        var a = await NewTaskAsync(env, "A", sprint.Id);
        var b = await NewTaskAsync(env, "B", sprint.Id);
        var c = await NewTaskAsync(env, "C", sprint.Id);

        await env.Tasks.MoveAsync(c.Id, TaskItemStatus.ToDo, 0);

        var order = (await env.Tasks.ListAsync(new Application.Services.TaskFilter { ProjectId = env.Project.Id, SprintId = sprint.Id }))
            .Select(t => t.Title);
        order.Should().Equal("C", "A", "B");
    }

    [Fact]
    public async Task El_checklist_y_los_comentarios_se_guardan_con_su_autor()
    {
        using var env = await SetupAsync();
        var task = await NewTaskAsync(env, "Con detalle", null);

        await env.Tasks.AddChecklistItemAsync(task.Id, "Pedir permiso");
        await env.Tasks.AddChecklistItemAsync(task.Id, "Coordinar brigada");
        var loaded = (await env.Tasks.GetAsync(task.Id))!;
        await env.Tasks.SetChecklistItemAsync(loaded.Checklist.First().Id, true);
        await env.Tasks.AddCommentAsync(task.Id, "Ya llamé al supervisor");

        var reloaded = (await env.Tasks.GetAsync(task.Id))!;
        reloaded.Checklist.Count(i => i.IsDone).Should().Be(1);
        reloaded.Checklist.Should().HaveCount(2);
        reloaded.Comments.Single().CreatedBy.Should().Be("bryan@tekcom.com");
    }

    [Fact]
    public async Task Los_adjuntos_se_guardan_y_se_pueden_leer_pero_no_se_permiten_ejecutables()
    {
        using var env = await SetupAsync();
        var task = await NewTaskAsync(env, "Con adjunto", null);

        using var content = new MemoryStream("hola"u8.ToArray());
        var attachment = await env.Tasks.AddAttachmentAsync(task.Id, "foto.jpg", "image/jpeg", content);

        var opened = await env.Tasks.OpenAttachmentAsync(attachment.Id);
        opened!.Value.Attachment.FileName.Should().Be("foto.jpg");
        using var reader = new StreamReader(opened.Value.Content);
        (await reader.ReadToEndAsync()).Should().Be("hola");
    }

    [Fact]
    public async Task Mis_tareas_trae_solo_las_del_usuario_ordenadas_por_fecha_limite()
    {
        using var env = await SetupAsync();
        var today = env.Db.Clock.Today;
        await env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Tarde", AssigneeUserId = "u1", DueDate = today.AddDays(5) });
        await env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Pronto", AssigneeUserId = "u1", DueDate = today.AddDays(1) });
        await env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, Title = "Ajena", AssigneeUserId = "u2", DueDate = today });

        var mine = await env.Tasks.MyTasksAsync("u1", onlyOpen: true);

        mine.Select(t => t.Title).Should().Equal("Pronto", "Tarde");
    }

    [Fact]
    public async Task Una_tarea_no_puede_ir_a_un_sprint_de_otro_proyecto()
    {
        using var env = await SetupAsync();
        var other = await new ProjectService(env.Db).SaveAsync(new Project { Code = "G-2", Name = "Otro", ProjectTypeId = env.Project.ProjectTypeId });
        var foreign = await env.Sprints.SaveAsync(new Sprint { ProjectId = other.Id, Name = "Ajeno", StartDate = env.Db.Clock.Today, EndDate = env.Db.Clock.Today.AddDays(7) });
        var task = await NewTaskAsync(env, "Mía", null);

        var act = () => env.Tasks.AssignToSprintAsync(task.Id, foreign.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }
}
