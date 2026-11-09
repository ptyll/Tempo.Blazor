using System.Collections;
using System.Globalization;
using System.Text.Json;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

internal static class NotionDatabaseValueFormatter
{
    /// <summary>
    /// The display text of a record's primary field. Every reading view used to keep its own copy;
    /// one helper keeps a date, a list and an empty value formatted the same way.
    /// </summary>
    public static string PrimaryText(IDatabaseRecord record, IEnumerable<IDatabaseField> fields, string dateFormat = "yyyy-MM-dd")
    {
        var primary = fields.FirstOrDefault(field => field.IsPrimary);
        if (primary is null) return string.Empty;
        return record.Fields.TryGetValue(primary.Id.ToString(), out var value)
            ? Format(value, dateFormat)
            : string.Empty;
    }

    public static string Format(object? value, string dateFormat = "yyyy-MM-dd")
    {
        return value switch
        {
            null           => string.Empty,
            string s       => FormatDateString(s, dateFormat),
            bool b         => b ? "✓" : string.Empty,
            double d       => d.ToString("G", CultureInfo.CurrentCulture),
            float f        => f.ToString("G", CultureInfo.CurrentCulture),
            decimal d      => d.ToString("G", CultureInfo.CurrentCulture),
            int i          => i.ToString(CultureInfo.CurrentCulture),
            long l         => l.ToString(CultureInfo.CurrentCulture),
            DateTime dt    => dt.ToString(dateFormat, CultureInfo.CurrentCulture),
            DateTimeOffset dto => dto.ToString(dateFormat, CultureInfo.CurrentCulture),
            DateOnly d     => d.ToString(dateFormat, CultureInfo.CurrentCulture),
            JsonElement je => FormatJsonElement(je, dateFormat),
            IEnumerable e  => FormatEnumerable(e, dateFormat),
            _              => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };
    }

    /// <summary>
    /// An ISO date stored as text (the demo API round-trips <see cref="DateTime"/> as a string)
    /// formats with the caller's pattern and <see cref="CultureInfo.CurrentCulture"/>. Anything
    /// else, including a title that merely starts with digits, stays as stored.
    /// </summary>
    private static string FormatDateString(string? text, string dateFormat)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return TryParseDate(text, out var parsed)
            ? parsed.ToString(dateFormat, CultureInfo.CurrentCulture)
            : text;
    }

    private static bool TryParseDate(string text, out DateTime parsed)
    {
        parsed = default;
        if (text.Length < 10 || text[4] != '-' || text[7] != '-') return false;
        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces,
            out parsed);
    }

    private static string FormatEnumerable(IEnumerable values, string dateFormat)
    {
        var parts = values
            .Cast<object?>()
            .Select(value => Format(value, dateFormat))
            .Where(value => value.Length > 0);

        return string.Join(", ", parts);
    }

    private static string FormatJsonElement(JsonElement element, string dateFormat)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null      => string.Empty,
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.String    => FormatDateString(element.GetString(), dateFormat),
            JsonValueKind.True      => "✓",
            JsonValueKind.False     => string.Empty,
            JsonValueKind.Number    => element.TryGetInt64(out var l)
                ? l.ToString(CultureInfo.CurrentCulture)
                : element.TryGetDouble(out var d)
                    ? d.ToString("G", CultureInfo.CurrentCulture)
                    : element.GetRawText(),
            JsonValueKind.Array => FormatEnumerable(element.EnumerateArray(), dateFormat),
            _                   => element.GetRawText()
        };
    }
}
