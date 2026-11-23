using System.Globalization;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Culture-invariant wire form of a typed option value, shared by
/// <c>TmSelect</c> and native radio <c>value</c> attributes.
/// </summary>
internal static class OptionValueFormatter
{
    /// <summary>
    /// Formats <paramref name="value"/> for an HTML <c>value</c> attribute.
    /// Null becomes <c>""</c>. Date/time types use ISO round-trip forms;
    /// <see cref="bool"/> is <c>true</c>/<c>false</c>; other
    /// <see cref="IFormattable"/> types use <see cref="CultureInfo.InvariantCulture"/>.
    /// Used by <c>TmSelect</c> and native radio <c>value</c> — not by segmented
    /// <c>data-value</c> (see <see cref="FormatSegmented{TValue}"/>).
    /// </summary>
    public static string Format<TValue>(TValue? value) => value switch
    {
        null => string.Empty,
        bool flag => flag ? "true" : "false",
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>
    /// Formats a segmented <c>data-value</c>. Existing types keep
    /// <see cref="object.ToString"/> so a <see cref="bool"/> stays
    /// <c>True</c>/<c>False</c> (the documented DOM contract and the
    /// TimeInput/E2E selectors). Date/time types that had no historical
    /// wire form use the invariant ISO round-trip.
    /// </summary>
    public static string FormatSegmented<TValue>(TValue? value) => value switch
    {
        null => string.Empty,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("O", CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
