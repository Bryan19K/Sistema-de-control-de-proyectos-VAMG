using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

public enum ExpenseKind
{
    /// <summary>Compra con proveedor (transporte, equipo, mobiliario, material…). Lleva IVA aparte.</summary>
    Purchase = 0,

    /// <summary>Gasto de caja chica con factura (DTE).</summary>
    PettyCash = 1,

    /// <summary>Viáticos de supervisores y técnicos.</summary>
    Travel = 2,
}

/// <summary>Fondo de caja chica o de viáticos: una entrega de dinero con su monto asignado.</summary>
public class ExpenseFund : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public ExpenseKind Kind { get; set; }
    public decimal AssignedAmount { get; set; }
    public string SourceKey { get; set; } = string.Empty;
}

public class ExpenseEntry : AuditableEntity
{
    public ExpenseKind Kind { get; set; }

    public int? FundId { get; set; }
    public ExpenseFund? Fund { get; set; }

    public DateOnly Date { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>TRANSPORTE, EQUIPO, MOBILIARIO, SEGURIDAD, ALQUILER, MATERIAL…</summary>
    public string? Category { get; set; }
    public string? Supplier { get; set; }
    public string? InvoiceNumber { get; set; }
    public string? Unit { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? UnitCost { get; set; }

    /// <summary>Sin IVA (solo compras). En caja chica y viáticos el monto ya es el total.</summary>
    public decimal? Subtotal { get; set; }
    public decimal? Vat { get; set; }
    public decimal Total { get; set; }

    public string? Status { get; set; }
    public string? PaidBy { get; set; }

    /// <summary>Centro de costos como viene en el Excel (2601-2607). Si coincide con el código de un proyecto, se enlaza.</summary>
    public string? CostCenter { get; set; }
    public int? ProjectId { get; set; }
    public Project? Project { get; set; }

    /// <summary>Identifica la fila del Excel para no duplicarla al importar otra vez.</summary>
    public string SourceKey { get; set; } = string.Empty;
}

/// <summary>Precio target por contratista de un rubro (ODN o feeder).</summary>
public class PriceItem : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public string Section { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Unit { get; set; }
    public decimal TargetPrice { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Orden de compra de un nodo (o del feeder): valor, brigada asignada y estado de liquidación.</summary>
public class ContractOrder : AuditableEntity
{
    public int ProjectId { get; set; }
    public Project? Project { get; set; }

    public int? SiteId { get; set; }
    public Site? Site { get; set; }

    /// <summary>SON01, FEEDER, "SON17 / 18".</summary>
    public string Code { get; set; } = string.Empty;
    public string? OrderNumber { get; set; }
    public int? HomesPassed { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public string? ContractorName { get; set; }
    public string? MaterialStatus { get; set; }
    public string? ConstructionStatus { get; set; }
    public string? LiquidationStatus { get; set; }
}
