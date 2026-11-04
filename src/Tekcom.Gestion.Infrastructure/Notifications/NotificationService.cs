using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Notifications;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Identity;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Notifications;

/// <summary>
/// Decide a quién y qué avisar, y deja todo en la bitácora. Cada aviso lleva una clave por día,
/// de modo que correr las revisiones varias veces no manda el mismo mensaje dos veces.
/// </summary>
public sealed class NotificationService(
    IDbContextFactory<ApplicationDbContext> factory,
    IEnumerable<INotificationChannel> channels,
    IDashboardService dashboard,
    IProjectService projects,
    ISprintService sprints,
    IInventoryService inventory,
    IDateTimeProvider clock,
    ILogger<NotificationService> logger) : INotificationService
{
    private sealed record Recipient(string UserId, string Name, string? Email, string? Phone);

    private readonly List<INotificationChannel> _channels = channels.ToList();

    public IReadOnlyList<ChannelStatus> Channels() =>
        _channels.Select(c => new ChannelStatus(c.Kind, c.IsConfigured, c.MissingSetup)).ToList();

    public async Task<List<NotificationRule>> RulesAsync(CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return (await db.NotificationRules.AsNoTracking().ToListAsync(ct)).OrderBy(r => (int)r.Type).ToList();
    }

    public async Task SaveRuleAsync(NotificationType type, bool enabled, bool sendEmail, bool sendWhatsApp, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var rule = await db.NotificationRules.FirstOrDefaultAsync(r => r.Type == type, ct)
            ?? db.NotificationRules.Add(new NotificationRule { Type = type }).Entity;
        rule.Enabled = enabled;
        rule.SendEmail = sendEmail;
        rule.SendWhatsApp = sendWhatsApp;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<NotificationLog>> LogAsync(int take = 200, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await db.NotificationLogs.AsNoTracking().OrderByDescending(l => l.Id).Take(take).ToListAsync(ct);
    }

    // ---------- Avisos por evento ----------

    public async Task<int> NotifyTaskAssignedAsync(int taskId, string? assignedByUserId = null, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var task = await db.Tasks.AsNoTracking().Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task?.AssigneeUserId is null || task.AssigneeUserId == assignedByUserId)
        {
            return 0;
        }

        var recipients = await RecipientsAsync(db, [task.AssigneeUserId], ct);
        var due = task.DueDate is null ? "sin fecha límite" : $"vence el {task.DueDate:dd/MM/yyyy}";
        return await DispatchAsync(NotificationType.TaskAssigned, recipients,
            "Nueva tarea asignada",
            $"Se le asignó la tarea \"{task.Title}\" del proyecto {task.Project?.Name} ({due}).",
            $"TaskAssigned:{task.Id}:{task.AssigneeUserId}", task.ProjectId, ct);
    }

    public async Task<int> RunChecksAsync(CancellationToken ct = default)
    {
        var sent = 0;
        var today = clock.Today;
        await using (var db = factory.CreateDbContext())
        {
            var open = await db.Tasks.AsNoTracking().Include(t => t.Project)
                .Where(t => t.Status != TaskItemStatus.Done && t.DueDate != null && t.AssigneeUserId != null
                            && t.Project!.Status != ProjectStatus.Closed)
                .ToListAsync(ct);

            foreach (var task in open.Where(t => t.DueDate == today.AddDays(1)))
            {
                var recipients = await RecipientsAsync(db, [task.AssigneeUserId!], ct);
                sent += await DispatchAsync(NotificationType.TaskDueSoon, recipients, "Tarea por vencer",
                    $"La tarea \"{task.Title}\" ({task.Project?.Name}) vence mañana, {task.DueDate:dd/MM/yyyy}.",
                    $"TaskDueSoon:{task.Id}:{today:yyyyMMdd}", task.ProjectId, ct);
            }

            foreach (var task in open.Where(t => t.DueDate < today))
            {
                var days = today.DayNumber - task.DueDate!.Value.DayNumber;
                var recipients = await RecipientsAsync(db, [task.AssigneeUserId!], ct);
                sent += await DispatchAsync(NotificationType.TaskOverdue, recipients, "Tarea vencida",
                    $"La tarea \"{task.Title}\" ({task.Project?.Name}) está vencida desde hace {days} día(s) (límite {task.DueDate:dd/MM/yyyy}).",
                    $"TaskOverdue:{task.Id}:{today:yyyyMMdd}", task.ProjectId, ct);
            }
        }

        foreach (var project in (await projects.ListAsync(ct)).Where(p => p.Status == ProjectStatus.Active))
        {
            sent += await CheckSprintAsync(project, today, ct);
            sent += await CheckStockAsync(project, today, ct);
        }

        sent += await CheckProjectsBehindAsync(today, ct);
        return sent;
    }

    private async Task<int> CheckSprintAsync(Project project, DateOnly today, CancellationToken ct)
    {
        var sent = 0;
        foreach (var sprint in (await sprints.ListAsync(project.Id, ct)).Where(s => s.Sprint.Status == SprintStatus.Active && s.Pace.Status == PaceStatus.Behind))
        {
            await using var db = factory.CreateDbContext();
            var recipients = await RoleRecipientsAsync(db, [AppRoles.Admin, AppRoles.Supervisor, AppRoles.Gerencia], ct);
            sent += await DispatchAsync(NotificationType.SprintBehind, recipients, "Sprint atrasado",
                $"El {sprint.Sprint.Name} de {project.Name} va atrasado: {sprint.ProgressPercent:0.#}% hecho contra {sprint.Pace.ElapsedPercent:0.#}% del tiempo ({Math.Abs(sprint.Pace.DaysDelta)} día(s)).",
                $"SprintBehind:{sprint.Sprint.Id}:{today:yyyyMMdd}", project.Id, ct);
        }

        return sent;
    }

    private async Task<int> CheckProjectsBehindAsync(DateOnly today, CancellationToken ct)
    {
        var sent = 0;
        var data = await dashboard.GetAsync(ct);
        foreach (var row in data.Projects.Where(r => r.Pace.Status == PaceStatus.Behind))
        {
            await using var db = factory.CreateDbContext();
            var recipients = await RoleRecipientsAsync(db, [AppRoles.Admin, AppRoles.Supervisor, AppRoles.Gerencia], ct);
            sent += await DispatchAsync(NotificationType.ProjectBehind, recipients, "Proyecto atrasado",
                $"El proyecto {row.Project.Name} va atrasado: {row.Progress:0.#}% de avance contra {row.Pace.ElapsedPercent:0.#}% del tiempo ({Math.Abs(row.Pace.DaysDelta)} día(s)).",
                $"ProjectBehind:{row.Project.Id}:{today:yyyyMMdd}", row.Project.Id, ct);
        }

        return sent;
    }

    private async Task<int> CheckStockAsync(Project project, DateOnly today, CancellationToken ct)
    {
        var low = (await inventory.StockAsync(project.Id, null, ct))
            .Where(r => r.Status is StockStatus.Low or StockStatus.Negative)
            .OrderBy(r => r.Material.Name)
            .ToList();
        if (low.Count == 0)
        {
            return 0;
        }

        var lines = low.Take(10).Select(r => $"- {r.Material.Name}: {r.Stock:0.###} (mínimo {r.Material.MinimumStock:0.###})");
        var more = low.Count > 10 ? $"\n… y {low.Count - 10} más." : string.Empty;
        await using var db = factory.CreateDbContext();
        var recipients = await RoleRecipientsAsync(db, [AppRoles.Admin, AppRoles.Bodega], ct);
        return await DispatchAsync(NotificationType.LowStock, recipients, "Stock bajo en bodega",
            $"{project.Name}: {low.Count} material(es) con stock bajo.\n{string.Join('\n', lines)}{more}",
            $"LowStock:{project.Id}:{today:yyyyMMdd}", project.Id, ct);
    }

    public async Task<int> SendDailySummaryAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var data = await dashboard.GetAsync(ct);
        var behind = data.Projects.Count(r => r.Pace.Status == PaceStatus.Behind);

        await using var db = factory.CreateDbContext();
        var dueToday = await db.Tasks.AsNoTracking()
            .CountAsync(t => t.Status != TaskItemStatus.Done && t.DueDate == today && t.Project!.Status != ProjectStatus.Closed, ct);

        var body = $"Resumen del {today:dd/MM/yyyy}:\n" +
                   $"- Proyectos activos: {data.ActiveProjects} ({behind} atrasado(s))\n" +
                   $"- Tareas vencidas: {data.OverdueTasks}\n" +
                   $"- Tareas que vencen hoy: {dueToday}\n" +
                   $"- Tareas de esta semana: {data.TasksThisWeek} ({data.TasksThisWeekDone} completadas)\n" +
                   $"- Materiales con stock bajo: {data.LowStockMaterials}";

        var recipients = await RoleRecipientsAsync(db, [AppRoles.Admin, AppRoles.Supervisor, AppRoles.Gerencia], ct);
        return await DispatchAsync(NotificationType.DailySummary, recipients, "Resumen diario TEK-COM", body,
            $"DailySummary:{today:yyyyMMdd}", null, ct);
    }

    public async Task<NotificationLog> SendTestAsync(string userId, NotificationChannelKind channel, CancellationToken ct = default)
    {
        var sender = _channels.FirstOrDefault(c => c.Kind == channel)
            ?? throw new BusinessRuleException("El canal no existe.");
        if (!sender.IsConfigured)
        {
            throw new BusinessRuleException($"El canal no está configurado. {sender.MissingSetup}");
        }

        await using var db = factory.CreateDbContext();
        var recipient = (await RecipientsAsync(db, [userId], ct)).FirstOrDefault()
            ?? throw new BusinessRuleException("El usuario no existe.");
        var address = channel == NotificationChannelKind.Email ? recipient.Email : recipient.Phone;
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new BusinessRuleException(channel == NotificationChannelKind.Email
                ? "El usuario no tiene correo."
                : "El usuario no tiene teléfono registrado.");
        }

        var log = await SendAndLogAsync(db, sender, NotificationType.DailySummary, recipient, address,
            "Prueba de notificación", "Este es un mensaje de prueba de TEK-COM Gestión.",
            $"Test:{Guid.NewGuid():N}", null, ct);
        return log;
    }

    // ---------- Envío ----------

    private async Task<int> DispatchAsync(NotificationType type, List<Recipient> recipients, string subject, string body,
        string dedupeKey, int? projectId, CancellationToken ct)
    {
        await using var db = factory.CreateDbContext();
        var rule = await db.NotificationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Type == type, ct);
        if (rule is not { Enabled: true })
        {
            return 0;
        }

        var sent = 0;
        foreach (var channel in _channels.Where(c => c.IsConfigured))
        {
            var allowed = channel.Kind == NotificationChannelKind.Email ? rule.SendEmail : rule.SendWhatsApp;
            if (!allowed)
            {
                continue;
            }

            foreach (var recipient in recipients)
            {
                var address = channel.Kind == NotificationChannelKind.Email ? recipient.Email : recipient.Phone;
                if (string.IsNullOrWhiteSpace(address))
                {
                    continue;
                }

                var key = $"{dedupeKey}:{recipient.UserId}:{channel.Kind}";
                if (await db.NotificationLogs.AnyAsync(l => l.DedupeKey == key, ct))
                {
                    continue;
                }

                var log = await SendAndLogAsync(db, channel, type, recipient, address, subject, body, key, projectId, ct);
                if (log.Status == NotificationStatus.Sent)
                {
                    sent++;
                }
            }
        }

        return sent;
    }

    private async Task<NotificationLog> SendAndLogAsync(ApplicationDbContext db, INotificationChannel channel, NotificationType type,
        Recipient recipient, string address, string subject, string body, string key, int? projectId, CancellationToken ct)
    {
        var log = new NotificationLog
        {
            Type = type, Channel = channel.Kind, UserId = recipient.UserId, Recipient = address,
            Subject = subject, Body = Trim(body, 2000), DedupeKey = key, ProjectId = projectId,
        };
        try
        {
            await channel.SendAsync(address, subject, body, ct);
            log.Status = NotificationStatus.Sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Status = NotificationStatus.Failed;
            log.Error = Trim(ex.Message, 500);
            logger.LogWarning(ex, "No se pudo enviar el aviso {Type} por {Channel} a {Recipient}", type, channel.Kind, address);
        }

        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
        return log;
    }

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // ---------- Destinatarios ----------

    private static async Task<List<Recipient>> RecipientsAsync(ApplicationDbContext db, IEnumerable<string> userIds, CancellationToken ct)
    {
        var ids = userIds.ToList();
        return await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new Recipient(u.Id, u.FullName, u.Email, u.PhoneNumber))
            .ToListAsync(ct);
    }

    private static async Task<List<Recipient>> RoleRecipientsAsync(ApplicationDbContext db, string[] roles, CancellationToken ct)
    {
        var ids = await (from ur in db.UserRoles
                         join r in db.Roles on ur.RoleId equals r.Id
                         where roles.Contains(r.Name!)
                         select ur.UserId).Distinct().ToListAsync(ct);
        return await RecipientsAsync(db, ids, ct);
    }
}
