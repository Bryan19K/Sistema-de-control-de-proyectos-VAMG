using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Application.Models;

public readonly record struct QuantityPair(decimal Projected, decimal Installed)
{
    public decimal Pending => Projected - Installed;
    public decimal Percent => Projected <= 0 ? 0 : Math.Round(Installed / Projected * 100m, 1);
}

/// <summary>Cantidades de un material en un nodo: proyectado vigente, entregado, instalado y devuelto.</summary>
public sealed class MaterialQuantity
{
    public required Material Material { get; init; }

    /// <summary>Cantidad de la última versión de la proyección de diseño.</summary>
    public decimal DesignProjected { get; init; }

    /// <summary>Suma de los ajustes (suma o resta) al proyectado.</summary>
    public decimal Adjustments { get; init; }
    public decimal Delivered { get; init; }
    public decimal Installed { get; init; }
    public decimal Returned { get; init; }

    /// <summary>Proyectado vigente = diseño ± ajustes.</summary>
    public decimal Projected => DesignProjected + Adjustments;

    /// <summary>Sobrante = entregado − instalado.</summary>
    public decimal Surplus => Delivered - Installed;

    /// <summary>Pendiente por devolver = sobrante − devuelto.</summary>
    public decimal PendingReturn => Surplus - Returned;

    /// <summary>Variación = instalado − proyectado.</summary>
    public decimal Variation => Installed - Projected;

    public decimal VariationPercent => Projected == 0 ? 0 : Math.Round(Variation / Projected * 100m, 1);
}

public sealed class SiteSummary
{
    public required Site Site { get; init; }
    public decimal ProgressPercent { get; init; }
    public required PaceResult Pace { get; init; }
    public required IReadOnlyDictionary<StageCode, StageStatus> Stages { get; init; }
    public required IReadOnlyDictionary<QuantityGroup, QuantityPair> Quantities { get; init; }

    /// <summary>Avance de FATs (sub-box, end-box, Hub-box y X-box) como en el Excel.</summary>
    public decimal FatPercent { get; init; }

    /// <summary>Con el % de FATs instaladas definido en el proyecto y sin mediciones finalizadas.</summary>
    public bool ReadyToMeasure { get; init; }
}

public sealed record UserListItem(
    string Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool IsLockedOut);

public sealed class StockRow
{
    public required Material Material { get; init; }
    public decimal Received { get; init; }
    public decimal Delivered { get; init; }
    public decimal Returned { get; init; }

    /// <summary>Total instalado en campo (nodos y feeder). No afecta el stock de bodega.</summary>
    public decimal Installed { get; init; }

    /// <summary>Suma neta de los ajustes (positivos y negativos).</summary>
    public decimal AdjustmentNet { get; init; }

    /// <summary>Total proyectado de todos los nodos del proyecto (diseño ± ajustes).</summary>
    public decimal ProjectedTotal { get; init; }

    /// <summary>Stock = ingresos − entregas + devoluciones ± ajustes. Nunca se guarda: se calcula.</summary>
    public decimal Stock => Received - Delivered + Returned + AdjustmentNet;

    /// <summary>Lo que falta recibir para cubrir el diseño.</summary>
    public decimal Shortage => Math.Max(0, ProjectedTotal - Received);

    public StockStatus Status =>
        Stock < 0 ? StockStatus.Negative
        : Shortage > 0 ? StockStatus.Shortage
        : Material.MinimumStock > 0 && Stock <= Material.MinimumStock ? StockStatus.Low
        : StockStatus.Ok;
}

public sealed class MovementFilter
{
    public int ProjectId { get; set; }
    public MovementType? Type { get; set; }
    public int? MaterialId { get; set; }
    public int? SiteId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public bool IncludeVoided { get; set; } = true;
    public int Take { get; set; } = 500;
}

public sealed class LiquidationRow
{
    public required MaterialQuantity Quantity { get; init; }
    public decimal Justified { get; init; }

    /// <summary>Pendiente por devolver que todavía no está justificado.</summary>
    public decimal Unresolved => Math.Max(0, Quantity.PendingReturn - Justified);
}

public sealed class LiquidationView
{
    public required Liquidation Liquidation { get; init; }
    public required Site Site { get; init; }
    public required List<LiquidationRow> Rows { get; init; }
    public required List<LiquidationJustification> Justifications { get; init; }

    public bool IsClosed => Liquidation.Status == LiquidationStatus.Closed;
    public decimal TotalSurplus => Rows.Sum(r => Math.Max(0, r.Quantity.Surplus));
    public decimal TotalPending => Rows.Sum(r => Math.Max(0, r.Quantity.PendingReturn));
    public decimal TotalUnresolved => Rows.Sum(r => r.Unresolved);
    public int WithVariation => Rows.Count(r => r.Quantity.Projected > 0 && r.Quantity.Variation != 0);

    /// <summary>
    /// Precisión de diseño: 100 − (Σ|instalado − proyectado| ÷ Σ proyectado × 100), entre 0 y 100.
    /// Solo cuenta los materiales que tienen proyección (lo instalado sin proyectar no entra en la medida).
    /// </summary>
    public decimal DesignAccuracy
    {
        get
        {
            var projectedRows = Rows.Where(r => r.Quantity.Projected > 0).ToList();
            var projected = projectedRows.Sum(r => r.Quantity.Projected);
            if (projected <= 0)
            {
                return 0;
            }

            var deviation = projectedRows.Sum(r => Math.Abs(r.Quantity.Installed - r.Quantity.Projected));
            return Math.Clamp(Math.Round(100m - deviation / projected * 100m, 0), 0, 100);
        }
    }

    public bool CanClose => !IsClosed && TotalUnresolved == 0;

    public string? BlockingMessage
    {
        get
        {
            if (IsClosed || TotalUnresolved == 0)
            {
                return null;
            }

            var details = string.Join(", ", Rows.Where(r => r.Unresolved > 0)
                .Select(r => $"{r.Quantity.Material.Name} {r.Unresolved:0.###}"));
            return $"No se puede cerrar el nodo: hay {TotalUnresolved:0.###} unidades pendientes por devolver o justificar ({details}).";
        }
    }
}

public sealed record MeasurementSummary(int Measured, int Acceptable, int NotAcceptable);
