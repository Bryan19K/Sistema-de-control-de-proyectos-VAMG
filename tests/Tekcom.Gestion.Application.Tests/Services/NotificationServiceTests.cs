using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tekcom.Gestion.Application.Notifications;
using Tekcom.Gestion.Application.Tests.Support;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Identity;
using Tekcom.Gestion.Infrastructure.Identity;
using Tekcom.Gestion.Infrastructure.Notifications;
using Tekcom.Gestion.Infrastructure.Services;

namespace Tekcom.Gestion.Application.Tests.Services;

public class NotificationServiceTests
{
    private sealed class FakeChannel(NotificationChannelKind kind, bool configured = true) : INotificationChannel
    {
        public NotificationChannelKind Kind => kind;
        public bool IsConfigured => configured;
        public string? MissingSetup => configured ? null : "Falta configurar.";
        public string? FailWith { get; set; }
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string recipient, string subject, string body, CancellationToken ct = default)
        {
            if (FailWith is not null)
            {
                throw new InvalidOperationException(FailWith);
            }

            Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    private sealed class Env : IDisposable
    {
        public required TestDb Db { get; init; }
        public required NotificationService Service { get; init; }
        public required TaskService Tasks { get; init; }
        public required SprintService Sprints { get; init; }
        public required FakeChannel Email { get; init; }
        public required FakeChannel WhatsApp { get; init; }
        public required Project Project { get; init; }
        public required ApplicationUser Admin { get; init; }
        public required ApplicationUser Worker { get; init; }
        public DateOnly Today => Db.Clock.Today;
        public void Dispose() => Db.Dispose();
    }

    private static async Task<Env> SetupAsync(bool whatsAppConfigured = true)
    {
        var db = await TestDb.CreateAsync();
        var projects = new ProjectService(db);
        var quantities = new QuantityService(db);
        var sites = new SiteService(db, quantities, db.Clock);
        var sprints = new SprintService(db, db.Clock);
        var inventory = new InventoryService(db, quantities, db.CurrentUser, db.Clock);
        var plan = new WorkPlanService(db, sites, db.Clock);
        var dashboard = new DashboardService(db, projects, sites, plan, sprints, inventory, quantities, new FeederService(db), db.Clock);

        await using var context = db.CreateDbContext();
        var typeId = await context.ProjectTypes.Where(t => t.Code == "GENERAL").Select(t => t.Id).SingleAsync();
        var project = await projects.SaveAsync(new Project { Code = "N-1", Name = "Proyecto avisos", ProjectTypeId = typeId, Status = ProjectStatus.Active });

        var admin = new ApplicationUser { UserName = "admin@tekcom.com", Email = "admin@tekcom.com", FullName = "Ana Admin", PhoneNumber = "7000-0001" };
        var worker = new ApplicationUser { UserName = "tec@tekcom.com", Email = "tec@tekcom.com", FullName = "Tito Técnico", PhoneNumber = "7000-0002" };
        context.Users.AddRange(admin, worker);
        var adminRole = new IdentityRole(AppRoles.Admin) { NormalizedName = AppRoles.Admin.ToUpperInvariant() };
        var contractorRole = new IdentityRole(AppRoles.Contratista) { NormalizedName = AppRoles.Contratista.ToUpperInvariant() };
        context.Roles.AddRange(adminRole, contractorRole);
        context.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = admin.Id, RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = worker.Id, RoleId = contractorRole.Id });
        await context.SaveChangesAsync();

        var email = new FakeChannel(NotificationChannelKind.Email);
        var whatsApp = new FakeChannel(NotificationChannelKind.WhatsApp, whatsAppConfigured);
        var service = new NotificationService(db, [email, whatsApp], dashboard, projects, sprints, inventory, db.Clock,
            NullLogger<NotificationService>.Instance);
        return new Env
        {
            Db = db, Service = service, Tasks = new TaskService(db, new FakeFileStorage(), db.Clock), Sprints = sprints,
            Email = email, WhatsApp = whatsApp, Project = project, Admin = admin, Worker = worker,
        };
    }

    private static Task<TaskItem> NewTaskAsync(Env env, string title, int dueOffset, string? assignee = null, TaskItemStatus status = TaskItemStatus.ToDo) =>
        env.Tasks.SaveAsync(new TaskItem
        {
            ProjectId = env.Project.Id, Title = title, AssigneeUserId = assignee ?? env.Worker.Id,
            DueDate = env.Today.AddDays(dueOffset), Status = status,
        });

    [Fact]
    public async Task Al_asignar_una_tarea_se_avisa_por_correo_y_WhatsApp_y_queda_en_la_bitacora()
    {
        using var env = await SetupAsync();
        var task = await NewTaskAsync(env, "Instalar cruce", 3);

        var sent = await env.Service.NotifyTaskAssignedAsync(task.Id, assignedByUserId: env.Admin.Id);

        sent.Should().Be(2);
        env.Email.Sent.Should().ContainSingle().Which.To.Should().Be("tec@tekcom.com");
        env.Email.Sent[0].Body.Should().Contain("Instalar cruce");
        env.WhatsApp.Sent.Should().ContainSingle().Which.To.Should().Be("7000-0002");
        var log = await env.Service.LogAsync();
        log.Should().HaveCount(2).And.OnlyContain(l => l.Status == NotificationStatus.Sent && l.Type == NotificationType.TaskAssigned);
    }

    [Fact]
    public async Task No_avisa_a_quien_se_asigna_la_tarea_a_si_mismo_ni_repite_el_aviso()
    {
        using var env = await SetupAsync();
        var task = await NewTaskAsync(env, "Mi tarea", 3);

        (await env.Service.NotifyTaskAssignedAsync(task.Id, assignedByUserId: env.Worker.Id)).Should().Be(0);
        (await env.Service.NotifyTaskAssignedAsync(task.Id, assignedByUserId: env.Admin.Id)).Should().Be(2);
        (await env.Service.NotifyTaskAssignedAsync(task.Id, assignedByUserId: env.Admin.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Un_canal_sin_configurar_no_envia_ni_llena_la_bitacora()
    {
        using var env = await SetupAsync(whatsAppConfigured: false);
        var task = await NewTaskAsync(env, "Tarea", 3);

        await env.Service.NotifyTaskAssignedAsync(task.Id);

        env.WhatsApp.Sent.Should().BeEmpty();
        env.Email.Sent.Should().HaveCount(1);
        (await env.Service.LogAsync()).Should().HaveCount(1);
        env.Service.Channels().Single(c => c.Kind == NotificationChannelKind.WhatsApp).IsConfigured.Should().BeFalse();
    }

    [Fact]
    public async Task Una_regla_desactivada_o_sin_canal_no_envia()
    {
        using var env = await SetupAsync();
        await env.Service.SaveRuleAsync(NotificationType.TaskAssigned, enabled: true, sendEmail: false, sendWhatsApp: true);
        var task = await NewTaskAsync(env, "Tarea", 3);

        await env.Service.NotifyTaskAssignedAsync(task.Id);
        env.Email.Sent.Should().BeEmpty();
        env.WhatsApp.Sent.Should().HaveCount(1);

        await env.Service.SaveRuleAsync(NotificationType.TaskOverdue, enabled: false, sendEmail: true, sendWhatsApp: true);
        await NewTaskAsync(env, "Vencida", -2);
        await env.Service.RunChecksAsync();
        env.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task La_revision_avisa_tareas_por_vencer_y_vencidas_y_no_repite_en_el_mismo_dia()
    {
        using var env = await SetupAsync();
        await NewTaskAsync(env, "Mañana", 1);
        await NewTaskAsync(env, "Atrasada", -3);
        await NewTaskAsync(env, "Lejana", 10);
        await NewTaskAsync(env, "Hecha vencida", -5, status: TaskItemStatus.Done);

        var first = await env.Service.RunChecksAsync();
        var second = await env.Service.RunChecksAsync();

        first.Should().BeGreaterThanOrEqualTo(4); // 2 tareas x 2 canales (+ posibles avisos de atraso)
        second.Should().Be(0);
        var toWorker = env.Email.Sent.Where(m => m.To == "tec@tekcom.com").ToList();
        toWorker.Should().Contain(m => m.Subject == "Tarea por vencer" && m.Body.Contains("Mañana"));
        toWorker.Should().Contain(m => m.Subject == "Tarea vencida" && m.Body.Contains("Atrasada") && m.Body.Contains("3 día"));
        toWorker.Should().NotContain(m => m.Body.Contains("Lejana") || m.Body.Contains("Hecha vencida"));
    }

    [Fact]
    public async Task Un_envio_fallido_queda_registrado_con_su_error()
    {
        using var env = await SetupAsync();
        env.Email.FailWith = "SMTP caído";
        var task = await NewTaskAsync(env, "Tarea", 3);

        var sent = await env.Service.NotifyTaskAssignedAsync(task.Id);

        sent.Should().Be(1); // solo WhatsApp salió
        var failed = (await env.Service.LogAsync()).Single(l => l.Status == NotificationStatus.Failed);
        failed.Channel.Should().Be(NotificationChannelKind.Email);
        failed.Error.Should().Contain("SMTP caído");
    }

    [Fact]
    public async Task El_sprint_atrasado_se_avisa_a_los_administradores_y_no_a_los_contratistas()
    {
        using var env = await SetupAsync();
        var sprint = await env.Sprints.SaveAsync(new Sprint
        {
            ProjectId = env.Project.Id, Name = "Sprint 1", StartDate = env.Today.AddDays(-12), EndDate = env.Today.AddDays(2),
        });
        await env.Sprints.StartAsync(sprint.Id);
        await NewTaskForSprintAsync(env, sprint.Id);

        await env.Service.RunChecksAsync();

        env.Email.Sent.Where(m => m.Subject == "Sprint atrasado").Should().ContainSingle().Which.To.Should().Be("admin@tekcom.com");
    }

    private static Task<TaskItem> NewTaskForSprintAsync(Env env, int sprintId) =>
        env.Tasks.SaveAsync(new TaskItem { ProjectId = env.Project.Id, SprintId = sprintId, Title = "Pendiente", Status = TaskItemStatus.ToDo });

    [Fact]
    public async Task El_resumen_diario_va_solo_a_la_gerencia_y_administracion_con_los_conteos()
    {
        using var env = await SetupAsync();
        await NewTaskAsync(env, "Atrasada", -3);
        await NewTaskAsync(env, "Para hoy", 0);

        var sent = await env.Service.SendDailySummaryAsync();

        sent.Should().Be(1); // el resumen diario no se manda por WhatsApp
        var message = env.Email.Sent.Single();
        message.To.Should().Be("admin@tekcom.com");
        message.Body.Should().Contain("Proyectos activos: 1").And.Contain("Tareas vencidas: 1").And.Contain("vencen hoy: 1");
        (await env.Service.SendDailySummaryAsync()).Should().Be(0);
    }

    [Fact]
    public async Task La_prueba_se_envia_al_usuario_y_exige_canal_configurado_y_contacto()
    {
        using var env = await SetupAsync(whatsAppConfigured: false);

        var log = await env.Service.SendTestAsync(env.Admin.Id, NotificationChannelKind.Email);
        log.Status.Should().Be(NotificationStatus.Sent);
        env.Email.Sent.Should().ContainSingle().Which.Subject.Should().Be("Prueba de notificación");

        var whatsApp = () => env.Service.SendTestAsync(env.Admin.Id, NotificationChannelKind.WhatsApp);
        await whatsApp.Should().ThrowAsync<Application.Common.BusinessRuleException>().WithMessage("*no está configurado*");

        await using var context = env.Db.CreateDbContext();
        var user = await context.Users.FirstAsync(u => u.Id == env.Worker.Id);
        user.Email = null;
        await context.SaveChangesAsync();
        var noEmail = () => env.Service.SendTestAsync(env.Worker.Id, NotificationChannelKind.Email);
        await noEmail.Should().ThrowAsync<Application.Common.BusinessRuleException>().WithMessage("*no tiene correo*");
    }

    [Theory]
    [InlineData("7777-1234", "50377771234")]
    [InlineData("+503 7777 1234", "50377771234")]
    [InlineData("(503) 7777-1234", "50377771234")]
    [InlineData("1 305 555 0100", "13055550100")]
    public void El_telefono_se_normaliza_con_el_codigo_de_El_Salvador(string input, string expected)
    {
        var channel = new WhatsAppChannel(new HttpClient(), Options.Create(new WhatsAppOptions()));
        channel.NormalizePhone(input).Should().Be(expected);
    }

    [Fact]
    public void Los_canales_sin_configuracion_dicen_que_les_falta()
    {
        var whatsApp = new WhatsAppChannel(new HttpClient(), Options.Create(new WhatsAppOptions { PhoneNumberId = "123" }));
        whatsApp.IsConfigured.Should().BeFalse();
        whatsApp.MissingSetup.Should().Contain("AccessToken");

        var email = new EmailChannel(Options.Create(new EmailOptions { Host = "smtp.test", FromAddress = "avisos@tekcom.com" }));
        email.IsConfigured.Should().BeTrue();
        new EmailChannel(Options.Create(new EmailOptions())).MissingSetup.Should().Contain("Email:Host");
    }
}
