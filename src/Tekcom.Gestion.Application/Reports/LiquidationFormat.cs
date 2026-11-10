namespace Tekcom.Gestion.Application.Reports;

/// <summary>
/// Una línea de la liquidación en el formato oficial de la empresa: material, grupo (CL - NEXTWAVE o TEKCOM),
/// entregado por almacén, utilizado, devolución y lo que se debe.
/// </summary>
public sealed record LiquidationLine(string? Code, string Name, string Group, string Unit, decimal Delivered, decimal Used, decimal Returned, string? Observation)
{
    /// <summary>TOTAL A DEVOLVER = entregado − utilizado (negativo si se usó más de lo entregado).</summary>
    public decimal ToReturn => Delivered - Used;

    /// <summary>DEBE = total a devolver − devolución.</summary>
    public decimal Owed => ToReturn - Returned;
}

/// <summary>La liquidación de un nodo: una hoja del Excel oficial.</summary>
public sealed record LiquidationSheet(int SiteId, string NodeCode, string ProjectName, string? Contractor, bool Closed, string? Observations, List<LiquidationLine> Lines);

/// <summary>Arma la liquidación de los nodos con lo que ya registra el sistema y la exporta en el formato oficial.</summary>
public interface ILiquidationFormatService
{
    /// <summary>Una hoja por nodo con materiales entregados, utilizados o devueltos. Con <paramref name="siteId"/> solo ese nodo.</summary>
    Task<List<LiquidationSheet>> SheetsAsync(int projectId, int? siteId, CancellationToken ct = default);

    /// <summary>Excel con el formato oficial LIQUIDACION_DE_NODOS_FTTH: una hoja por nodo.</summary>
    byte[] ToOfficialExcel(IReadOnlyList<LiquidationSheet> sheets);
}

/// <summary>Excel con las tablas del modelo de Power BI del tablero de despliegue (hechos y dimensiones), armado con los datos del sistema.</summary>
public interface IPowerBiExportService
{
    Task<byte[]> BuildAsync(int projectId, CancellationToken ct = default);
}
