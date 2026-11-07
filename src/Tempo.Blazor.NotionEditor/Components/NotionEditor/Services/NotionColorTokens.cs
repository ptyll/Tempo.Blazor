namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Maps a stored block colour onto the data-palette token names. Documents saved before
/// Fáze 2 store the picker hex; new documents store the colour name. Both render through
/// <c>--tm-notion-color-*</c>, so the theme — not a baked hex — decides the colour.
/// </summary>
public static class NotionColorTokens
{
    private static readonly Dictionary<string, string> LegacyHex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#9b9a97"] = "gray",
        ["#64473a"] = "brown",
        ["#d9730d"] = "orange",
        ["#dfab01"] = "yellow",
        ["#0f7b6c"] = "green",
        ["#0b6e99"] = "blue",
        ["#6940a5"] = "purple",
        ["#ad1a72"] = "pink",
        ["#e03e3e"] = "red",
        ["#ebeced"] = "gray",
        ["#e9e5e3"] = "brown",
        ["#faebdd"] = "orange",
        ["#fbf3db"] = "yellow",
        ["#ddedea"] = "green",
        ["#ddebf1"] = "blue",
        ["#eae4f2"] = "purple",
        ["#f4dfeb"] = "pink",
        ["#fbe4e4"] = "red",
    };

    /// <summary>
    /// The one hex <c>document.execCommand</c> may receive. A CSS colour name is not a colour
    /// the browser keeps across save and reload, so the toolbar paints from the token and sends
    /// this allowlisted pair. Nothing else in the package may invent a hex.
    /// </summary>
    private static readonly Dictionary<string, (string Text, string Background)> InlinePalette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gray"] = ("#9b9a97", "#ebeced"),
        ["brown"] = ("#64473a", "#e9e5e3"),
        ["orange"] = ("#d9730d", "#faebdd"),
        ["yellow"] = ("#dfab01", "#fbf3db"),
        ["green"] = ("#0f7b6c", "#ddedea"),
        ["blue"] = ("#0b6e99", "#ddebf1"),
        ["purple"] = ("#6940a5", "#eae4f2"),
        ["pink"] = ("#ad1a72", "#f4dfeb"),
        ["red"] = ("#e03e3e", "#fbe4e4"),
    };

    /// <summary>The palette name (<c>red</c>) for a stored colour, or the value unchanged.</summary>
    public static string? Name(string? stored) =>
        stored is not null && LegacyHex.TryGetValue(stored, out var name) ? name : stored;

    /// <summary>
    /// Legacy hex for a palette name, or null when the name is not on the allowlist.
    /// <paramref name="background"/> selects the wash; otherwise the ink.
    /// </summary>
    public static string? InlineHex(string? name, bool background)
    {
        if (name is null || !InlinePalette.TryGetValue(name, out var pair))
        {
            return null;
        }

        return background ? pair.Background : pair.Text;
    }

    /// <summary>
    /// A select/status option colour as <c>var(--tm-notion-option-{name}-solid)</c>.
    /// A legacy hex maps through <see cref="Name"/>; anything else falls back to the theme primary.
    /// </summary>
    public static string OptionSolid(string? stored) => OptionToken(stored, "solid") ?? "var(--tm-color-primary)";

    /// <summary>The option wash (<c>--tm-notion-option-{name}-bg</c>), or the muted surface.</summary>
    public static string OptionWash(string? stored) => OptionToken(stored, "bg") ?? "var(--tm-bg-muted)";

    /// <summary>The option ink (<c>--tm-notion-option-{name}-fg</c>), or the secondary text.</summary>
    public static string OptionInk(string? stored) => OptionToken(stored, "fg") ?? "var(--tm-text-secondary)";

    private static string? OptionToken(string? stored, string suffix)
    {
        var name = Name(stored);
        return name is not null && InlinePalette.ContainsKey(name)
            ? $"var(--tm-notion-option-{name.ToLowerInvariant()}-{suffix})"
            : null;
    }
}
