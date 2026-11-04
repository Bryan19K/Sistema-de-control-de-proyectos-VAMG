using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>Fase de un proyecto de edificio (Ingeniería previa y sus procesos, Construcción) con su ponderación y avance.</summary>
public class ProjectPhase : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Si es un proceso de otra fase (Levantamiento dentro de Ingeniería previa).</summary>
    public string? ParentName { get; set; }

    /// <summary>Peso dentro de su nivel, en porcentaje (30, 40, 60…).</summary>
    public decimal Weight { get; set; }

    /// <summary>Avance de 0 a 100.</summary>
    public decimal Percent { get; set; }

    public int SortOrder { get; set; }
}

public enum BuildingRecordKind
{
    /// <summary>Apartamento: seguimiento de mano de obra, fusiones, etiquetado y certificación.</summary>
    Unit = 0,

    /// <summary>Infraestructura del edificio (ODF, gabinete, NAP, feeder, cierres).</summary>
    Infrastructure = 1,

    /// <summary>Resumen de un nivel del edificio.</summary>
    Level = 2,
}

/// <summary>
/// Línea de seguimiento de un proyecto de edificio. Se guarda lo principal en columnas y el resto del Excel
/// (columna → valor) en <see cref="DetailJson"/> para mostrarlo sin perder nada.
/// </summary>
public class BuildingRecord : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public BuildingRecordKind Kind { get; set; }

    /// <summary>Nivel o piso (texto, por si trae "Sótano" o "PB").</summary>
    public string? Level { get; set; }

    /// <summary>Apartamento, rubro de infraestructura o "Nivel 3".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Avance de 0 a 100 (null si no aplica).</summary>
    public decimal? ProgressPercent { get; set; }

    public string? Status { get; set; }
    public string? Notes { get; set; }
    public string? DetailJson { get; set; }
    public int SortOrder { get; set; }
}
