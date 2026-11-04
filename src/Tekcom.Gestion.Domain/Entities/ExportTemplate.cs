using Tekcom.Gestion.Domain.Common;

namespace Tekcom.Gestion.Domain.Entities;

/// <summary>
/// Plantilla de diseño de los exportes: colores, orientación, columnas visibles y pie.
/// El encabezado fijo de TEK-COM (logo, proyecto, fecha de corte y "Elaborado por") no se puede quitar.
/// </summary>
public class ExportTemplate : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Si se define, la plantilla solo aplica a ese reporte; si es nulo, a todos.</summary>
    public string? ReportKey { get; set; }

    public string HeaderColor { get; set; } = "#12304f";
    public string AccentColor { get; set; } = "#e8742c";
    public bool Landscape { get; set; } = true;
    public bool ZebraRows { get; set; } = true;
    public string? FooterNote { get; set; }

    /// <summary>Claves de las columnas que se ocultan, separadas por coma.</summary>
    public string? HiddenColumns { get; set; }

    /// <summary>La plantilla "Estándar TEK-COM" es del sistema y no se elimina.</summary>
    public bool IsSystem { get; set; }

    public IReadOnlySet<string> HiddenSet => (HiddenColumns ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
