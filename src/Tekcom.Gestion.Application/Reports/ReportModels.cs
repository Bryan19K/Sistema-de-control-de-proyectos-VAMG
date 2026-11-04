using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Application.Reports;

public enum ReportColumnType
{
    Text = 0,
    Integer = 1,
    Decimal = 2,
    Date = 3,
    Percent = 4,
}

public sealed record ReportColumn(string Key, string Header, ReportColumnType Type = ReportColumnType.Text, double Width = 1);

/// <summary>Una tabla del reporte. Los valores van tal cual (decimal, DateOnly o texto) para dar formato en cada archivo.</summary>
public sealed class ReportSection
{
    public string? Heading { get; init; }
    public required List<ReportColumn> Columns { get; init; }
    public List<object?[]> Rows { get; } = [];

    /// <summary>Fila de totales opcional: mismo largo que las columnas.</summary>
    public object?[]? Totals { get; set; }
}

/// <summary>Reporte listo para exportar. Lleva siempre los datos del encabezado fijo.</summary>
public sealed class ReportDocument
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string ProjectName { get; init; }
    public required DateOnly CutoffDate { get; init; }
    public required string PreparedBy { get; init; }
    public string? Subtitle { get; set; }

    /// <summary>Datos sueltos mostrados bajo el encabezado (por ejemplo, nodo y contratista del acta).</summary>
    public List<(string Label, string Value)> Facts { get; } = [];
    public List<ReportSection> Sections { get; } = [];
    public List<string> Notes { get; } = [];

    /// <summary>Líneas de firma al pie (actas).</summary>
    public List<string> Signatures { get; } = [];

    public string FileBaseName => $"{Key}-{CutoffDate:yyyyMMdd}";
}

public sealed record ReportDefinition(string Key, string Name, string Description, bool NeedsSite = false);

public sealed class ReportRequest
{
    public required string Key { get; init; }
    public int ProjectId { get; init; }
    public int? SiteId { get; init; }
    public DateOnly? CutoffDate { get; init; }
    public string PreparedBy { get; init; } = string.Empty;
}

public interface IReportService
{
    IReadOnlyList<ReportDefinition> Definitions { get; }
    Task<ReportDocument> BuildAsync(ReportRequest request, CancellationToken ct = default);
}

public interface IReportExporter
{
    byte[] ToExcel(ReportDocument document, ExportTemplate template);
    byte[] ToPdf(ReportDocument document, ExportTemplate template);
}

public interface IExportTemplateService
{
    Task<List<ExportTemplate>> ListAsync(string? reportKey = null, CancellationToken ct = default);
    Task<ExportTemplate> GetAsync(int? id, CancellationToken ct = default);
    Task<ExportTemplate> SaveAsync(ExportTemplate template, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
