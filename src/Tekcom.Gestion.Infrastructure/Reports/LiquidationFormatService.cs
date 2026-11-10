using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Tekcom.Gestion.Application.Common;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Application.Services;
using Tekcom.Gestion.Domain.Entities;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Infrastructure.Persistence;

namespace Tekcom.Gestion.Infrastructure.Reports;

/// <summary>
/// Liquidación de nodos con el formato oficial de la empresa (LIQUIDACION_DE_NODOS_FTTH): una hoja por nodo con
/// ITEMS, NOMBRE DE MATERIAL, GRUPO, UM, ENTREGADO POR ALMACEN, UTILIZADO, TOTAL A DEVOLVER, DEVOLUCION, DEBE y OBSERVACION.
/// Las cantidades salen de lo que ya se registra en el nodo (entregado, instalado y devuelto), sin volver a digitarlas.
/// </summary>
public sealed class LiquidationFormatService : ILiquidationFormatService
{
    public const string NextWaveGroup = "CL - NEXTWAVE";
    public const string TekcomGroup = "TEKCOM";

    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IProjectService _projects;
    private readonly ISiteService _sites;
    private readonly IQuantityService _quantities;
    private readonly string? _logoPath;

    public LiquidationFormatService(IDbContextFactory<ApplicationDbContext> factory, IProjectService projects, ISiteService sites,
        IQuantityService quantities, IConfiguration configuration, IHostEnvironment environment)
    {
        _factory = factory;
        _projects = projects;
        _sites = sites;
        _quantities = quantities;
        var configured = configuration["Branding:LogoPath"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "wwwroot", "branding", "logo.png")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _logoPath = File.Exists(path) ? path : null;
    }

    // ------------------------------------------------------------------ datos

    public async Task<List<LiquidationSheet>> SheetsAsync(int projectId, int? siteId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct) ?? throw new BusinessRuleException("Seleccione un proyecto.");
        var siteList = (await _sites.ListAsync(projectId, ct)).Select(s => s.Site).OrderBy(s => s.Code, StringComparer.OrdinalIgnoreCase).ToList();
        if (siteId is { } only)
        {
            siteList = siteList.Where(s => s.Id == only).ToList();
            if (siteList.Count == 0)
            {
                throw new BusinessRuleException("El nodo no existe en este proyecto.");
            }
        }

        await using var db = _factory.CreateDbContext();
        var liquidations = await db.Liquidations.AsNoTracking().Where(l => l.ProjectId == projectId).ToListAsync(ct);
        var justifications = await db.LiquidationJustifications.AsNoTracking().Where(j => j.Liquidation!.ProjectId == projectId).ToListAsync(ct);

        var sheets = new List<LiquidationSheet>();
        foreach (var site in siteList)
        {
            var lines = (await _quantities.GetSiteMaterialsAsync(site.Id, ct))
                .Where(q => q.Material.TracksStock && (q.Delivered > 0 || q.Installed > 0 || q.Returned > 0))
                .OrderBy(q => SortKey(q.Material))
                .Select(q =>
                {
                    var liquidation = liquidations.FirstOrDefault(l => l.SiteId == site.Id);
                    var notes = liquidation is null
                        ? []
                        : justifications.Where(j => j.LiquidationId == liquidation.Id && j.MaterialId == q.Material.Id)
                            .Select(j => $"Justificado {j.Quantity:0.###}: {j.Reason}").ToList();
                    return new LiquidationLine(q.Material.Code, OfficialName(q.Material), OwnerOf(q.Material), OfficialUnit(q.Material.Unit),
                        q.Delivered, q.Installed, q.Returned, notes.Count == 0 ? null : string.Join("; ", notes));
                }).ToList();

            if (lines.Count == 0 && siteId is null)
            {
                continue;
            }

            var current = liquidations.FirstOrDefault(l => l.SiteId == site.Id);
            sheets.Add(new LiquidationSheet(site.Id, site.Code, project.Name, site.Contractor?.Name, current?.Status == LiquidationStatus.Closed,
                current?.Observations, lines));
        }

        return sheets;
    }

    /// <summary>Grupo de la liquidación: lo que pone el cliente (NextWave, código NW-) y lo que pone TEK-COM.</summary>
    internal static string OwnerOf(Material material)
    {
        var code = (material.Code ?? string.Empty).Trim();
        if (code.StartsWith("NW", StringComparison.OrdinalIgnoreCase))
        {
            return NextWaveGroup;
        }

        if (code.Length > 0)
        {
            return TekcomGroup;
        }

        var name = material.Name.ToUpperInvariant();
        var isNextWave = material.Category is MaterialCategory.FiberCable or MaterialCategory.MpoCable or MaterialCategory.NetworkElement
                         || name.Contains("TREBOL") || name.Contains("ARO DE SUJECION") || name.Contains("TEMCLAN");
        return isNextWave ? NextWaveGroup : TekcomGroup;
    }

    /// <summary>Nombres como los escribe el formato oficial: CABLE FC 250 METROS, CABLE FC MPO 500 METROS, HUB BOX, XBOX…</summary>
    internal static string OfficialName(Material material)
    {
        if (material is { Group: QuantityGroup.FcCable, LengthMeters: { } fc })
        {
            return $"CABLE FC {fc} METROS";
        }

        if (material is { Group: QuantityGroup.MpoCable, LengthMeters: { } mpo })
        {
            return $"CABLE FC MPO {mpo} METROS";
        }

        var upper = material.Name.Trim().ToUpperInvariant();
        return upper switch
        {
            "X-BOX" or "X BOX" => "XBOX",
            "HUB-BOX" => "HUB BOX",
            _ => upper,
        };
    }

    internal static string OfficialUnit(string? unit) => (unit ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "UND" or "UN" or "U" or "UNIDAD" => "Unidad",
        "PAR" or "PARES" => "Par",
        "M" or "MT" or "MTS" or "METRO" or "METROS" => "Metro",
        "ROLLO" or "ROLL" => "Rollo",
        _ => string.IsNullOrWhiteSpace(unit) ? "Unidad" : unit.Trim(),
    };

    private static (int Owner, int Kind, int Length, string Name) SortKey(Material m) =>
        (OwnerOf(m) == NextWaveGroup ? 0 : 1,
            m.Group == QuantityGroup.FcCable ? 0 : m.Group == QuantityGroup.MpoCable ? 1 : 2,
            -(m.LengthMeters ?? 0), m.Name);

    // ------------------------------------------------------------------ Excel oficial

    private static readonly string[] Headers =
        ["ITEMS", "NOMBRE DE MATERIAL", "GRUPO", "UM", "ENTREGADO POR ALMACEN", "UTILIZADO", "TOTAL A DEVOLVER", "DEVOLUCION", "DEBE", "OBSERVACION"];

    private static readonly double[] Widths = [12, 43.6, 15.9, 10.8, 15.3, 12.3, 12.9, 14.6, 10.4, 59.2];

    public byte[] ToOfficialExcel(IReadOnlyList<LiquidationSheet> sheets)
    {
        if (sheets.Count == 0)
        {
            throw new BusinessRuleException("No hay nodos con materiales para liquidar.");
        }

        using var book = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var data in sheets)
        {
            var sheet = book.AddWorksheet(UniqueName(data.NodeCode, used));
            sheet.Style.Font.FontName = "Calibri";
            sheet.Style.Font.FontSize = 12;
            for (var c = 0; c < Widths.Length; c++)
            {
                sheet.Column(c + 1).Width = Widths[c];
            }

            for (var r = 1; r <= 4; r++)
            {
                sheet.Row(r).Height = 18;
            }

            // Encabezado: logo a la izquierda y, a la derecha, el nodo y el contratista (la tabla de abajo es la del formato oficial).
            var banner = sheet.Range(1, 1, 4, 10).Merge();
            banner.Value = $"LIQUIDACIÓN DE MATERIALES · NODO {data.NodeCode}\n{data.ProjectName}{(string.IsNullOrWhiteSpace(data.Contractor) ? string.Empty : $" · Contratista: {data.Contractor}")}";
            banner.Style.Alignment.WrapText = true;
            banner.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            banner.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            banner.Style.Font.Bold = true;
            if (_logoPath is not null)
            {
                sheet.AddPicture(_logoPath).MoveTo(sheet.Cell(1, 1)).WithSize(231, 68);
            }

            sheet.Row(5).Height = 43.8;
            for (var c = 0; c < Headers.Length; c++)
            {
                var cell = sheet.Cell(5, c + 1);
                cell.Value = Headers[c];
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText = true;
            }

            var row = 6;
            foreach (var line in data.Lines)
            {
                sheet.Cell(row, 1).Value = line.Code ?? string.Empty;
                sheet.Cell(row, 2).Value = line.Name;
                sheet.Cell(row, 3).Value = line.Group;
                sheet.Cell(row, 4).Value = line.Unit;
                sheet.Cell(row, 5).Value = line.Delivered;
                sheet.Cell(row, 6).Value = line.Used;
                sheet.Cell(row, 7).FormulaA1 = $"E{row}-F{row}";
                sheet.Cell(row, 8).Value = line.Returned;
                sheet.Cell(row, 9).FormulaA1 = $"G{row}-H{row}";
                sheet.Cell(row, 9).Style.Fill.BackgroundColor = XLColor.FromHtml("#FBE5D6");
                sheet.Cell(row, 10).Value = line.Observation ?? string.Empty;
                for (var c = 4; c <= 9; c++)
                {
                    sheet.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                sheet.Range(row, 5, row, 9).Style.NumberFormat.Format = "#,##0.##";
                row++;
            }

            if (!string.IsNullOrWhiteSpace(data.Observations))
            {
                sheet.Cell(row + 1, 1).Value = "OBSERVACIONES:";
                sheet.Cell(row + 1, 1).Style.Font.Bold = true;
                sheet.Cell(row + 1, 2).Value = data.Observations;
            }

            sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            sheet.PageSetup.FitToPages(1, 0);
        }

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static string UniqueName(string code, HashSet<string> used)
    {
        var clean = new string(code.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        if (clean.Length == 0) { clean = "NODO"; }
        if (clean.Length > 31) { clean = clean[..31]; }
        var name = clean;
        for (var i = 2; !used.Add(name); i++)
        {
            var suffix = $" ({i})";
            name = clean[..Math.Min(clean.Length, 31 - suffix.Length)] + suffix;
        }

        return name;
    }
}
