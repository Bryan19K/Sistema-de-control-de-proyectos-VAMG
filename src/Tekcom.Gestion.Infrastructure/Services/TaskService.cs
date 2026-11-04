using Microsoft.EntityFrameworkCore;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Common.Interfaces;
using Tekcom.Gestion.Application.Notifications;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Services;

public sealed class TaskService(
    IDbContextFactory<ApplicationDbContext> factory,
    IFileStorage storage,
    IDateTimeProvider clock,
    INotificationDispatcher? notifier = null) : ITaskService
{
    private static IQueryable<TaskItem> WithDetails(IQueryable<TaskItem> query) => query
        .Include(t => t.Site)
        .Include(t => t.Material)
        .Include(t => t.Sprint)
        .Include(t => t.Project)
        .Include(t => t.Checklist)
        .Include(t => t.Attachments)
        .Include(t => t.Comments)
        .AsSplitQuery();

    public async Task<List<TaskItem>> ListAsync(TaskFilter filter, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = WithDetails(db.Tasks.AsNoTracking()).Where(t => t.ProjectId == filter.ProjectId);

        if (filter.BacklogOnly)
        {
            query = query.Where(t => t.SprintId == null);
        }
        else if (filter.SprintId is { } sprintId)
        {
            query = query.Where(t => t.SprintId == sprintId);
        }

        if (!string.IsNullOrWhiteSpace(filter.AssigneeUserId))
        {
            query = query.Where(t => t.AssigneeUserId == filter.AssigneeUserId);
        }

        if (filter.OnlyOpen)
        {
            query = query.Where(t => t.Status != TaskItemStatus.Done);
        }

        var list = await query.OrderBy(t => t.Status).ThenBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            list = list.Where(t => t.Title.Contains(filter.Search, StringComparison.OrdinalIgnoreCase)
                || (t.Description ?? string.Empty).Contains(filter.Search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return list;
    }

    public async Task<List<TaskItem>> MyTasksAsync(string userId, bool onlyOpen, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var query = WithDetails(db.Tasks.AsNoTracking()).Where(t => t.AssigneeUserId == userId);
        if (onlyOpen)
        {
            query = query.Where(t => t.Status != TaskItemStatus.Done);
        }

        var list = await query.ToListAsync(ct);
        return list
            .OrderBy(t => t.Status == TaskItemStatus.Done)
            .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(t => t.Priority)
            .ToList();
    }

    public async Task<TaskItem?> GetAsync(int taskId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        return await WithDetails(db.Tasks.AsNoTracking()).FirstOrDefaultAsync(t => t.Id == taskId, ct);
    }

    public async Task<TaskItem> SaveAsync(TaskItem task, CancellationToken ct = default)
    {
        var title = (task.Title ?? string.Empty).Trim();
        if (title.Length == 0)
        {
            throw new BusinessRuleException("El título de la tarea es obligatorio.");
        }

        await using var db = factory.CreateDbContext();
        if (!await db.Projects.AnyAsync(p => p.Id == task.ProjectId, ct))
        {
            throw new BusinessRuleException("El proyecto no existe.");
        }

        if (task.SprintId is { } sprintId)
        {
            var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == sprintId, ct)
                ?? throw new BusinessRuleException("El sprint no existe.");
            if (sprint.ProjectId != task.ProjectId)
            {
                throw new BusinessRuleException("El sprint no pertenece a este proyecto.");
            }

            if (sprint.Status == SprintStatus.Closed)
            {
                throw new BusinessRuleException("No se pueden agregar tareas a un sprint cerrado.");
            }
        }

        if (task.SiteId is { } siteId && !await db.Sites.AnyAsync(s => s.Id == siteId && s.ProjectId == task.ProjectId, ct))
        {
            throw new BusinessRuleException("El nodo no pertenece a este proyecto.");
        }

        var isNew = task.Id == 0;
        var entity = isNew
            ? db.Tasks.Add(new TaskItem { ProjectId = task.ProjectId }).Entity
            : await db.Tasks.FirstAsync(t => t.Id == task.Id, ct);

        var statusChanged = isNew || entity.Status != task.Status;
        var newAssignee = string.IsNullOrWhiteSpace(task.AssigneeUserId) ? null : task.AssigneeUserId;
        var assigneeChanged = newAssignee is not null && entity.AssigneeUserId != newAssignee;
        entity.Title = title;
        entity.Description = string.IsNullOrWhiteSpace(task.Description) ? null : task.Description.Trim();
        entity.SprintId = task.SprintId;
        entity.AssigneeUserId = string.IsNullOrWhiteSpace(task.AssigneeUserId) ? null : task.AssigneeUserId;
        entity.Priority = task.Priority;
        entity.DueDate = task.DueDate;
        entity.SiteId = task.SiteId;
        entity.MaterialId = task.MaterialId;
        entity.Status = task.Status;

        if (statusChanged)
        {
            entity.CompletedOn = entity.Status == TaskItemStatus.Done ? clock.Today : null;
            entity.SortOrder = await NextOrderAsync(db, entity, ct);
        }

        await db.SaveChangesAsync(ct);
        if (assigneeChanged)
        {
            notifier?.TaskAssigned(entity.Id);
        }

        return entity;
    }

    public async Task DeleteAsync(int taskId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is not null)
        {
            db.Tasks.Remove(task); // borrado lógico por el interceptor
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task MoveAsync(int taskId, TaskItemStatus status, int index, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new BusinessRuleException("La tarea no existe.");

        var column = await db.Tasks
            .Where(t => t.ProjectId == task.ProjectId && t.SprintId == task.SprintId && t.Status == status && t.Id != task.Id)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .ToListAsync(ct);

        if (task.Status != status)
        {
            task.Status = status;
            task.CompletedOn = status == TaskItemStatus.Done ? clock.Today : null;
        }

        column.Insert(Math.Clamp(index, 0, column.Count), task);
        for (var i = 0; i < column.Count; i++)
        {
            column[i].SortOrder = (i + 1) * 10;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task AssignToSprintAsync(int taskId, int? sprintId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new BusinessRuleException("La tarea no existe.");

        if (sprintId is { } id)
        {
            var sprint = await db.Sprints.FirstOrDefaultAsync(s => s.Id == id, ct)
                ?? throw new BusinessRuleException("El sprint no existe.");
            if (sprint.ProjectId != task.ProjectId)
            {
                throw new BusinessRuleException("El sprint no pertenece a este proyecto.");
            }

            if (sprint.Status == SprintStatus.Closed)
            {
                throw new BusinessRuleException("No se pueden agregar tareas a un sprint cerrado.");
            }
        }

        task.SprintId = sprintId;
        task.SortOrder = await NextOrderAsync(db, task, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task AddChecklistItemAsync(int taskId, string text, CancellationToken ct = default)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new BusinessRuleException("Escriba el texto del punto del checklist.");
        }

        await using var db = factory.CreateDbContext();
        var count = await db.TaskChecklistItems.CountAsync(i => i.TaskItemId == taskId, ct);
        db.TaskChecklistItems.Add(new TaskChecklistItem { TaskItemId = taskId, Text = text, SortOrder = count + 1 });
        await db.SaveChangesAsync(ct);
    }

    public async Task SetChecklistItemAsync(int itemId, bool done, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var item = await db.TaskChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is not null)
        {
            item.IsDone = done;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task RemoveChecklistItemAsync(int itemId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var item = await db.TaskChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId, ct);
        if (item is not null)
        {
            db.TaskChecklistItems.Remove(item);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task AddCommentAsync(int taskId, string text, CancellationToken ct = default)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            throw new BusinessRuleException("Escriba el comentario.");
        }

        await using var db = factory.CreateDbContext();
        db.TaskComments.Add(new TaskComment { TaskItemId = taskId, Text = text });
        await db.SaveChangesAsync(ct);
    }

    public async Task<TaskAttachment> AddAttachmentAsync(int taskId, string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        if (!await db.Tasks.AnyAsync(t => t.Id == taskId, ct))
        {
            throw new BusinessRuleException("La tarea no existe.");
        }

        var cleanName = Path.GetFileName(fileName);
        var stored = await storage.SaveAsync(content, cleanName, ct);
        var attachment = new TaskAttachment
        {
            TaskItemId = taskId,
            FileName = cleanName,
            StoredName = stored,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
        };

        var path = await storage.OpenReadAsync(stored, ct);
        attachment.Size = path?.Length ?? 0;
        path?.Dispose();

        db.TaskAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return attachment;
    }

    public async Task<(TaskAttachment Attachment, Stream Content)?> OpenAttachmentAsync(int attachmentId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var attachment = await db.TaskAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(attachment.StoredName, ct);
        return stream is null ? null : (attachment, stream);
    }

    public async Task RemoveAttachmentAsync(int attachmentId, CancellationToken ct = default)
    {
        await using var db = factory.CreateDbContext();
        var attachment = await db.TaskAttachments.FirstOrDefaultAsync(a => a.Id == attachmentId, ct);
        if (attachment is not null)
        {
            db.TaskAttachments.Remove(attachment); // el archivo se conserva en el almacenamiento
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<int> NextOrderAsync(ApplicationDbContext db, TaskItem task, CancellationToken ct)
    {
        var max = await db.Tasks
            .Where(t => t.ProjectId == task.ProjectId && t.SprintId == task.SprintId && t.Status == task.Status && t.Id != task.Id)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync(ct);
        return (max ?? 0) + 10;
    }
}
