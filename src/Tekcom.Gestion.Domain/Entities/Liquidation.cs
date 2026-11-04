using Tekcom.Gestion.Domain.Common;
using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>
/// Liquidación de materiales de un nodo. No se puede cerrar con material pendiente de devolver
/// que no esté justificado.
/// </summary>
public class Liquidation : AuditableEntity
{
    public int ProjectId { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    public LiquidationStatus Status { get; set; } = LiquidationStatus.Open;
    public string? Observations { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public string? ClosedBy { get; set; }

    public ICollection<LiquidationJustification> Justifications { get; set; } = [];
}

/// <summary>Material pendiente de devolver que se justifica (por ejemplo, merma o pérdida).</summary>
public class LiquidationJustification : AuditableEntity
{
    public int LiquidationId { get; set; }
    public Liquidation? Liquidation { get; set; }

    public int MaterialId { get; set; }
    public Material? Material { get; set; }

    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Medición de potencia de una FAT. Se guarda cada medición; la última por FAT es la vigente.</summary>
public class FatMeasurement : AuditableEntity
{
    public int ProjectId { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }

    public string FatCode { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public decimal PowerDbm { get; set; }
    public bool IsAcceptable { get; set; }
    public string? Notes { get; set; }
}
