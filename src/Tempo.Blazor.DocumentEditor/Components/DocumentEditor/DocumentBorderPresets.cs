namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// The border presets of the table and cell inspectors (redesign 146c7e1b, phase 6, DE-2.2): <c>None</c>, <c>Default</c>, <c>Strong</c>
/// and <c>Custom</c>. A preset writes exactly the CSS string the select wrote before the redesign, so stored documents are unchanged;
/// any other stored value (a DOCX border such as <c>0.5pt solid #000000</c>) is shown as <c>Custom</c> and left alone until the
/// reader picks a preset.
/// </summary>
public static class DocumentBorderPresets
{
    /// <summary>The preset key of "no border" (stored as an empty border string).</summary>
    public const string None = "none";

    /// <summary>The preset key of the default border.</summary>
    public const string Default = "default";

    /// <summary>The preset key of the strong border.</summary>
    public const string Strong = "strong";

    /// <summary>The display-only preset key of a border that matches no preset (an imported value).</summary>
    public const string Custom = "custom";

    /// <summary>The CSS string of the default border.</summary>
    public const string DefaultCss = "1px solid var(--tm-color-border)";

    /// <summary>The CSS string of the strong border.</summary>
    public const string StrongCss = "2px solid var(--tm-color-primary)";

    /// <summary>The preset a stored border string reads as: empty is <see cref="None"/>, the two known strings are their presets, anything else is <see cref="Custom"/>.</summary>
    public static string Resolve(string? css)
    {
        var trimmed = css?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return None;
        }

        if (string.Equals(trimmed, DefaultCss, StringComparison.Ordinal))
        {
            return Default;
        }

        return string.Equals(trimmed, StrongCss, StringComparison.Ordinal) ? Strong : Custom;
    }

    /// <summary>The CSS string a writable preset stores; <c>false</c> for <see cref="Custom"/> and for an unknown key (nothing to write).</summary>
    public static bool TryGetCss(string? preset, out string css)
    {
        switch (preset)
        {
            case None:
                css = string.Empty;
                return true;
            case Default:
                css = DefaultCss;
                return true;
            case Strong:
                css = StrongCss;
                return true;
            default:
                css = string.Empty;
                return false;
        }
    }
}
