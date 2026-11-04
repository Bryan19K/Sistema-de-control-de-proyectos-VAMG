using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Foto diaria del avance de un proyecto: permite graficar la curva real día a día y comparar semana contra semana.</summary>
public class ProgressSnapshot : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public DateOnly Date { get; set; }
    public decimal ProgressPercent { get; set; }
    public decimal ElapsedPercent { get; set; }

    public int TotalSites { get; set; }
    public int SitesFinished { get; set; }
    public int SitesReady { get; set; }
    public int TasksTotal { get; set; }
    public int TasksDone { get; set; }
}
