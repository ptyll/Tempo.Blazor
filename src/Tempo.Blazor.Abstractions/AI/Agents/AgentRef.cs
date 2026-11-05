using System.Globalization;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One agent, as every agent visualization names it: the identity a host keys its data by, the display name a
/// reader sees, the optional role and avatar, and the slot in the eight-colour agent palette.
/// <para>
/// The record is a VALUE — a host recomposes it with <c>agent with { … }</c> when a display name or a colour
/// changes, and the components never mutate it. It carries no state: activity lives in
/// <see cref="AgentActivityStatus"/> per block, node or tile, because the same agent can be idle in one lane and
/// running in another.
/// </para>
/// <para>
/// <see cref="ColorIndex"/> is normalised into the palette on assignment, so it always names a declared
/// <c>--tm-agent-color-{0..7}</c> token. Eight distinct slots are the design: two agents sharing a colour in one
/// visualization would be indistinguishable, and a ninth colour would have no dark counterpart to keep the ink
/// readable.
/// </para>
/// </summary>
public sealed record AgentRef
{
    /// <summary>How many slots the agent palette has: <c>--tm-agent-color-0</c> … <c>--tm-agent-color-7</c>.</summary>
    public const int PaletteSize = 8;

    private int _colorIndex;

    /// <summary>Creates an agent reference.</summary>
    /// <param name="name">The display name the reader sees. Null or whitespace is stored as an empty name, which
    /// a component renders through its localized fallback rather than as an empty gap.</param>
    /// <param name="id">Stable identity used as the <c>@key</c> and as the value a selection reports. Null or
    /// whitespace generates one, so two agents with the same name are still two agents.</param>
    public AgentRef(string name, string? id = null)
    {
        Name = name ?? string.Empty;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
    }

    /// <summary>Stable identity of the agent. Never empty: a missing id is generated at construction.</summary>
    public string Id { get; init; }

    /// <summary>The display name, as the host wants it read. Empty when the host supplied no name.</summary>
    public string Name { get; init; }

    /// <summary>
    /// What the agent is for — "Orchestrator", "Reviewer", "Worker" — in the host's own words. Null renders no role
    /// line, which is not the same as an empty one.
    /// </summary>
    public string? Role { get; init; }

    /// <summary>
    /// An absolute URL of the agent's picture. Null renders <see cref="Initials"/> on the palette colour. A component
    /// loads it only when it is an absolute <c>http</c>/<c>https</c> source — the package's image allowlist — and
    /// falls back to <see cref="Initials"/> for anything else, because the picture is host data and a refused target
    /// must never become a live source. Components render the picture as a decorative image (empty <c>alt</c>),
    /// because the agent's name is always next to it.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// The slot in the agent palette. Any integer is accepted and normalised into
    /// <c>0 … PaletteSize - 1</c> (wrapping, negative values included), so a host that counts its agents past eight
    /// still lands on a declared token instead of an undeclared custom property that resolves to nothing.
    /// </summary>
    public int ColorIndex
    {
        get => _colorIndex;
        init => _colorIndex = NormalizeColorIndex(value);
    }

    /// <summary>Whether the host supplied a name. A component renders its localized fallback when it is false.</summary>
    public bool HasName => !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// The one or two letters the avatar paints when there is no picture: the first letter of the first word and of
    /// the last word ("Data analyst" → "DA", "Bedřich Novák" → "BN"), a single letter for a single word, and
    /// <c>"?"</c> when the name has no letter at all — an avatar must never be an empty circle. Diacritics and
    /// surrogate pairs are kept as whole text elements, so a Czech or an emoji name cannot render half a glyph.
    /// </summary>
    public string Initials
    {
        get
        {
            var words = Name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                return "?";
            }

            var first = FirstTextElement(words[0]);
            if (words.Length == 1)
            {
                return first;
            }

            return first + FirstTextElement(words[^1]);
        }
    }

    /// <summary>
    /// The custom property <see cref="ColorIndex"/> points at, without the <c>var()</c> wrapper — the name a host
    /// passes to <c>var(…)</c> or writes into an inline style. Always one of the declared palette tokens.
    /// </summary>
    public string ColorToken => PaletteToken(ColorIndex);

    /// <summary>The palette token of a slot, normalised the same way <see cref="ColorIndex"/> is.</summary>
    /// <param name="colorIndex">Any integer; it wraps into the palette.</param>
    /// <returns>e.g. <c>--tm-agent-color-3</c>.</returns>
    public static string PaletteToken(int colorIndex)
        => $"--tm-agent-color-{NormalizeColorIndex(colorIndex).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Wraps any integer into <c>0 … PaletteSize - 1</c>, negatives included.</summary>
    /// <param name="colorIndex">Any integer.</param>
    public static int NormalizeColorIndex(int colorIndex)
        => ((colorIndex % PaletteSize) + PaletteSize) % PaletteSize;

    /// <summary>The first text element of a word, upper-cased — one glyph of the user's own name.</summary>
    private static string FirstTextElement(string word)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(word);
        if (!enumerator.MoveNext())
        {
            return "?";
        }

        var element = enumerator.GetTextElement();
        return element.ToUpperInvariant();
    }
}
