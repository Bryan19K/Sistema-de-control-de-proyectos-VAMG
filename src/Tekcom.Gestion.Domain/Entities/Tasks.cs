using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Sprint de un proyecto. Su duración es libre: se define con fecha de inicio y de fin.</summary>
public class Sprint : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public SprintStatus Status { get; set; } = SprintStatus.Planned;
    public DateOnly? ClosedOn { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = [];
}

/// <summary>Tarea. Sin sprint (<see cref="SprintId"/> nulo) está en el backlog.</summary>
public class TaskItem : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? SprintId { get; set; }
    public Sprint? Sprint { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Id del usuario responsable (ASP.NET Identity).</summary>
    public string? AssigneeUserId { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public TaskItemStatus Status { get; set; } = TaskItemStatus.ToDo;
    public DateOnly? DueDate { get; set; }
    public DateOnly? CompletedOn { get; set; }

    /// <summary>Orden dentro de su columna del tablero.</summary>
    public int SortOrder { get; set; }

    // Vínculos opcionales
    public int? SiteId { get; set; }
    public Site? Site { get; set; }
    public int? MaterialId { get; set; }
    public Material? Material { get; set; }

    public ICollection<TaskChecklistItem> Checklist { get; set; } = [];
    public ICollection<TaskComment> Comments { get; set; } = [];
    public ICollection<TaskAttachment> Attachments { get; set; } = [];
}

public class TaskChecklistItem : AuditableEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public string Text { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Comentario de una tarea. Quién y cuándo lo escribió queda en los campos de auditoría.</summary>
public class TaskComment : AuditableEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public string Text { get; set; } = string.Empty;
}

public class TaskAttachment : AuditableEntity
{
    public int TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Nombre con el que se guardó el archivo en el almacenamiento.</summary>
    public string StoredName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
}
