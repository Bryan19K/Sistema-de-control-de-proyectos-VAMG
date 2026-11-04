using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Tekcom.Gestion.Application.Import;
using Tekcom.Gestion.Domain.Enums;
using Tekcom.Gestion.Domain.Logic;

namespace Tekcom.Gestion.Infrastructure.Import;

/// <summary>
/// Lee el Excel de control "PROYECTO FTTH" (hojas AVANCE ODN y AVANCE FEEDER). Las columnas se
/// detectan por el texto del encabezado, no por la letra, así que sigue funcionando si se mueven.
/// No escribe nada: solo produce el análisis con sus problemas.
/// </summary>
internal sealed partial class ExcelWorkbookParser
{
    private readonly ImportReport _report = new();
    private readonly ParsedWorkbook _workbook = new();
    private Dictionary<string, Dictionary<string, string>> _comments = new(StringComparer.OrdinalIgnoreCase);
    public ImportAnalysis Parse(Stream stream, string fileName)
    {
        _report.FileName = fileName;

        XLWorkbook book;
        try
        {
            _comments = OoxmlComments.Read(stream);
            stream.Position = 0;
            book = new XLWorkbook(stream);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Error, "(archivo)", null, null,
                "No se pudo abrir el archivo. Verifique que sea un Excel (.xlsx) válido y que no esté dañado."));
            return new ImportAnalysis { Report = _report, Workbook = _workbook };
        }

        using (book)
        {
            ParseSummary(FindSheet(book, "RESUMEN GENERAL"));

            var odn = FindSheet(book, "AVANCE ODN");
            if (odn is null)
            {
                _report.Issues.Add(new ImportIssue(ImportSeverity.Error, "(archivo)", null, null,
                    "No se encontró la hoja 'AVANCE ODN'."));
            }
            else
            {
                ParseOdn(odn);
            }

            var feeder = FindSheet(book, "AVANCE FEEDER");
            if (feeder is null)
            {
                _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, "(archivo)", null, null,
                    "No se encontró la hoja 'AVANCE FEEDER': el feeder no se importa."));
            }
            else
            {
                ParseFeeder(feeder);
            }
        }

        foreach (var node in _workbook.Nodes)
        {
            _report.NodeCodes.Add(node.Code);
        }

        BuildTotalChecksFromParsed();
        return new ImportAnalysis { Report = _report, Workbook = _workbook };
    }

    // ---------------------------------------------------------------- Resumen (solo para sugerir el proyecto)

    private void ParseSummary(IXLWorksheet? sheet)
    {
        if (sheet is null)
        {
            return;
        }

        var title = Text(sheet.Cell("B2"));
        var subtitle = Text(sheet.Cell("B3"));
        _workbook.ProjectTitle = title;
        _report.SuggestedClient = null;

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var match = ProjectCodeRegex().Match(subtitle);
            if (match.Success)
            {
                _report.SuggestedProjectCode = match.Groups["code"].Value.Trim();
                _report.SuggestedProjectName = PrettyTitle(match.Groups["name"].Value.Trim());
            }
            else
            {
                _report.SuggestedProjectName = PrettyTitle(subtitle);
            }
        }
    }

    // ---------------------------------------------------------------- AVANCE ODN

    private static readonly HashSet<string> OdnIgnoredKeys =
    [
        "PENDIENTE", "AVANCE FO", "AVANCE INSTALACION DE FO METROS", "AVANCE MPO", "AVANCE MPO METROS",
        "TOTAL PROYECTADO", "TOTAL INSTALADO", "DE AVANCE DE INSTALACION DE FATS", "CONTRATISTA DE FUSION",
        "LISTO PARA MEDIR", "AVANCE GLOBAL", "COMENTARIO DE PAGO", "OBSERVACIONES", "REQUERIMIENTOS",
        "DATA STATUS", "SITIO", "NODO", "RESPONSABLE DE BRIGADA", "TELEFONO RESPONSABLE", "FECHA DE INICIO",
        "FECHA DE FINAL", "LATITUD", "LONGITUD", "OPERADOR", "SUPERVISOR RESPONSABLE", "ATP",
        "MEDICIONES CON RECEPCION", "ESTETICAS", "TECNICAS", "RECEPCION FINAL",
    ];

    private void ParseOdn(IXLWorksheet sheet)
    {
        const string name = "AVANCE ODN";
        var headerRow = FindHeaderRow(sheet, "NODO");
        if (headerRow is null)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Error, name, null, null,
                "No se encontró la fila de encabezados (columna 'NODO')."));
            return;
        }

        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? headerRow.Value;

        // Encabezado de cada columna: el de la fila principal o, si está vacío, el de la fila de grupo.
        var columns = new List<OdnColumn>();
        for (var c = 1; c <= lastCol; c++)
        {
            var raw = Text(sheet.Cell(headerRow.Value, c));
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = Text(sheet.Cell(headerRow.Value - 1, c));
            }

            if (!string.IsNullOrWhiteSpace(raw))
            {
                columns.Add(new OdnColumn(c, raw.Trim(), NameNormalizer.Key(raw)));
            }
        }

        int? Col(string key) => columns.FirstOrDefault(x => x.Key == key)?.Index;

        var nodeCol = Col("NODO")!.Value;
        var zoneCol = Col("SITIO");
        var statusCol = Col("DATA STATUS");
        var brigadeCol = Col("RESPONSABLE DE BRIGADA");
        var phoneCol = Col("TELEFONO RESPONSABLE");
        var startCol = Col("FECHA DE INICIO");
        var endCol = Col("FECHA DE FINAL");
        var latCol = Col("LATITUD");
        var lonCol = Col("LONGITUD");
        var operatorCol = Col("OPERADOR");
        var supervisorCol = Col("SUPERVISOR RESPONSABLE");

        // Clasificación de columnas de cantidades.
        var quantityColumns = new List<QuantityColumn>();
        var materialBlockStart = Col("CONECTORES");
        var materialBlockEnd = Col("VINETAS");
        var recognized = new HashSet<int>();

        foreach (var column in columns)
        {
            var q = Classify(column, materialBlockStart, materialBlockEnd);
            if (q is not null)
            {
                quantityColumns.Add(q);
                recognized.Add(column.Index);
            }
        }

        // Columnas que no se reconocen: se avisa una sola vez.
        foreach (var column in columns.Where(c => !recognized.Contains(c.Index)))
        {
            if (OdnIgnoredKeys.Contains(column.Key) || column.Key.StartsWith("MANGAS", StringComparison.Ordinal))
            {
                if (column.Key.StartsWith("MANGAS", StringComparison.Ordinal))
                {
                    _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, name, null, column.Raw,
                        "No se importa: sus fórmulas son distintas en cada fila (a veces igual a las fusiones, a veces un porcentaje)."));
                }

                continue;
            }

            _report.Issues.Add(new ImportIssue(ImportSeverity.Info, name, null, column.Raw,
                "Columna no reconocida: no se importa."));
        }

        int? stageCol(string key) => Col(key);
        var atpCol = stageCol("ATP");
        var measuredCol = stageCol("MEDICIONES CON RECEPCION");
        var aestheticCol = stageCol("ESTETICAS");
        var technicalCol = stageCol("TECNICAS");
        var finalCol = stageCol("RECEPCION FINAL");
        var paymentCol = Col("COMENTARIO DE PAGO");
        var remarksCol = Col("OBSERVACIONES");
        var requirementsCol = Col("REQUERIMIENTOS");
        var arosCol = columns.FirstOrDefault(c => c.Key == "ARO DE SUJECION")?.Index;
        var deadFiberCol = columns.FirstOrDefault(c => c.Key.StartsWith("FIBRA MUERTA", StringComparison.Ordinal))?.Index;

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingDates = new List<string>();
        int? totalRow = null;

        for (var r = headerRow.Value + 1; r <= lastRow; r++)
        {
            var code = Text(sheet.Cell(r, nodeCol))?.Trim();
            var firstCell = Text(sheet.Cell(r, 2))?.Trim();

            if (string.Equals(firstCell, "TOTAL", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(code))
            {
                totalRow = r;
                continue;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            var node = new ParsedNode
            {
                Row = r,
                Code = code.ToUpperInvariant(),
                Zone = zoneCol is null ? null : Text(sheet.Cell(r, zoneCol.Value))?.Trim(),
                DataStatus = statusCol is null ? null : Text(sheet.Cell(r, statusCol.Value))?.Trim().ToUpperInvariant(),
                Brigade = brigadeCol is null ? null : Text(sheet.Cell(r, brigadeCol.Value))?.Trim(),
                Phone = phoneCol is null ? null : Text(sheet.Cell(r, phoneCol.Value))?.Trim(),
                Start = startCol is null ? null : ReadDate(sheet.Cell(r, startCol.Value), name, r, "FECHA DE INICIO", pendingDates, code),
                End = endCol is null ? null : ReadDate(sheet.Cell(r, endCol.Value), name, r, "FECHA DE FINAL", pendingDates, code),
                Latitude = latCol is null ? null : ReadDouble(sheet.Cell(r, latCol.Value)),
                Longitude = lonCol is null ? null : ReadDouble(sheet.Cell(r, lonCol.Value)),
                Operator = operatorCol is null ? null : Text(sheet.Cell(r, operatorCol.Value))?.Trim(),
                Supervisor = supervisorCol is null ? null : Text(sheet.Cell(r, supervisorCol.Value))?.Trim(),
            };

            if (!seenCodes.Add(node.Code))
            {
                _report.Issues.Add(new ImportIssue(ImportSeverity.Error, name, r, "NODO",
                    $"El nodo {node.Code} aparece repetido; esta fila no se importa."));
                _report.Counts.RowsSkipped++;
                continue;
            }

            ReadQuantities(sheet, r, node, quantityColumns);

            // Tréboles: vienen escritos en el comentario de la celda del aro de sujeción.
            if (arosCol is not null)
            {
                var comment = CommentText(sheet.Cell(r, arosCol.Value));
                if (comment is not null)
                {
                    var match = TrebolRegex().Match(comment);
                    if (match.Success && TryParseNumber(match.Groups["n"].Value, out var trebles))
                    {
                        node.Quantities.Add(new ParsedQuantity
                        {
                            MaterialName = "Trébol",
                            Category = MaterialCategory.Hardware,
                            Installed = trebles,
                        });
                    }
                    else
                    {
                        _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, name, r, "ARO DE SUJECION",
                            $"El comentario de la celda no tiene una cantidad de tréboles reconocible: \"{Shorten(comment)}\"."));
                    }
                }
            }

            var notes = new List<string>();
            notes.AddRange(node.Remarks);
            if (deadFiberCol is not null)
            {
                var comment = CommentText(sheet.Cell(r, deadFiberCol.Value));
                if (comment is not null)
                {
                    notes.Add($"Fibra muerta: {comment}");
                }
            }

            foreach (var (col, label) in new[] { (paymentCol, "Comentario de pago"), (remarksCol, "Observaciones"), (requirementsCol, "Requerimientos") })
            {
                var value = col is null ? null : Text(sheet.Cell(r, col.Value))?.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    notes.Add($"{label}: {value}");
                }
            }

            DeriveStages(node, sheet, r, atpCol, measuredCol, aestheticCol, technicalCol, finalCol);

            var nodeWithNotes = notes.Count == 0 ? node : CloneWithNotes(node, string.Join(" | ", notes));
            _workbook.Nodes.Add(nodeWithNotes);
        }

        if (pendingDates.Count > 0)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Info, name, null, "FECHA DE INICIO / FECHA DE FINAL",
                $"Fechas por definir en {pendingDates.Count} nodo(s): {string.Join(", ", pendingDates.Distinct())}. Quedan sin fecha."));
        }

        if (totalRow is null)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, name, null, null,
                "No hay fila TOTAL: no se pueden validar los totales contra el Excel."));
        }
        else
        {
            CaptureExcelTotals(sheet, totalRow.Value, quantityColumns);
        }

        foreach (var node in _workbook.Nodes)
        {
            WarnIfInstalledExceedsProjected(node, name);
        }
    }

    private static ParsedNode CloneWithNotes(ParsedNode node, string notes)
    {
        var copy = new ParsedNode
        {
            Row = node.Row,
            Code = node.Code,
            Zone = node.Zone,
            DataStatus = node.DataStatus,
            Brigade = node.Brigade,
            Phone = node.Phone,
            Start = node.Start,
            End = node.End,
            Latitude = node.Latitude,
            Longitude = node.Longitude,
            Operator = node.Operator,
            Supervisor = node.Supervisor,
            Notes = notes,
            HasErrors = node.HasErrors,
        };
        copy.Quantities.AddRange(node.Quantities);
        foreach (var (code, status) in node.Stages)
        {
            copy.Stages[code] = status;
        }

        return copy;
    }

    private void ReadQuantities(IXLWorksheet sheet, int row, ParsedNode node, List<QuantityColumn> columns)
    {
        var byMaterial = new Dictionary<string, ParsedQuantity>(StringComparer.Ordinal);

        foreach (var column in columns)
        {
            var cell = sheet.Cell(row, column.Index);
            var value = ReadQuantity(cell, "AVANCE ODN", row, column.Header, node, column.TotalGroupName is not null);
            if (value is null)
            {
                continue;
            }

            var key = NameNormalizer.Key(column.MaterialName);
            if (!byMaterial.TryGetValue(key, out var quantity))
            {
                quantity = new ParsedQuantity
                {
                    MaterialName = column.MaterialName,
                    Category = column.Category,
                    Group = column.Group,
                    Unit = column.Unit,
                    LengthMeters = column.LengthMeters,
                };
                byMaterial[key] = quantity;
                node.Quantities.Add(quantity);
            }

            if (column.Role == QuantityRole.Projected)
            {
                quantity.Projected = (quantity.Projected ?? 0) + value.Value;
            }
            else
            {
                quantity.Installed = (quantity.Installed ?? 0) + value.Value;
            }
        }
    }

    private decimal? ReadQuantity(IXLCell cell, string sheet, int row, string column, ParsedNode node, bool blocking)
    {
        var value = Value(cell);
        if (value.IsBlank || (value.IsText && string.IsNullOrWhiteSpace(value.GetText())))
        {
            return null;
        }

        if (value.IsError)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet, row, column,
                $"La celda tiene un error de fórmula ({value}); la fila de {node.Code} no se importa."));
            node.HasErrors = true;
            return null;
        }

        if (TryNumber(value, out var number))
        {
            if (number < 0)
            {
                _report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet, row, column,
                    $"La cantidad es negativa ({number}); la fila de {node.Code} no se importa."));
                node.HasErrors = true;
                return null;
            }

            return number;
        }

        if (!blocking)
        {
            // Materiales de apoyo (conectores, herrajes...): el texto no impide importar el nodo.
            // Se conserva como nota para no perder lo que escribió el usuario.
            _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet, row, column,
                $"{node.Code}: '{value}' no es una cantidad; esa celda no se importa y se guarda como nota del nodo."));
            node.Remarks.Add($"{column}: {value}");
            return null;
        }

        _report.Issues.Add(new ImportIssue(ImportSeverity.Error, sheet, row, column,
            $"'{value}' no es una cantidad; la fila de {node.Code} no se importa."));
        node.HasErrors = true;
        return null;
    }

    private void WarnIfInstalledExceedsProjected(ParsedNode node, string sheet)
    {
        foreach (var q in node.Quantities.Where(q => q.Group != QuantityGroup.Crossing && q.Projected is > 0 && q.Installed > q.Projected))
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet, node.Row, q.MaterialName,
                $"{node.Code}: lo instalado ({q.Installed:0.###}) supera lo proyectado ({q.Projected:0.###})."));
        }
    }

    private void DeriveStages(ParsedNode node, IXLWorksheet sheet, int row,
        int? atpCol, int? measuredCol, int? aestheticCol, int? technicalCol, int? finalCol)
    {
        (decimal Projected, decimal Installed) Sum(params QuantityGroup[] groups) =>
            (node.Quantities.Where(q => groups.Contains(q.Group)).Sum(q => q.Projected ?? 0),
             node.Quantities.Where(q => groups.Contains(q.Group)).Sum(q => q.Installed ?? 0));

        void FromQuantities(StageCode stage, params QuantityGroup[] groups)
        {
            var (projected, installed) = Sum(groups);
            if (projected == 0 && installed == 0)
            {
                return;
            }

            node.Stages[stage] = projected > 0 && installed >= projected
                ? StageStatus.Finished
                : installed > 0 ? StageStatus.InProgress : StageStatus.Pending;
        }

        FromQuantities(StageCode.FcCable, QuantityGroup.FcCable);
        FromQuantities(StageCode.MpoCable, QuantityGroup.MpoCable);
        FromQuantities(StageCode.Fat, QuantityGroup.SubBox, QuantityGroup.EndBox);
        FromQuantities(StageCode.HubAndXBox, QuantityGroup.HubBox, QuantityGroup.XBox);
        FromQuantities(StageCode.Fusion, QuantityGroup.Fusion);
        FromQuantities(StageCode.AmericanCrossings, QuantityGroup.Crossing);

        StageStatus? Explicit(int? col) => col is null ? null : StatusFromText(Text(sheet.Cell(row, col.Value)));

        void Set(StageCode stage, StageStatus? status)
        {
            if (status is not null)
            {
                node.Stages[stage] = status.Value;
            }
        }

        Set(StageCode.Atp, Explicit(atpCol));
        Set(StageCode.FatMeasurements, Explicit(measuredCol));
        Set(StageCode.FinalReception, Explicit(finalCol));

        var corrections = new[] { Explicit(aestheticCol), Explicit(technicalCol) }.Where(s => s is not null).Select(s => s!.Value).ToList();
        if (corrections.Count > 0)
        {
            node.Stages[StageCode.Corrections] =
                corrections.Contains(StageStatus.InCorrection) ? StageStatus.InCorrection
                : corrections.Contains(StageStatus.InProgress) ? StageStatus.InProgress
                : corrections.Contains(StageStatus.Pending) ? StageStatus.Pending
                : corrections.All(s => s == StageStatus.NotApplicable) ? StageStatus.NotApplicable
                : StageStatus.Finished;
        }
    }

    private static StageStatus? StatusFromText(string? text)
    {
        var key = NameNormalizer.Key(text);
        return key switch
        {
            "" => null,
            "FINALIZADO" or "FINALIZADA" or "FINALIZADOS" or "FINALIZADAS" => StageStatus.Finished,
            "EN PROCESO" => StageStatus.InProgress,
            "SIN INICIAR" or "PENDIENTE" => StageStatus.Pending,
            "EN CORRECCION" => StageStatus.InCorrection,
            "N A" or "NA" => StageStatus.NotApplicable,
            _ => null,
        };
    }

    // ---------------------------------------------------------------- Clasificación de columnas

    private enum QuantityRole { Projected, Installed }

    private sealed record OdnColumn(int Index, string Raw, string Key);

    private sealed record QuantityColumn(
        int Index,
        string Header,
        string MaterialName,
        QuantityRole Role,
        MaterialCategory Category,
        QuantityGroup Group,
        string Unit,
        int? LengthMeters,
        string? TotalGroupName);

    private QuantityColumn? Classify(OdnColumn column, int? blockStart, int? blockEnd)
    {
        var key = column.Key;

        var fo = FoRegex().Match(key);
        if (fo.Success)
        {
            var length = int.Parse(fo.Groups["n"].Value, CultureInfo.InvariantCulture);
            return new QuantityColumn(column.Index, column.Raw, $"Cable FC SC/APC {length} m", Role(fo), MaterialCategory.FiberCable,
                QuantityGroup.FcCable, "UND", length, "FC");
        }

        var mpo = MpoRegex().Match(key);
        if (mpo.Success)
        {
            var length = int.Parse(mpo.Groups["n"].Value, CultureInfo.InvariantCulture);
            return new QuantityColumn(column.Index, column.Raw, $"Cable MPO {length} m", Role(mpo), MaterialCategory.MpoCable,
                QuantityGroup.MpoCable, "UND", length, "MPO");
        }

        foreach (var (prefix, material, group) in new[]
                 {
                     ("SUB BOX", "NAP de paso", QuantityGroup.SubBox),
                     ("END BOX", "NAP final", QuantityGroup.EndBox),
                     ("HBBOX", "Hub-box", QuantityGroup.HubBox),
                     ("XBOX", "X-box", QuantityGroup.XBox),
                 })
        {
            if (key == $"{prefix} PROYECTADO" || key == $"{prefix} INSTALADO")
            {
                return new QuantityColumn(column.Index, column.Raw, material,
                    key.EndsWith("PROYECTADO", StringComparison.Ordinal) ? QuantityRole.Projected : QuantityRole.Installed,
                    MaterialCategory.NetworkElement, group, "UND", null, group.ToString());
            }
        }

        if (key == "FUSIONES EN XBOX PROYECTADO")
        {
            return new QuantityColumn(column.Index, column.Raw, "Fusión en X-box", QuantityRole.Projected,
                MaterialCategory.Activity, QuantityGroup.Fusion, "fusión", null, "Fusion");
        }

        if (key == "FUSIONES XBOX REAL")
        {
            return new QuantityColumn(column.Index, column.Raw, "Fusión en X-box", QuantityRole.Installed,
                MaterialCategory.Activity, QuantityGroup.Fusion, "fusión", null, "Fusion");
        }

        if (key == "CRUCE AMERICANOS PROY")
        {
            return new QuantityColumn(column.Index, column.Raw, "Cruce americano", QuantityRole.Projected,
                MaterialCategory.Crossing, QuantityGroup.Crossing, "UND", null, "Crossing");
        }

        if (key == "CRUCE AMERICANO")
        {
            return new QuantityColumn(column.Index, column.Raw, "Cruce americano", QuantityRole.Installed,
                MaterialCategory.Crossing, QuantityGroup.Crossing, "UND", null, "Crossing");
        }

        // Bloque "MATERIALES DE ODN": una sola cantidad (la usada), sin proyectado.
        if (blockStart is not null && blockEnd is not null && column.Index >= blockStart && column.Index <= blockEnd)
        {
            var unit = key.StartsWith("FIBRA MUERTA", StringComparison.Ordinal) ? "m"
                : key.StartsWith("ABRAZADERAS", StringComparison.Ordinal) ? "PAR" : "UND";
            return new QuantityColumn(column.Index, column.Raw, MaterialDisplayName(column.Raw), QuantityRole.Installed,
                MaterialCategory.Hardware, QuantityGroup.None, unit, null, null);
        }

        return null;
    }

    private static QuantityRole Role(Match match) =>
        match.Groups["role"].Value == "PROYECTADO" ? QuantityRole.Projected : QuantityRole.Installed;

    private static string MaterialDisplayName(string header)
    {
        // "FIBRA MUERTA (m)" → "FIBRA MUERTA (m)"; se conserva el texto tal como lo usa el Excel.
        return Regex.Replace(header.Trim(), @"\s+", " ");
    }

    // ---------------------------------------------------------------- Totales del Excel

    private void CaptureExcelTotals(IXLWorksheet sheet, int totalRow, List<QuantityColumn> columns)
    {
        foreach (var group in columns.Where(c => c.TotalGroupName is not null).GroupBy(c => (c.Group, c.Role, c.TotalGroupName)))
        {
            decimal sum = 0;
            var any = false;
            foreach (var column in group)
            {
                var value = Value(sheet.Cell(totalRow, column.Index));
                if (TryNumber(value, out var number))
                {
                    sum += number;
                    any = true;
                }
            }

            if (any)
            {
                _workbook.Totals.Add(new ParsedTotal(
                    $"{GroupLabel(group.Key.Group)} · {(group.Key.Role == QuantityRole.Projected ? "proyectado" : "instalado")}",
                    group.Key.Group,
                    group.Key.Role == QuantityRole.Projected,
                    sum));
            }
        }
    }

    internal static string GroupLabel(QuantityGroup group) => group switch
    {
        QuantityGroup.FcCable => "Cable FC",
        QuantityGroup.MpoCable => "Cable MPO",
        QuantityGroup.SubBox => "Sub-box",
        QuantityGroup.EndBox => "End-box",
        QuantityGroup.HubBox => "Hub-box",
        QuantityGroup.XBox => "X-box",
        QuantityGroup.Fusion => "Fusiones",
        QuantityGroup.Crossing => "Cruces americanos",
        _ => group.ToString(),
    };

    private void BuildTotalChecksFromParsed()
    {
        foreach (var total in _workbook.Totals)
        {
            var system = _workbook.Nodes
                .Where(n => !n.HasErrors)
                .SelectMany(n => n.Quantities)
                .Where(q => q.Group == total.Group)
                .Sum(q => (total.IsProjected ? q.Projected : q.Installed) ?? 0);
            _report.TotalChecks.Add(new TotalCheck(total.Name, total.Value, system));
        }

        if (_workbook.Feeder is { } feeder)
        {
            var cables = feeder.Lines.Where(l => l.Key <= FeederItemKind.Cable96).ToList();
            if (feeder.ExcelPlannedMeters is not null)
            {
                _report.TotalChecks.Add(new TotalCheck("Feeder · metros proyectados",
                    feeder.ExcelPlannedMeters.Value, cables.Sum(l => l.Value.Planned)));
            }

            if (feeder.ExcelInstalledMeters is not null)
            {
                _report.TotalChecks.Add(new TotalCheck("Feeder · metros instalados",
                    feeder.ExcelInstalledMeters.Value, cables.Sum(l => l.Value.Installed)));
            }
        }
    }

    // ---------------------------------------------------------------- AVANCE FEEDER

    private decimal? _feederExcelPlanned;
    private decimal? _feederExcelInstalled;

    private static readonly Dictionary<string, FeederItemKind> FeederCableKinds = new()
    {
        ["12"] = FeederItemKind.Cable12,
        ["24"] = FeederItemKind.Cable24,
        ["48"] = FeederItemKind.Cable48,
        ["96"] = FeederItemKind.Cable96,
    };

    private static readonly Dictionary<string, FeederItemKind> FeederClosureKinds = new()
    {
        ["12"] = FeederItemKind.Closure12,
        ["24"] = FeederItemKind.Closure24,
        ["48"] = FeederItemKind.Closure48,
        ["96"] = FeederItemKind.Closure96,
    };

    private void ParseFeeder(IXLWorksheet sheet)
    {
        const string name = "AVANCE FEEDER";
        var headerRow = FindHeaderRow(sheet, "BRIGADA RESPONSABLE");
        if (headerRow is null)
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, name, null, null,
                "No se encontró la fila de encabezados (columna 'BRIGADA RESPONSABLE'): el feeder no se importa."));
            return;
        }

        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        var dataRow = headerRow.Value + 1;
        var brigade = Text(sheet.Cell(dataRow, 2))?.Trim();
        if (string.IsNullOrWhiteSpace(brigade))
        {
            _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, name, dataRow, "BRIGADA RESPONSABLE",
                "No hay una fila de datos del feeder."));
            return;
        }

        var feeder = new ParsedFeeder { Brigade = brigade };
        FeederItemKind? pendingKind = null;
        var invalid = false;

        // Los encabezados "INSTALADO (m)" / "INSTALADOS" se repiten: pertenecen al "proyectado" anterior.
        var inMaterialBlock = false;
        for (var c = 3; c <= lastCol; c++)
        {
            var raw = Text(sheet.Cell(headerRow.Value, c))?.Trim() ?? string.Empty;
            var groupRaw = Text(sheet.Cell(headerRow.Value - 1, c))?.Trim() ?? string.Empty;
            var key = NameNormalizer.Key(raw);
            var groupKey = NameNormalizer.Key(groupRaw);
            var cell = sheet.Cell(dataRow, c);

            decimal? Read()
            {
                var value = Value(cell);
                if (value.IsBlank)
                {
                    return null;
                }

                if (TryNumber(value, out var number) && number >= 0)
                {
                    return number;
                }

                _report.Issues.Add(new ImportIssue(ImportSeverity.Error, name, dataRow, raw,
                    $"'{value}' no es una cantidad válida; el feeder no se importa."));
                invalid = true;
                return null;
            }

            if (key == "TOTAL FEEDER PROYECTADO M")
            {
                _feederExcelPlanned = Read();
                continue;
            }

            if (key == "AVANCE FEEDER M")
            {
                _feederExcelInstalled = Read();
                continue;
            }

            var cable = FeederCableRegex().Match(key);
            if (cable.Success)
            {
                var kind = FeederCableKinds[cable.Groups["n"].Value];
                feeder.Lines[kind] = (Read() ?? 0, 0);
                pendingKind = kind;
                continue;
            }

            if (key == "INSTALADO M" && pendingKind is { } cableKind && feeder.Lines.TryGetValue(cableKind, out var cableLine))
            {
                feeder.Lines[cableKind] = (cableLine.Planned, Read() ?? 0);
                pendingKind = null;
                continue;
            }

            var closure = FeederClosureRegex().Match(key);
            if (closure.Success)
            {
                var kind = FeederClosureKinds[closure.Groups["n"].Value];
                feeder.Lines[kind] = (Read() ?? 0, 0);
                pendingKind = kind;
                continue;
            }

            if (key == "INSTALADOS" && pendingKind is { } closureKind && feeder.Lines.TryGetValue(closureKind, out var closureLine))
            {
                feeder.Lines[closureKind] = (closureLine.Planned, Read() ?? 0);
                pendingKind = null;
                continue;
            }

            if (groupKey.Contains("ODF PROYECTADO", StringComparison.Ordinal))
            {
                feeder.Lines[FeederItemKind.Odf96] = (Read() ?? 0, feeder.Lines.GetValueOrDefault(FeederItemKind.Odf96).Installed);
                continue;
            }

            if (groupKey.Contains("ODF INSTALADO", StringComparison.Ordinal))
            {
                feeder.Lines[FeederItemKind.Odf96] = (feeder.Lines.GetValueOrDefault(FeederItemKind.Odf96).Planned, Read() ?? 0);
                continue;
            }

            if (key == "ABRAZADERAS 3 5")
            {
                inMaterialBlock = true;
            }

            if (inMaterialBlock)
            {
                var quantity = Read();
                if (quantity is > 0)
                {
                    var closureMaterial = key.StartsWith("CIERRE", StringComparison.Ordinal);
                    feeder.Materials.Add(new ParsedQuantity
                    {
                        MaterialName = MaterialDisplayName(raw),
                        Category = closureMaterial ? MaterialCategory.Closure : MaterialCategory.Hardware,
                        Unit = key.StartsWith("ABRAZADERAS", StringComparison.Ordinal) ? "PAR" : "UND",
                        Installed = quantity,
                    });
                }

                if (key.StartsWith("CIERRE TIPO DOMO", StringComparison.Ordinal))
                {
                    inMaterialBlock = false;
                }
            }
        }

        if (invalid)
        {
            _report.Counts.RowsSkipped++;
            return;
        }

        feeder.ExcelPlannedMeters = _feederExcelPlanned;
        feeder.ExcelInstalledMeters = _feederExcelInstalled;
        _workbook.Feeder = feeder;
    }

    // ---------------------------------------------------------------- Utilidades de lectura

    private static IXLWorksheet? FindSheet(XLWorkbook book, string name) =>
        book.Worksheets.FirstOrDefault(s => NameNormalizer.Key(s.Name).Contains(NameNormalizer.Key(name), StringComparison.Ordinal));

    private static int? FindHeaderRow(IXLWorksheet sheet, string headerKey)
    {
        var lastRow = Math.Min(sheet.LastRowUsed()?.RowNumber() ?? 1, 15);
        var lastCol = Math.Min(sheet.LastColumnUsed()?.ColumnNumber() ?? 1, 30);
        for (var r = 1; r <= lastRow; r++)
        {
            for (var c = 1; c <= lastCol; c++)
            {
                if (NameNormalizer.Key(Text(sheet.Cell(r, c))) == headerKey)
                {
                    return r;
                }
            }
        }

        return null;
    }

    private static XLCellValue Value(IXLCell cell)
    {
        if (!cell.HasFormula)
        {
            return cell.Value;
        }

        var cached = cell.CachedValue;
        return cached.IsBlank ? cell.Value : cached;
    }

    private static string? Text(IXLCell cell)
    {
        var value = Value(cell);
        if (value.IsBlank)
        {
            return null;
        }

        return value.IsText ? value.GetText() : value.ToString(CultureInfo.InvariantCulture);
    }

    private string? CommentText(IXLCell cell)
    {
        if (!cell.HasComment)
        {
            return null;
        }

        var text = cell.GetComment().Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text)
            && _comments.TryGetValue(cell.Worksheet.Name, out var sheetComments)
            && sheetComments.TryGetValue(cell.Address.ToStringRelative(), out var fromPackage))
        {
            text = fromPackage;
        }

        const string marker = "Comment:";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            text = text[(index + marker.Length)..];
        }

        text = text.Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool TryNumber(XLCellValue value, out decimal number)
    {
        number = 0;
        if (value.IsNumber)
        {
            number = (decimal)value.GetNumber();
            return true;
        }

        if (value.IsText)
        {
            return TryParseNumber(value.GetText(), out number);
        }

        return false;
    }

    private static bool TryParseNumber(string text, out decimal number) =>
        decimal.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out number);

    private static double? ReadDouble(IXLCell cell)
    {
        var value = Value(cell);
        if (value.IsNumber)
        {
            return value.GetNumber();
        }

        return value.IsText && double.TryParse(value.GetText().Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private DateOnly? ReadDate(IXLCell cell, string sheet, int row, string column, List<string> pending, string nodeCode)
    {
        var value = Value(cell);
        if (value.IsBlank)
        {
            return null;
        }

        if (value.IsDateTime)
        {
            return DateOnly.FromDateTime(value.GetDateTime());
        }

        if (value.IsText)
        {
            var text = value.GetText().Trim();
            if (NameNormalizer.Key(text) == "POR DEFINIR")
            {
                pending.Add(nodeCode);
                return null;
            }

            if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-SV"), DateTimeStyles.None, out var parsed))
            {
                return DateOnly.FromDateTime(parsed);
            }
        }

        _report.Issues.Add(new ImportIssue(ImportSeverity.Warning, sheet, row, column,
            $"{nodeCode}: '{value}' no es una fecha válida; queda sin fecha."));
        return null;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "…";

    private static readonly HashSet<string> SmallWords = ["de", "del", "la", "el", "los", "las", "y", "en", "para"];
    private static readonly HashSet<string> Acronyms = ["FTTH", "ODN", "ATP", "OLT", "SFP", "MPO", "FC", "FO"];

    internal static string PrettyTitle(string text)
    {
        var words = text.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var upper = words[i].ToUpperInvariant();
            if (Acronyms.Contains(upper))
            {
                words[i] = upper;
            }
            else if (i > 0 && SmallWords.Contains(words[i]))
            {
                // se queda en minúscula
            }
            else
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }
        }

        return string.Join(' ', words);
    }

    [GeneratedRegex(@"^FO (?<n>\d+) M (?<role>PROYECTADO|INSTALADO)$")]
    private static partial Regex FoRegex();

    [GeneratedRegex(@"^MPO (?<n>\d+) (?<role>PROYECTADO|INSTALADO)$")]
    private static partial Regex MpoRegex();

    [GeneratedRegex(@"^FO (?<n>12|24|48|96)H PROYECTADO$")]
    private static partial Regex FeederCableRegex();

    [GeneratedRegex(@"^(?<n>12|24|48|96)H PROYECTADOS$")]
    private static partial Regex FeederClosureRegex();

    [GeneratedRegex(@"(?<n>\d[\d.,]*)\s*tr[eé]boles?", RegexOptions.IgnoreCase)]
    private static partial Regex TrebolRegex();

    [GeneratedRegex(@"^(?<code>\d{4}(?:\s*-\s*\d{4})?)\s*-\s*(?<name>.+)$")]
    private static partial Regex ProjectCodeRegex();
}
