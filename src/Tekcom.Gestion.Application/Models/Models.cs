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
