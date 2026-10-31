using System.Globalization;
using System.Text;
using System.Text.Json;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.AI.Formatting;

/// <summary>
/// Pretty-prints the raw JSON a tool call carries (its arguments and its result) for the
/// <c>TmToolCallCard</c> disclosure.
/// </summary>
/// <remarks>
/// The payload is model output, so it is parsed, never trusted: a document that does not parse is
/// reported as a failure and the card shows the raw text instead. Dropping an unparseable payload would
/// hide exactly the malformed call a reader needs to see, and parsing it into a typed shape would throw
/// on the first unexpected property.
/// </remarks>
internal static class AiJsonPrettyPrinter
{
    private const int MaximumDepth = 64;

    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        // Model-emitted JSON is frequently *almost* JSON: a trailing comma before a closing brace and a
        // // comment are the two shapes every provider has produced at least once. Both are accepted, so
        // the reader gets the structure instead of a wall of raw text.
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = MaximumDepth,
    };

    /// <summary>Formats <paramref name="json"/> with two-space indentation.</summary>
    /// <param name="json">The raw payload.</param>
    /// <param name="formatted">The indented document; empty when the payload is missing or invalid.</param>
    /// <returns><see langword="true"/> when the payload parsed and was formatted.</returns>
    public static bool TryFormat(string? json, out string formatted)
    {
        formatted = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, ParseOptions);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                document.RootElement.WriteTo(writer);
            }

            formatted = Encoding.UTF8.GetString(buffer.ToArray());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // A payload the reader cannot decode as UTF-16 text is still worth showing verbatim.
            return false;
        }
    }
}

/// <summary>
/// Renders an elapsed time as a short, localized duration — the "4.2 s" of a tool-call card and the
/// "4,2 s" of the Czech grouping summary come from here, so the two never disagree about the unit or
/// the decimal separator.
/// </summary>
/// <remarks>
/// The number is formatted in the localizer's culture and handed to the resource template as a string,
/// which keeps the placeholder contract of the resource files to a plain <c>{0}</c> and stops
/// <see cref="string.Format(IFormatProvider, string, object[])"/> from re-formatting it in a different
/// culture than the one the localizer serves.
/// </remarks>
internal static class AiDurationFormatter
{
    /// <summary>Formats <paramref name="duration"/> for display.</summary>
    /// <param name="duration">The elapsed time; a negative value is clamped to zero.</param>
    /// <param name="localizer">Source of the duration templates and of the number format.</param>
    /// <returns>A localized, human-scale duration ("0.4 s", "4.2 s", "1 min 15 s", "1 h 5 min").</returns>
    public static string Format(TimeSpan duration, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var culture = localizer.Culture ?? CultureInfo.CurrentUICulture;

        if (duration < TimeSpan.FromMinutes(1))
        {
            return localizer["TmAiDuration_Seconds", duration.TotalSeconds.ToString("0.0", culture)];
        }

        if (duration < TimeSpan.FromHours(1))
        {
            return localizer["TmAiDuration_Minutes",
                ((int)duration.TotalMinutes).ToString(culture),
                duration.Seconds.ToString(culture)];
        }

        return localizer["TmAiDuration_Hours",
            ((int)duration.TotalHours).ToString(culture),
            duration.Minutes.ToString(culture)];
    }
}
