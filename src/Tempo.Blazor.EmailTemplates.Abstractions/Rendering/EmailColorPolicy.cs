using System.Text.RegularExpressions;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The colour allow-list of the model colours that reach the markup as an attribute (§3.7a):
/// <c>#rgb</c>, <c>#rrggbb</c> and a named colour from a small, fixed safe set.
/// <para>
/// Deliberately narrow: the value is written into <c>bgcolor</c> of a table cell, and a CSS function
/// (<c>rgb()</c>, <c>url()</c>, <c>expression()</c>, <c>var()</c>) or a quote would let a crafted string
/// leave the attribute or fetch a remote resource. A value outside the list is not "sanitized" into
/// something else — it is dropped, so the render can report it and the markup never carries it.
/// </para>
/// </summary>
public static class EmailColorPolicy
{
    /// <summary>
    /// The named colours the pipeline accepts. Lower-case CSS colour keywords only — no function syntax,
    /// no quotes, no whitespace, so the value cannot be anything but a colour token.
    /// </summary>
    private static readonly HashSet<string> SafeNamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "aqua", "black", "blue", "darkgray", "darkgrey", "dimgray", "dimgrey", "fuchsia", "gainsboro",
        "gray", "green", "grey", "lightgray", "lightgrey", "lime", "maroon", "navy", "olive", "orange",
        "purple", "red", "silver", "teal", "transparent", "white", "whitesmoke", "yellow",
    };

    // Anchored, fixed-length alternatives: no backtracking, so a hostile value costs no time.
    private static readonly Regex HexPattern = new(
        "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Regex NamedPattern = new(
        "^[a-zA-Z]{3,16}$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Answers whether <paramref name="color"/> may be written into the markup. Empty (and whitespace) is
    /// not a colour: the caller omits the attribute instead.
    /// </summary>
    public static bool IsAllowed(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;
        var trimmed = color.Trim();
        return HexPattern.IsMatch(trimmed) || (NamedPattern.IsMatch(trimmed) && SafeNamedColors.Contains(trimmed));
    }
}
