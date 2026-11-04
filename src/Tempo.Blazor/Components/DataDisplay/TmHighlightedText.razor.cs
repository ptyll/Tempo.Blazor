using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Renders <see cref="Text"/> with every occurrence of <see cref="Query"/> wrapped in
/// <c>&lt;mark class="tm-highlighted-text__match"&gt;</c>, so a reader of a combobox row can see WHERE the
/// search term hit instead of re-reading the row to find it.
/// <para>
/// It renders a FRAGMENT — no wrapper element — because the call sites are option rows, table cells and
/// picker lines that already own their box and their typography. The rendered text is exactly the text that
/// was passed in: `<mark>` is an inline wrapper around a slice of it, so a copy/paste and the accessible
/// name both get the original string with no duplicated content.
/// </para>
/// <para>
/// The query is matched LITERALLY (never as a regular expression), case-insensitively by default, and —
/// unless <see cref="AccentInsensitive"/> is turned off — ignoring diacritics, so "usti" finds "Ústí" and the
/// marked run is the original accented text. A null, empty or whitespace-only query renders the text
/// unchanged, which makes the component safe to render unconditionally while the user is still typing.
/// </para>
/// <para>
/// This component is a presentation detail of a search, not a live region: it never announces anything. The
/// combobox that owns the list already announces the search state, and a second announcement per row would
/// be noise.
/// </para>
/// </summary>
public partial class TmHighlightedText : ComponentBase
{
    private IReadOnlyList<HighlightedTextSegment> _segments = [];

    /// <summary>The text to render. Rendered as text — never interpreted as markup.</summary>
    [Parameter] public string? Text { get; set; }

    /// <summary>
    /// The term to highlight, matched literally. Null, empty or whitespace-only means "no match" and the
    /// text renders unchanged. Leading and trailing whitespace is trimmed before matching.
    /// </summary>
    [Parameter] public string? Query { get; set; }

    /// <summary>
    /// When <c>true</c>, casing matters. Default <c>false</c> — a search box that required the exact casing
    /// of the data would be unusable.
    /// </summary>
    [Parameter] public bool CaseSensitive { get; set; }

    /// <summary>
    /// When <c>true</c> (the default), diacritics are ignored on BOTH sides: "usti" matches "Ústí" and
    /// "Ústí" matches "Usti". The marked run is always the original text. Turn it off when the distinction
    /// between "s" and "š" carries meaning for the host's data.
    /// </summary>
    [Parameter] public bool AccentInsensitive { get; set; } = true;

    /// <summary>
    /// Extra class(es) added to every <c>&lt;mark&gt;</c>, on top of the component's own
    /// <c>tm-highlighted-text__match</c>.
    /// </summary>
    [Parameter] public string? MatchClass { get; set; }

    /// <summary>The class attribute of one <c>&lt;mark&gt;</c>.</summary>
    private string MatchCssClass =>
        string.IsNullOrWhiteSpace(MatchClass)
            ? "tm-highlighted-text__match"
            : $"tm-highlighted-text__match {MatchClass}";

    /// <summary>The runs of the last render: plain text and matched text, in reading order.</summary>
    private IReadOnlyList<HighlightedTextSegment> Segments => _segments;

    /// <inheritdoc />
    protected override void OnParametersSet()
        => _segments = HighlightedTextMatcher.Split(Text, Query, CaseSensitive, AccentInsensitive);
}
