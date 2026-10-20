using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Parses raw CSV text and auto-detects the delimiter.</summary>
public static class CsvParser
{
    private static readonly char[] DelimiterCandidates = [',', ';', '\t'];
    private const int MaxRows = 5000;

    /// <summary>Maximum accepted input length (512 KB); a larger paste is rejected up front.</summary>
    internal const int MaxInputLength = 512 * 1024;

    /// <summary>
    /// Parses CSV text and returns headers + rows.
    /// Throws <see cref="DiagramImportException"/> if the input is empty, has no data rows or
    /// fewer than two columns, or exceeds <see cref="MaxInputLength"/> (too complex to import).
    /// The exception messages are English developer text — UIs map
    /// <see cref="DiagramImportException.Error"/> to a localized message, never <see cref="Exception.Message"/>.
    /// </summary>
    public static CsvParseResult Parse(string csvText)
    {
        if (string.IsNullOrWhiteSpace(csvText))
            throw new DiagramImportException(DiagramImportError.CsvEmpty, "The CSV input is empty.");

        if (csvText.Length > MaxInputLength)
            throw new DiagramImportException(DiagramImportError.InputTooComplex,
                $"The CSV input exceeds the maximum length of {MaxInputLength / 1024} KB.");

        var trimmed = csvText.Trim();
        var delimiter = DetectDelimiter(trimmed);
        var records = ReadRecords(trimmed, delimiter);

        if (records.Count < 2)
            throw new DiagramImportException(DiagramImportError.CsvNoData, "The CSV input has a header but no data rows.");

        var headers = records[0];
        if (headers.Count < 2)
            throw new DiagramImportException(DiagramImportError.CsvMinColumns, "The CSV header has fewer than two columns.");

        var rows = records.Skip(1).Take(MaxRows).ToList();

        return new CsvParseResult
        {
            Headers = headers,
            Rows = rows,
            DetectedDelimiter = delimiter
        };
    }

    private static char DetectDelimiter(string text)
    {
        char bestDelimiter = ',';
        int bestScore = -1;

        foreach (var delimiter in DelimiterCandidates)
        {
            var score = EvaluateDelimiter(text, delimiter);
            if (score > bestScore)
            {
                bestScore = score;
                bestDelimiter = delimiter;
            }
        }

        return bestDelimiter;
    }

    private static int EvaluateDelimiter(string text, char delimiter)
    {
        try
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = delimiter.ToString(),
                BadDataFound = null,
                HasHeaderRecord = false,
                TrimOptions = TrimOptions.Trim
            };

            using var reader = new StringReader(text);
            using var csv = new CsvReader(reader, config);

            var fieldCounts = new List<int>();
            while (csv.Read())
            {
                fieldCounts.Add(csv.Parser.Count);
            }

            if (fieldCounts.Count == 0)
                return -1;

            // Prefer delimiters that produce consistent column counts >= 2
            var mode = fieldCounts.GroupBy(x => x).OrderByDescending(g => g.Count()).First().Key;
            if (mode < 2)
                return -1;

            var consistency = fieldCounts.Count(x => x == mode);
            return consistency;
        }
        catch
        {
            return -1;
        }
    }

    private static List<List<string>> ReadRecords(string text, char delimiter)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            BadDataFound = null,
            HasHeaderRecord = false,
            TrimOptions = TrimOptions.Trim
        };

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, config);

        var records = new List<List<string>>();
        while (csv.Read())
        {
            var row = new List<string>();
            for (int i = 0; i < csv.Parser.Count; i++)
            {
                row.Add(csv.GetField(i) ?? string.Empty);
            }
            records.Add(row);
        }

        return records;
    }
}
