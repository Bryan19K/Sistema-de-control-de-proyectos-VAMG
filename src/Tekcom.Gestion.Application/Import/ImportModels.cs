using Tekcom.Gestion.Domain.Enums;

namespace Tekcom.Gestion.Application.Import;

public enum ImportSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Un problema detectado al leer el Excel. Los errores dejan la fila fuera de la importación.</summary>
public sealed record ImportIssue(ImportSeverity Severity, string Sheet, int? Row, string? Column, string Message);

/// <summary>Comparación entre el total que trae el Excel y el que calcula el sistema.</summary>
public sealed record TotalCheck(string Name, decimal ExcelValue, decimal SystemValue)
{
    public bool Matches => ExcelValue == SystemValue;
}

public sealed record ImportTarget(int? ProjectId, string? NewProjectCode, string? NewProjectName);

public sealed class ImportCounts
{
    public int SitesCreated { get; set; }
    public int SitesUpdated { get; set; }
    public int ContractorsCreated { get; set; }
    public int SupervisorsCreated { get; set; }
    public int MaterialsCreated { get; set; }
    public int ProjectionVersionsCreated { get; set; }
    public int InstalledMovementsCreated { get; set; }
    public int FeederLinesSaved { get; set; }
    public int RowsSkipped { get; set; }
}

public sealed class ImportReport
{
    public string FileName { get; set; } = string.Empty;
    public bool Applied { get; set; }
    public List<ImportIssue> Issues { get; } = [];
    public List<TotalCheck> TotalChecks { get; } = [];
    public ImportCounts Counts { get; } = new();
    public List<string> NodeCodes { get; } = [];
    public string? SuggestedProjectCode { get; set; }
    public string? SuggestedProjectName { get; set; }
    public string? SuggestedClient { get; set; }

    public int Errors => Issues.Count(i => i.Severity == ImportSeverity.Error);
    public int Warnings => Issues.Count(i => i.Severity == ImportSeverity.Warning);
    public bool TotalsMatch => TotalChecks.All(c => c.Matches);
}

/// <summary>Resultado de leer el Excel, listo para confirmar e importar.</summary>
public sealed class ImportAnalysis
{
    public required ImportReport Report { get; init; }
    public required ParsedWorkbook Workbook { get; init; }
}

/// <summary>Total de una columna (o grupo) tal como lo trae la fila TOTAL del Excel.</summary>
public sealed record ParsedTotal(string Name, QuantityGroup Group, bool IsProjected, decimal Value);

public sealed class ParsedWorkbook
{
    public string? ProjectTitle { get; set; }
    public List<ParsedNode> Nodes { get; } = [];
    public ParsedFeeder? Feeder { get; set; }
    public List<ParsedTotal> Totals { get; } = [];
}

public sealed class ParsedQuantity
{
    /// <summary>Nombre como viene en el Excel (se resuelve contra el catálogo y sus alias).</summary>
    public required string MaterialName { get; init; }
    public MaterialCategory Category { get; init; } = MaterialCategory.Other;
    public QuantityGroup Group { get; init; } = QuantityGroup.None;
    public string Unit { get; init; } = "UND";
    public int? LengthMeters { get; init; }
    public decimal? Projected { get; set; }
    public decimal? Installed { get; set; }
}

public sealed class ParsedNode
{
    public int Row { get; init; }
    public required string Code { get; init; }
    public string? Zone { get; init; }
    public string? DataStatus { get; init; }
    public string? Brigade { get; init; }
    public string? Phone { get; init; }
    public DateOnly? Start { get; init; }
    public DateOnly? End { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? Operator { get; init; }
    public string? Supervisor { get; init; }
    public string? Notes { get; init; }
    public List<ParsedQuantity> Quantities { get; } = [];
    public Dictionary<StageCode, StageStatus> Stages { get; } = [];

    /// <summary>Texto que el usuario escribió donde iba una cantidad (se guarda como nota).</summary>
    public List<string> Remarks { get; } = [];
    public bool HasErrors { get; set; }
}

public sealed class ParsedFeeder
{
    public string? Brigade { get; init; }

    /// <summary>Totales de metros que trae el Excel (para validar).</summary>
    public decimal? ExcelPlannedMeters { get; set; }
    public decimal? ExcelInstalledMeters { get; set; }
    public Dictionary<FeederItemKind, (decimal Planned, decimal Installed)> Lines { get; } = [];
    public List<ParsedQuantity> Materials { get; } = [];
}

public interface IExcelImportService
{
    /// <summary>Lee y valida el Excel sin escribir nada en la base.</summary>
    Task<ImportAnalysis> AnalyzeAsync(Stream file, string fileName, CancellationToken ct = default);

    /// <summary>Importa lo que no tiene errores y valida los totales contra el Excel.</summary>
    Task<ImportReport> ApplyAsync(ImportAnalysis analysis, ImportTarget target, CancellationToken ct = default);
}
