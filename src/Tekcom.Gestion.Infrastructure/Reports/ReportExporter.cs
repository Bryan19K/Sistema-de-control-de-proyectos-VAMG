using System.Globalization;
using ClosedXML.Excel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tekcom.Gestion.Application.Reports;
using Tekcom.Gestion.Domain.Entities;

namespace Tekcom.Gestion.Infrastructure.Reports;

/// <summary>
/// Exporta un reporte a Excel (ClosedXML) o PDF (QuestPDF). Todos los archivos llevan el encabezado
/// fijo de TEK-COM: logo, nombre del reporte, proyecto, fecha de corte y "Elaborado por".
/// </summary>
public sealed class ReportExporter : IReportExporter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("es-SV");
    private readonly string? _logoPath;

    static ReportExporter()
    {
        // Licencia Community de QuestPDF: gratuita para empresas con ingresos anuales menores a 1 millón de USD.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReportExporter(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Branding:LogoPath"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "wwwroot", "branding", "logo.png")
            : Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _logoPath = File.Exists(path) ? path : null;
    }

    // ================================================================ Excel

    public byte[] ToExcel(ReportDocument document, ExportTemplate template)
    {
        var sections = VisibleSections(document, template);
        var columnCount = Math.Max(1, sections.Count == 0 ? 1 : sections.Max(s => s.Columns.Count));
        var header = XLColor.FromHtml(template.HeaderColor);

        using var book = new XLWorkbook();
        book.Properties.Author = document.PreparedBy;
        book.Properties.Title = $"{document.Title} · {document.ProjectName}";
        var sheet = book.AddWorksheet(SafeSheetName(document.Title));

        // Anchos: cada columna toma el mayor ancho relativo que necesite alguna sección.
        for (var c = 1; c <= columnCount; c++)
        {
            double width = 10;
            foreach (var s in sections)
            {
                if (c <= s.Columns.Count)
                {
                    var total = s.Columns.Sum(x => x.Width);
                    width = Math.Max(width, s.Columns[c - 1].Width / total * 120);
                }
            }

            sheet.Column(c).Width = Math.Min(width, 60);
        }

        // ---- Encabezado fijo
        var logoCols = Math.Min(2, columnCount);
        if (_logoPath is not null)
        {
            var picture = sheet.AddPicture(_logoPath).MoveTo(sheet.Cell(1, 1));
            // Cabe en las 3 filas del encabezado (56 px de alto) conservando la proporción.
            var ratio = picture.OriginalWidth / (double)picture.OriginalHeight;
            picture.WithSize((int)Math.Round(56 * ratio), 56);
        }
        else
        {
            var brand = sheet.Range(1, 1, 3, logoCols).Merge();
            brand.FirstCell().Value = "TEK-COM";
            brand.Style.Font.Bold = true;
            brand.Style.Font.FontSize = 20;
            brand.Style.Font.FontColor = header;
            brand.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        var textStart = Math.Min(logoCols + 1, columnCount);
        void HeaderLine(int row, string text, double size, bool bold, XLColor? color = null)
        {
            var range = sheet.Range(row, textStart, row, columnCount).Merge();
            range.FirstCell().Value = text;
            range.Style.Font.FontSize = size;
            range.Style.Font.Bold = bold;
            range.Style.Font.FontColor = color ?? XLColor.FromHtml("#1f2a37");
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        }

        HeaderLine(1, document.Title, 16, true, header);
        HeaderLine(2, document.Subtitle is null ? document.ProjectName : $"{document.ProjectName} · {document.Subtitle}", 11, false);
        HeaderLine(3, $"Fecha de corte: {document.CutoffDate:dd/MM/yyyy}  ·  Elaborado por: {document.PreparedBy}", 10, false, XLColor.FromHtml("#5b6b7c"));

        var rule = sheet.Range(4, 1, 4, columnCount);
        rule.Style.Border.BottomBorder = XLBorderStyleValues.Thick;
        rule.Style.Border.BottomBorderColor = header;

        var row = 6;

        foreach (var (label, value) in document.Facts)
        {
            sheet.Cell(row, 1).Value = label;
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontColor = XLColor.FromHtml("#5b6b7c");
            sheet.Range(row, 2, row, Math.Max(2, Math.Min(columnCount, 4))).Merge().FirstCell().Value = value;
            row++;
        }

        if (document.Facts.Count > 0)
        {
            row++;
        }

        int? firstHeaderRow = null;
        foreach (var section in sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                sheet.Cell(row, 1).Value = section.Heading;
                sheet.Cell(row, 1).Style.Font.Bold = true;
                sheet.Cell(row, 1).Style.Font.FontSize = 12;
                sheet.Cell(row, 1).Style.Font.FontColor = header;
                row++;
            }

            var headerRow = row;
            firstHeaderRow ??= headerRow;
            for (var c = 0; c < section.Columns.Count; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                cell.Value = section.Columns[c].Header;
                cell.Style.Fill.BackgroundColor = header;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                if (IsNumeric(section.Columns[c].Type))
                {
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
            }

            row++;
            var dataStart = row;
            foreach (var values in section.Rows)
            {
                for (var c = 0; c < section.Columns.Count; c++)
                {
                    WriteCell(sheet.Cell(row, c + 1), values[c], section.Columns[c].Type);
                }

                if (template.ZebraRows && (row - dataStart) % 2 == 1)
                {
                    sheet.Range(row, 1, row, section.Columns.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#f5f7fa");
                }

                row++;
            }

            if (section.Totals is not null)
            {
                for (var c = 0; c < section.Columns.Count; c++)
                {
                    WriteCell(sheet.Cell(row, c + 1), section.Totals[c], section.Columns[c].Type);
                }

                var totals = sheet.Range(row, 1, row, section.Columns.Count);
                totals.Style.Font.Bold = true;
                totals.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                row++;
            }

            if (section.Rows.Count > 0)
            {
                var body = sheet.Range(dataStart, 1, row - 1, section.Columns.Count);
                body.Style.Border.InsideBorder = XLBorderStyleValues.Hair;
                body.Style.Border.InsideBorderColor = XLColor.FromHtml("#c9d2dc");
                body.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                body.Style.Border.BottomBorderColor = XLColor.FromHtml("#c9d2dc");
            }

            // Un solo filtro por hoja: se aplica a la primera tabla.
            if (headerRow == firstHeaderRow && section.Rows.Count > 0)
            {
                sheet.Range(headerRow, 1, dataStart + section.Rows.Count - 1, section.Columns.Count).SetAutoFilter();
            }

            row += 1;
        }

        foreach (var note in document.Notes)
        {
            var range = sheet.Range(row, 1, row, columnCount).Merge();
            range.FirstCell().Value = note;
            range.Style.Font.Italic = true;
            range.Style.Font.FontColor = XLColor.FromHtml("#5b6b7c");
            range.Style.Alignment.WrapText = true;
            sheet.Row(row).Height = note.Length > 140 ? 30 : 15;
            row++;
        }

        if (document.Signatures.Count > 0)
        {
            row += 3;
            var span = Math.Max(1, columnCount / document.Signatures.Count);
            for (var i = 0; i < document.Signatures.Count; i++)
            {
                var start = 1 + i * span;
                var end = Math.Min(columnCount, start + span - 2);
                if (end < start)
                {
                    end = start;
                }

                var line = sheet.Range(row, start, row, end);
                line.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                var label = sheet.Range(row, start, row, end).Merge();
                label.FirstCell().Value = document.Signatures[i];
                label.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                label.Style.Font.FontColor = XLColor.FromHtml("#5b6b7c");
            }

            row++;
        }

        // ---- Impresión: carta, ajustada al ancho, con encabezado de tabla repetido
        var setup = sheet.PageSetup;
        setup.PaperSize = XLPaperSize.LetterPaper;
        setup.PageOrientation = template.Landscape ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
        setup.FitToPages(1, 0);
        setup.Margins.Left = setup.Margins.Right = 0.4;
        setup.Margins.Top = 0.5;
        setup.Margins.Bottom = 0.6;
        setup.CenterHorizontally = true;
        setup.PrintAreas.Add(1, 1, Math.Max(row, 5), columnCount);
        if (firstHeaderRow is { } fixedRow)
        {
            setup.SetRowsToRepeatAtTop(fixedRow, fixedRow);
            if (sections.Count == 1 && document.Facts.Count == 0)
            {
                sheet.SheetView.FreezeRows(fixedRow);
            }
        }

        var footerLeft = string.IsNullOrWhiteSpace(template.FooterNote)
            ? $"Elaborado por: {document.PreparedBy}"
            : $"Elaborado por: {document.PreparedBy} · {template.FooterNote}";
        setup.Footer.Left.AddText(footerLeft);
        setup.Footer.Right.AddText("Página ");
        setup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
        setup.Footer.Right.AddText(" de ");
        setup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteCell(IXLCell cell, object? value, ReportColumnType type)
    {
        switch (value)
        {
            case null:
                return;
            case decimal number:
                cell.Value = number;
                break;
            case int integer:
                cell.Value = integer;
                break;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                break;
            default:
                cell.Value = value.ToString();
                break;
        }

        switch (type)
        {
            case ReportColumnType.Decimal:
                cell.Style.NumberFormat.Format = "#,##0.##";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case ReportColumnType.Integer:
                cell.Style.NumberFormat.Format = "#,##0";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case ReportColumnType.Percent:
                cell.Style.NumberFormat.Format = "0.0%";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                break;
            case ReportColumnType.Date:
                cell.Style.NumberFormat.Format = "dd/mm/yyyy";
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                break;
        }
    }

    // ================================================================ PDF

    public byte[] ToPdf(ReportDocument document, ExportTemplate template)
    {
        var sections = VisibleSections(document, template);
        var header = template.HeaderColor;

        return Document.Create(container => container.Page(page =>
        {
            page.Size(template.Landscape ? PageSizes.Letter.Landscape() : PageSizes.Letter);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor("#1f2a37"));

            page.Header().PaddingBottom(8).BorderBottom(2).BorderColor(header).Row(row =>
            {
                row.ConstantItem(150).AlignMiddle().Element(e =>
                {
                    if (_logoPath is not null)
                    {
                        e.Height(40).Image(_logoPath).FitArea();
                    }
                    else
                    {
                        e.Text("TEK-COM").FontSize(22).Bold().FontColor(header);
                    }
                });

                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text(document.Title).FontSize(15).Bold().FontColor(header);
                    c.Item().AlignRight().Text(document.Subtitle is null ? document.ProjectName : $"{document.ProjectName} · {document.Subtitle}").FontSize(10);
                    c.Item().AlignRight().Text($"Fecha de corte: {document.CutoffDate:dd/MM/yyyy}").FontSize(9).FontColor("#5b6b7c");
                });
            });

            page.Content().PaddingVertical(10).Column(column =>
            {
                column.Spacing(10);

                if (document.Facts.Count > 0)
                {
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.ConstantColumn(130); c.RelativeColumn(); });
                        foreach (var (label, value) in document.Facts)
                        {
                            table.Cell().PaddingVertical(1).Text(label).Bold().FontColor("#5b6b7c");
                            table.Cell().PaddingVertical(1).Text(value);
                        }
                    });
                }

                foreach (var section in sections)
                {
                    column.Item().Column(inner =>
                    {
                        if (!string.IsNullOrWhiteSpace(section.Heading))
                        {
                            inner.Item().PaddingBottom(4).Text(section.Heading).FontSize(11).Bold().FontColor(header);
                        }

                        inner.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                foreach (var col in section.Columns)
                                {
                                    c.RelativeColumn((float)col.Width);
                                }
                            });

                            table.Header(h =>
                            {
                                foreach (var col in section.Columns)
                                {
                                    var cell = h.Cell().Background(header).Padding(4);
                                    if (IsNumeric(col.Type))
                                    {
                                        cell.AlignRight().Text(col.Header).Bold().FontColor(Colors.White);
                                    }
                                    else
                                    {
                                        cell.Text(col.Header).Bold().FontColor(Colors.White);
                                    }
                                }
                            });

                            var index = 0;
                            foreach (var values in section.Rows)
                            {
                                var zebra = template.ZebraRows && index % 2 == 1;
                                for (var c = 0; c < section.Columns.Count; c++)
                                {
                                    var cell = table.Cell().Background(zebra ? "#f5f7fa" : Colors.White)
                                        .BorderBottom(0.5f).BorderColor("#d9e0e7").PaddingVertical(3).PaddingHorizontal(4);
                                    var text = Format(values[c], section.Columns[c].Type);
                                    if (IsNumeric(section.Columns[c].Type))
                                    {
                                        cell.AlignRight().Text(text);
                                    }
                                    else
                                    {
                                        cell.Text(text);
                                    }
                                }

                                index++;
                            }

                            if (section.Totals is not null)
                            {
                                for (var c = 0; c < section.Columns.Count; c++)
                                {
                                    var cell = table.Cell().BorderTop(1).BorderColor(header).Padding(4);
                                    var text = Format(section.Totals[c], section.Columns[c].Type);
                                    if (IsNumeric(section.Columns[c].Type)) { cell.AlignRight().Text(text).Bold(); }
                                    else { cell.Text(text).Bold(); }
                                }
                            }
                        });
                    });
                }

                foreach (var note in document.Notes)
                {
                    column.Item().Text(note).Italic().FontColor("#5b6b7c");
                }

                if (document.Signatures.Count > 0)
                {
                    column.Item().PaddingTop(40).Row(row =>
                    {
                        foreach (var name in document.Signatures)
                        {
                            row.RelativeItem().PaddingHorizontal(12).BorderTop(1).BorderColor("#5b6b7c").PaddingTop(3)
                                .AlignCenter().Text(name).FontColor("#5b6b7c");
                        }
                    });
                }
            });

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text(string.IsNullOrWhiteSpace(template.FooterNote)
                    ? $"Elaborado por: {document.PreparedBy}"
                    : $"Elaborado por: {document.PreparedBy} · {template.FooterNote}").FontSize(8).FontColor("#5b6b7c");
                row.RelativeItem().AlignRight().Text(x =>
                {
                    x.DefaultTextStyle(t => t.FontSize(8).FontColor("#5b6b7c"));
                    x.Span("Página ");
                    x.CurrentPageNumber();
                    x.Span(" de ");
                    x.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    // ================================================================ comunes

    private static List<ReportSection> VisibleSections(ReportDocument document, ExportTemplate template)
    {
        var applies = string.IsNullOrWhiteSpace(template.ReportKey) || template.ReportKey == document.Key;
        var hidden = applies ? template.HiddenSet : new HashSet<string>();
        if (hidden.Count == 0)
        {
            return document.Sections;
        }

        var result = new List<ReportSection>();
        foreach (var section in document.Sections)
        {
            var keep = section.Columns.Select((c, i) => (c, i)).Where(x => !hidden.Contains(x.c.Key)).ToList();
            if (keep.Count == 0)
            {
                continue;
            }

            var copy = new ReportSection { Heading = section.Heading, Columns = keep.Select(x => x.c).ToList() };
            foreach (var values in section.Rows)
            {
                copy.Rows.Add(keep.Select(x => values[x.i]).ToArray());
            }

            if (section.Totals is not null)
            {
                copy.Totals = keep.Select(x => section.Totals[x.i]).ToArray();
            }

            result.Add(copy);
        }

        return result;
    }

    private static bool IsNumeric(ReportColumnType type) =>
        type is ReportColumnType.Decimal or ReportColumnType.Integer or ReportColumnType.Percent;

    private static string Format(object? value, ReportColumnType type) => value switch
    {
        null => string.Empty,
        decimal d when type == ReportColumnType.Percent => d.ToString("P1", Culture),
        decimal d => d.ToString("#,##0.##", Culture),
        int i => i.ToString("#,##0", Culture),
        DateOnly date => date.ToString("dd/MM/yyyy", Culture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string SafeSheetName(string title)
    {
        var name = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        return name.Length > 31 ? name[..31] : name;
    }
}
