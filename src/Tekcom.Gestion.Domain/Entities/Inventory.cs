using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

public class Material : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Código en el ERP.</summary>
    public string? Code { get; set; }
    public string Unit { get; set; } = "UND";
    public MaterialCategory Category { get; set; } = MaterialCategory.Other;

    /// <summary>Rol del material en el flujo del nodo (cables, FATs, fusiones, cruces).</summary>
    public QuantityGroup Group { get; set; } = QuantityGroup.None;

    /// <summary>Largo en metros para cables preconectorizados.</summary>
    public int? LengthMeters { get; set; }

    /// <summary>Si es false (fusiones, cruces) se cuenta como partida pero no pasa por bodega.</summary>
    public bool TracksStock { get; set; } = true;

    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Notes { get; set; }

    public ICollection<MaterialAlias> Aliases { get; set; } = [];
}

/// <summary>Nombre alterno de un material (ej. SUB-BOX → NAP de paso).</summary>
public class MaterialAlias : AuditableEntity
{
    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    public string Alias { get; set; } = string.Empty;

    /// <summary>Clave normalizada para comparar sin importar mayúsculas, acentos ni símbolos.</summary>
    public string AliasKey { get; set; } = string.Empty;
}

/// <summary>Una versión de la proyección de diseño de un nodo.</summary>
public class DesignProjection : AuditableEntity
{
    public int ProjectId { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    public int Version { get; set; }
    public DateOnly Date { get; set; }
    public string? Source { get; set; }
    public string? Notes { get; set; }

    public ICollection<DesignProjectionLine> Lines { get; set; } = [];
}

public class DesignProjectionLine : AuditableEntity
{
    public int DesignProjectionId { get; set; }
    public DesignProjection? DesignProjection { get; set; }

    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    public decimal Quantity { get; set; }
}

/// <summary>Ajuste (suma o resta) a la cantidad proyectada de un material en un nodo. Siempre con motivo.</summary>
public class ProjectionAdjustment : AuditableEntity
{
    public int ProjectId { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    public decimal Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
}

/// <summary>
/// Movimiento de inventario. El stock nunca se guarda: se calcula sumando movimientos.
/// Nada se borra: se anula con motivo.
/// </summary>
public class InventoryMovement : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public MovementType Type { get; set; }

    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    /// <summary>Siempre positiva. En los ajustes, <see cref="IsIncrease"/> indica si suma o resta.</summary>
    public decimal Quantity { get; set; }
    public bool IsIncrease { get; set; } = true;

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    public int? ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    public DateOnly Date { get; set; }
    public string? Reference { get; set; }
    public string? Batch { get; set; }
    public string? Notes { get; set; }

    /// <summary>Saldo inicial cargado desde un Excel.</summary>
    public bool IsOpeningBalance { get; set; }

    /// <summary>Justificación cuando se permite dejar el stock en negativo.</summary>
    public string? NegativeStockJustification { get; set; }

    public bool IsVoided { get; set; }
    public string? VoidReason { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidedBy { get; set; }
}

/// <summary>Cantidad proyectada del feeder del proyecto (metros por tipo de fibra, cierres, ODF).</summary>
public class FeederLine : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public FeederItemKind Kind { get; set; }
    public decimal PlannedQuantity { get; set; }
    public int? ContractorId { get; set; }
    public Contractor? Contractor { get; set; }

    public ICollection<FeederEntry> Entries { get; set; } = [];
}

/// <summary>Instalación de feeder en un día.</summary>
public class FeederEntry : AuditableEntity
{
    public int FeederLineId { get; set; }
    public FeederLine? FeederLine { get; set; }

    public DateOnly Date { get; set; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public bool IsOpeningBalance { get; set; }
}
