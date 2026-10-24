using System.Text.RegularExpressions;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// The shape a wireframe stencil theme name must have (decision WIREFRAME-STENCIL-THEME): at most <see cref="MaxLength"/> characters from
/// <c>[A-Za-z0-9._-]</c>. The library treats any other name as the default tokens (no warning, nothing stored), and a host that takes a theme
/// from request input (a query string, an export body) should reject it with the same rule before it reaches a log or a renderer.
/// </summary>
public static partial class WireframeStencilThemeName
{
    /// <summary>The longest accepted theme name.</summary>
    public const int MaxLength = 64;

    [GeneratedRegex("^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>
    /// <c>true</c> when <paramref name="name"/> is unset (<c>null</c> / blank) or a well-formed theme name. Surrounding white space is ignored.
    /// Well-formed does not mean known: an unknown name is still valid and draws the default tokens.
    /// </summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        return trimmed.Length <= MaxLength && NamePattern().IsMatch(trimmed);
    }
}
