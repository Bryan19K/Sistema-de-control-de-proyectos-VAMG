using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Documento (ProjectDocument) adjunto a un proyecto, un nodo y/o una tarea (ATP, CAES, minutas firmadas, fotos...).</summary>
public class ProjectDocument : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public int? TaskItemId { get; set; }
    public TaskItem? TaskItem { get; set; }

    public DocumentKind Kind { get; set; } = DocumentKind.Other;
    public string Title { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }

    /// <summary>El documento adjunto es la copia firmada.</summary>
    public bool IsSigned { get; set; }
    public string? Notes { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Nombre con el que quedó guardado el archivo en el almacenamiento.</summary>
    public string StoredName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
}

/// <summary>Minuta de una reunión del proyecto, con sus pendientes.</summary>
public class Minute : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateOnly Date { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Attendees { get; set; }
    public string? Summary { get; set; }

    public ICollection<MinuteItem> Items { get; set; } = [];
}

/// <summary>Pendiente (acuerdo) de una minuta, con responsable, fecha y nodo opcionales.</summary>
public class MinuteItem : AuditableEntity
{
    public int MinuteId { get; set; }
    public Minute? Minute { get; set; }

    public string Description { get; set; } = string.Empty;
    public string? ResponsibleUserId { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public DateOnly? DueDate { get; set; }
    public bool IsDone { get; set; }
    public DateOnly? DoneOn { get; set; }
}
