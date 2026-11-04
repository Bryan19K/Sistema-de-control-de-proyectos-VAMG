using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

public class WorkPlanPhase : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public ICollection<WorkPlanActivity> Activities { get; set; } = [];
}

public class WorkPlanActivity : AuditableEntity
{
    public int ProjectId { get; set; }
    public int PhaseId { get; set; }
    public WorkPlanPhase? Phase { get; set; }

    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }

    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public DateOnly? ActualStart { get; set; }
    public DateOnly? ActualEnd { get; set; }

    /// <summary>Avance manual (0 a 100). Si la actividad está vinculada a un nodo, se usa el avance del nodo.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Un hito no tiene duración: marca una fecha (por ejemplo, la entrega al cliente).</summary>
    public bool IsMilestone { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public string? Notes { get; set; }

    public ICollection<ActivityDependency> Predecessors { get; set; } = [];
}

/// <summary>La actividad <see cref="ActivityId"/> depende de <see cref="PredecessorId"/>.</summary>
public class ActivityDependency : AuditableEntity
{
    public int ActivityId { get; set; }
    public WorkPlanActivity? Activity { get; set; }

    public int PredecessorId { get; set; }
    public WorkPlanActivity? Predecessor { get; set; }
}

/// <summary>Plantilla de plan de trabajo: fases y actividades con fechas relativas al inicio del proyecto.</summary>
public class PlanTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Estructura en JSON (ver <c>PlanTemplateDefinition</c>).</summary>
    public string DefinitionJson { get; set; } = "{}";
    public bool IsSystem { get; set; }
}
