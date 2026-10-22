using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The shape of a colour value the pipeline may WRITE: <c>#rgb</c>/<c>#rgba</c>/<c>#rrggbb</c>/<c>#rrggbbaa</c>,
/// an <c>rgb()</c>/<c>rgba()</c>/<c>hsl()</c>/<c>hsla()</c> function of numbers, percentages and separators, or a
/// plain CSS keyword.
/// </summary>
/// <remarks>
/// The value of a colour attribute is copied into the message's <c>style</c> by the MJML compiler, so a value
/// carrying <c>;</c>, a quote, a <c>url(…)</c> or a template token stops being a colour and becomes another
/// declaration. Both ends of the pipeline use this ONE predicate: the generator refuses to write a value that
/// does not match, and the importer reports the ones it had to keep in the model.
/// </remarks>
internal static partial class CssColourValue
{
    /// <summary>Answers whether <paramref name="value"/> is a colour the pipeline may emit.</summary>
    public static bool IsPlausible(string? value)
        => !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim());

    [GeneratedRegex(
        @"^(#[0-9a-fA-F]{3,8}|rgba?\(\s*[0-9.%]+(\s*,\s*[0-9.%]+){2,3}\s*\)|hsla?\(\s*[0-9.%]+(\s*,\s*[0-9.%]+){2,3}\s*\)|[a-zA-Z]{3,24})$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pattern();
}
