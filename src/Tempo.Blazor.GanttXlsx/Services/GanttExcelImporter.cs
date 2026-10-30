using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Services;

/// <summary>Imports Gantt tasks from an XLSX spreadsheet stream.</summary>
public static class GanttExcelImporter
{
    private static readonly Dictionary<string, GanttColumnKey> DefaultHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["title"]          = GanttColumnKey.Title,
            ["name"]           = GanttColumnKey.Title,
            ["start"]          = GanttColumnKey.Start,
            ["end"]            = GanttColumnKey.End,
            ["finish"]         = GanttColumnKey.End,
            ["progress"]       = GanttColumnKey.Progress,
            ["done"]           = GanttColumnKey.Progress,
            ["status"]         = GanttColumnKey.Status,
            ["priority"]       = GanttColumnKey.Priority,
            ["wbs"]            = GanttColumnKey.WBS,
            ["predecessors"]   = GanttColumnKey.Predecessor,
            ["assignees"]      = GanttColumnKey.Assignees,
            ["resource names"] = GanttColumnKey.Assignees,
            ["duration"]       = GanttColumnKey.Duration,
            ["length"]         = GanttColumnKey.Duration,
            ["days"]           = GanttColumnKey.Duration,
            ["deadline"]       = GanttColumnKey.Deadline,
            ["due"]            = GanttColumnKey.Deadline,
            ["due date"]       = GanttColumnKey.Deadline,
            ["estimate"]       = GanttColumnKey.Estimation,
            ["estimation"]     = GanttColumnKey.Estimation,
            ["estimated hours"] = GanttColumnKey.Estimation,
            ["time log"]       = GanttColumnKey.TimeLog,
            ["time spent"]     = GanttColumnKey.TimeLog,
            ["logged hours"]   = GanttColumnKey.TimeLog,
            ["actual hours"]   = GanttColumnKey.TimeLog,
        };

    /// <summary>The sheet's header row: the first row of the first worksheet.</summary>
    /// <param name="stream">Readable XLSX stream.</param>
    /// <returns>The header names in column order; empty (and whitespace-only) headers are skipped.</returns>
    public static IReadOnlyList<string> ReadHeaders(Stream stream)
    {
        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbp = doc.WorkbookPart ?? throw new InvalidOperationException("Missing WorkbookPart.");
        var rows = FirstSheetRows(wbp);
        if (rows.Count == 0) return [];
        return ReadRow(rows[0], wbp.SharedStringTablePart?.SharedStringTable)
            .Where(header => !string.IsNullOrWhiteSpace(header))
            .ToList();
    }

    /// <summary>Imports work items from an XLSX stream using optional column mappings.</summary>
    /// <param name="stream">Readable XLSX stream.</param>
    /// <param name="mappings">Optional source-column to Gantt-property mappings.</param>
    /// <returns>Imported work items.</returns>
    public static IReadOnlyList<TmWorkItem> Import(Stream stream,
        IEnumerable<GanttColumnMapping>? mappings = null)
        => ImportResult(stream, mappings).Tasks;

    /// <summary>
    /// Imports the full Gantt dataset: tasks, the dependencies named by a Predecessor column and the
    /// hierarchy named by a WBS column. Data-level problems (unknown predecessors, self references,
    /// cycles, unknown WBS parents, duplicate codes, duplicate headers, unparsable tokens or cells)
    /// become warnings — importing never throws for the sheet's content. Task ids are stable per row
    /// within one import: <c>{options.IdPrefix}{n}</c> (default <c>xlsx-row-{n}</c>), where <c>n</c>
    /// is the 1-based data-row ordinal — the convention predecessor row references use.
    /// </summary>
    /// <param name="stream">Readable XLSX stream.</param>
    /// <param name="mappings">Optional source-column to Gantt-property mappings.</param>
    /// <param name="options">Optional options, e.g. a resolver turning assignee names into people, or a per-import task-id prefix.</param>
    /// <returns>Tasks, dependencies and warnings.</returns>
    public static GanttImportResult ImportResult(Stream stream,
        IEnumerable<GanttColumnMapping>? mappings = null,
        GanttExcelImportOptions? options = null)
    {
        // The same source column may be mapped more than once by a host-seeded list; the first
        // mapping wins (a ToDictionary here used to throw on that input).
        var userMappings = new Dictionary<string, GanttColumnKey>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in mappings ?? [])
        {
            userMappings.TryAdd(mapping.SourceColumn, mapping.TargetProperty);
        }

        using var doc = SpreadsheetDocument.Open(stream, false);
        var wbp = doc.WorkbookPart ?? throw new InvalidOperationException("Missing WorkbookPart.");
        var rows = FirstSheetRows(wbp);
        if (rows.Count < 2)
        {
            return new GanttImportResult();
        }

        var sharedStrings = wbp.SharedStringTablePart?.SharedStringTable;
        var headers = ReadRow(rows[0], sharedStrings);
        var headerSheetRow = (int?)rows[0].RowIndex?.Value ?? 1;

        var warnings = new List<GanttImportWarning>();

        // Build the column-index → GanttColumnKey map. Empty headers map nothing, and a header used
        // by several columns maps its FIRST column only — later duplicates warn (DuplicateHeader).
        var colMap = new Dictionary<int, GanttColumnKey>();
        var mappedHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (string.IsNullOrWhiteSpace(header)) continue;
            if (!userMappings.TryGetValue(header, out var key)
                && !DefaultHeaders.TryGetValue(header, out key))
            {
                continue;
            }

            if (!mappedHeaders.Add(header))
            {
                warnings.Add(new GanttImportWarning
                {
                    Row = 0,
                    SheetRow = headerSheetRow,
                    Column = header,
                    Code = GanttImportWarningCode.DuplicateHeader,
                    Value = header,
                });
                continue;
            }

            colMap[i] = key;
        }

        var tasks = new List<TmWorkItem>();
        // 1-based data-row ordinal (1 = the first data row), the task of that row and its WBS code —
        // the lookup tables for the second pass.
        var rowByOrdinal = new Dictionary<int, TmWorkItem>();
        var ordinalByTask = new Dictionary<string, int>(StringComparer.Ordinal);
        var rowByWbs = new Dictionary<string, TmWorkItem>(StringComparer.OrdinalIgnoreCase);
        var wbsByTask = new Dictionary<string, string>(StringComparer.Ordinal);
        var sheetRowByTask = new Dictionary<string, int>(StringComparer.Ordinal);
        var wbsHeader = HeaderName(headers, colMap, GanttColumnKey.WBS);
        var predecessorHeader = HeaderName(headers, colMap, GanttColumnKey.Predecessor);
        var predecessorsByOrdinal = new Dictionary<int, (string Value, string? Column, int SheetRow)>();
        var idPrefix = string.IsNullOrWhiteSpace(options?.IdPrefix)
            ? GanttExcelImportOptions.DefaultIdPrefix
            : options!.IdPrefix;

        // ── Phase one: rows → tasks (stable ids, lookups, plain fields) ──
        for (var rowIdx = 1; rowIdx < rows.Count; rowIdx++)
        {
            var ordinal = rowIdx; // rows[1] is data row 1
            var sheetRow = (int?)rows[rowIdx].RowIndex?.Value ?? ordinal + 1;
            var cells = ReadRow(rows[rowIdx], sharedStrings);
            var task = new TmWorkItem { Id = $"{idPrefix}{ordinal}" };
            string? wbs = null;
            bool endParsed = false;
            double? durationDays = null;
            for (var col = 0; col < cells.Count; col++)
            {
                if (!colMap.TryGetValue(col, out var key)) continue;
                var val = cells[col];
                if (string.IsNullOrWhiteSpace(val)) continue; // an empty cell is "no data", never a warning
                switch (key)
                {
                    case GanttColumnKey.Title:
                        task.Title = val; break;
                    case GanttColumnKey.Start:
                        if (TryParseDate(val, out var s)) task.Start = s;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.End:
                        if (TryParseDate(val, out var e)) { task.End = e; endParsed = true; }
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Duration:
                        if (TryParseDays(val, out var duration)) durationDays = duration;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Deadline:
                        if (TryParseDate(val, out var due)) task.DueDate = due;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Estimation:
                        if (TryParseDays(val, out var estimation)) task.EstimationHours = estimation;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.TimeLog:
                        if (TryParseDays(val, out var logged)) task.LoggedHours = logged;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Progress:
                        if (TryParseProgress(val, out var p)) task.PercentComplete = Math.Clamp(p, 0, 100);
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Status:
                        if (Enum.TryParse<TmWorkItemStatus>(val, true, out var st)) task.Status = st;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.Priority:
                        if (Enum.TryParse<TmWorkItemPriority>(val, true, out var pr)) task.Priority = pr;
                        else WarnUnparsed(warnings, ordinal, sheetRow, headers[col], val);
                        break;
                    case GanttColumnKey.WBS:
                        wbs = val; break;
                    case GanttColumnKey.Predecessor:
                        predecessorsByOrdinal[ordinal] = (val, predecessorHeader, sheetRow); break;
                    case GanttColumnKey.Assignees:
                        task.Assignees = ParseAssignees(val, options?.AssigneeResolver); break;
                }
            }

            if (string.IsNullOrWhiteSpace(task.Title)) continue;

            // An End that was never mapped (or never parsed) falls back to Start + Duration, so a
            // sheet that stores the plan as a length still draws a bar.
            if (!endParsed && durationDays is { } days && task.Start != default(DateTime))
            {
                task.End = task.Start.AddDays(days);
            }

            tasks.Add(task);
            rowByOrdinal[ordinal] = task;
            ordinalByTask[task.Id] = ordinal;
            if (!string.IsNullOrWhiteSpace(wbs))
            {
                wbsByTask[task.Id] = wbs;
                sheetRowByTask[task.Id] = sheetRow;
                if (!rowByWbs.TryAdd(wbs, task))
                {
                    warnings.Add(new GanttImportWarning
                    {
                        Row = ordinal,
                        SheetRow = sheetRow,
                        Column = wbsHeader,
                        Code = GanttImportWarningCode.DuplicateWbs,
                        Value = wbs,
                    });
                }
            }
        }

        // ── Phase two: WBS → ParentId, predecessors → dependencies ──
        foreach (var task in tasks)
        {
            if (!wbsByTask.TryGetValue(task.Id, out var code)) continue;
            var cut = code.LastIndexOf('.');
            if (cut < 0) continue; // a root
            var parentCode = code[..cut];
            if (rowByWbs.TryGetValue(parentCode, out var parent))
            {
                task.ParentId = parent.Id;
            }
            else
            {
                warnings.Add(new GanttImportWarning
                {
                    Row = ordinalByTask[task.Id],
                    SheetRow = sheetRowByTask.GetValueOrDefault(task.Id, ordinalByTask[task.Id] + 1),
                    Column = wbsHeader,
                    Code = GanttImportWarningCode.UnknownWbsParent,
                    Value = parentCode,
                });
            }
        }

        var dependencies = new List<GanttDependency>();
        var seenEdges = new HashSet<(string FromId, string ToId, GanttDependencyType Type)>();
        foreach (var (ordinal, cell) in predecessorsByOrdinal.OrderBy(p => p.Key))
        {
            // A row whose title is empty produced no task (it is not imported); its predecessors
            // reference nothing and are simply dropped.
            if (!rowByOrdinal.TryGetValue(ordinal, out var task)) continue;
            var sheetRow = cell.SheetRow;
            foreach (var rawToken in cell.Value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var token = rawToken.Trim();
                if (token.Length == 0) continue;
                if (!TryParsePredecessor(token, out var reference, out var type, out var lagDays))
                {
                    warnings.Add(new GanttImportWarning
                    {
                        Row = ordinal,
                        SheetRow = sheetRow,
                        Column = cell.Column,
                        Code = GanttImportWarningCode.UnparsedValue,
                        Value = token,
                    });
                    continue;
                }

                var from = ResolveReference(reference, rowByOrdinal, rowByWbs);
                if (from is null)
                {
                    warnings.Add(new GanttImportWarning
                    {
                        Row = ordinal,
                        SheetRow = sheetRow,
                        Column = cell.Column,
                        Code = GanttImportWarningCode.UnknownPredecessor,
                        Value = token,
                    });
                    continue;
                }

                if (from.Id == task.Id)
                {
                    warnings.Add(new GanttImportWarning
                    {
                        Row = ordinal,
                        SheetRow = sheetRow,
                        Column = cell.Column,
                        Code = GanttImportWarningCode.SelfDependency,
                        Value = token,
                    });
                    continue;
                }

                // The edge that would close a cycle is skipped, not thrown: one bad link must not
                // cost the user the whole import.
                if (Reaches(dependencies, task.Id, from.Id))
                {
                    warnings.Add(new GanttImportWarning
                    {
                        Row = ordinal,
                        SheetRow = sheetRow,
                        Column = cell.Column,
                        Code = GanttImportWarningCode.CircularDependency,
                        Value = token,
                    });
                    continue;
                }

                // The same edge twice in one cell (or across rows) imports once.
                if (!seenEdges.Add((from.Id, task.Id, type))) continue;

                dependencies.Add(new GanttDependency
                {
                    FromId = from.Id,
                    ToId = task.Id,
                    DepType = type,
                    LagDays = lagDays,
                });
            }
        }

        return new GanttImportResult
        {
            Tasks = tasks,
            Dependencies = dependencies,
            Warnings = warnings,
        };
    }

    private static void WarnUnparsed(List<GanttImportWarning> warnings, int ordinal, int sheetRow,
        string? column, string value)
        => warnings.Add(new GanttImportWarning
        {
            Row = ordinal,
            SheetRow = sheetRow,
            Column = column,
            Code = GanttImportWarningCode.UnparsedValue,
            Value = value,
        });

    private static List<Row> FirstSheetRows(WorkbookPart wbp)
        => wbp.WorksheetParts.First().Worksheet.Descendants<Row>().ToList();

    private static string? HeaderName(List<string> headers, Dictionary<int, GanttColumnKey> colMap, GanttColumnKey key)
    {
        foreach (var (column, mapped) in colMap)
        {
            if (mapped == key) return headers[column];
        }

        return null;
    }

    private static List<TmWorkItemAssignee> ParseAssignees(string value, Func<string, TmWorkItemAssignee?>? resolver)
    {
        var assignees = new List<TmWorkItemAssignee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            var name = raw.Trim();
            if (name.Length == 0 || !seen.Add(name)) continue;
            var assignee = resolver?.Invoke(name);
            if (assignee is null)
            {
                // A supplied resolver that answers null explicitly does not know the name — skip it.
                // Without a resolver the normalized name is both the id and the display name.
                if (resolver is not null) continue;
                assignee = new TmWorkItemAssignee { Id = name, Name = name };
            }

            assignees.Add(assignee);
        }

        return assignees;
    }

    private static TmWorkItem? ResolveReference(string reference,
        Dictionary<int, TmWorkItem> rowByOrdinal,
        Dictionary<string, TmWorkItem> rowByWbs)
    {
        // A bare number is a sheet row per the MS Project convention (1 = the first data row);
        // anything dotted or lettered is a WBS code.
        if (reference.All(char.IsDigit))
        {
            return int.TryParse(reference, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal)
                ? rowByOrdinal.GetValueOrDefault(ordinal)
                : null;
        }

        return rowByWbs.GetValueOrDefault(reference);
    }

    private static bool TryParsePredecessor(string token, out string reference, out GanttDependencyType type, out int lagDays)
    {
        reference = token;
        type = GanttDependencyType.FinishToStart;
        lagDays = 0;

        // Lag: ±N with an optional 'd' suffix, always at the end ("3FS+2d", "4FF-1"). A token that
        // carries a sign which does not form such a lag ("3FS+2w", "3+2 days") is an unparsable
        // token — the caller reports UnparsedValue, never UnknownPredecessor.
        var sign = token.LastIndexOfAny(['+', '-']);
        if (sign >= 0)
        {
            var lag = token[sign..].TrimEnd('d', 'D');
            var digits = lag[1..];
            if (digits.Length == 0 || !digits.All(char.IsDigit)
                || !int.TryParse(lag, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out lagDays))
            {
                return false;
            }

            reference = token[..sign].TrimEnd();
        }

        // Type: FS / SS / FF / SF, case-insensitive, on the reference's tail ("1.2FS", "5ss").
        if (reference.Length >= 2)
        {
            var tail = reference[^2..].ToUpperInvariant();
            if (tail is "FS" or "SS" or "FF" or "SF")
            {
                type = tail switch
                {
                    "SS" => GanttDependencyType.StartToStart,
                    "FF" => GanttDependencyType.FinishToFinish,
                    "SF" => GanttDependencyType.StartToFinish,
                    _ => GanttDependencyType.FinishToStart,
                };
                reference = reference[..^2];
            }
        }

        return reference.Length > 0;
    }

    /// <summary>Whether <paramref name="fromId"/> already reaches <paramref name="toId"/> through the accepted edges.</summary>
    private static bool Reaches(List<GanttDependency> edges, string fromId, string toId)
    {
        if (fromId == toId) return true;
        var stack = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        stack.Push(fromId);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (current == toId) return true;
            if (!visited.Add(current)) continue;
            foreach (var edge in edges)
            {
                if (edge.FromId == current) stack.Push(edge.ToId);
            }
        }

        return false;
    }

    private static List<string> ReadRow(Row row, SharedStringTable? sst)
    {
        // Sparse sheets skip empty cells entirely; the CellReference ("C3") is the only reliable
        // column index — a positional append would slide every value after a gap one column left.
        var values = new List<string>();
        foreach (var cell in row.Descendants<Cell>())
        {
            var raw = cell.CellValue?.Text ?? "";
            if (cell.DataType?.Value == CellValues.SharedString && sst is not null &&
                int.TryParse(raw, out var idx))
                raw = sst.ElementAt(idx).InnerText;
            else if (cell.DataType?.Value == CellValues.InlineString)
                raw = cell.InlineString?.Text?.Text ?? raw;

            var column = ColumnIndex(cell.CellReference?.Value);
            if (column < 0)
            {
                values.Add(raw);
                continue;
            }

            while (values.Count < column) values.Add(string.Empty);
            if (column == values.Count) values.Add(raw);
            else values[column] = raw;
        }

        return values;
    }

    /// <summary>0-based column index of an A1-style reference ("C3" → 2); -1 when the reference is absent.</summary>
    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference)) return -1;
        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character)) break;
            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return index - 1;
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        value = value.Trim();
        string[] formats = ["yyyy-MM-dd", "dd.MM.yyyy", "MM/dd/yyyy", "yyyy-MM-ddTHH:mm:ss"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out result))
        {
            return true;
        }

        // A raw numeric cell carries an Excel serial date (days since 1899-12-30).
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial is >= -657435.0 and <= 2958465.0 // the range DateTime.FromOADate accepts
            && (result = DateTime.FromOADate(serial)) is { };
    }

    /// <summary>An invariant day / hour count ("5", "2.5"); the decimal point never follows the locale.</summary>
    private static bool TryParseDays(string value, out double days)
        => double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out days);

    /// <summary>
    /// Progress accepts "NN", "NN%" and Excel's stored 0–1 fraction scale (0.5 = 50 %, 1 = 100 %);
    /// type "NN%" to force an exact percent.
    /// </summary>
    private static bool TryParseProgress(string value, out int percent)
    {
        percent = 0;
        var text = value.Trim();
        var hasPercentSuffix = text.EndsWith('%');
        if (hasPercentSuffix) text = text[..^1].Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        if (!hasPercentSuffix && number is > 0 and <= 1)
        {
            number *= 100; // a fraction is Excel's percent scale, not a whole percent
        }

        percent = (int)Math.Round(number);
        return true;
    }
}
