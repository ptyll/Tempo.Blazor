namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One model a host offers in <c>TmModelPicker</c>: its identity, its price per million tokens, the size of
/// its context window, the relative speed and quality scores the picker draws as bars, its capabilities and
/// the reasoning options it supports.
/// <para>
/// The record is GENERIC on purpose — it is a snapshot of the host's own model catalogue, not a binding to a
/// provider SDK. Every non-identity property is optional: a host that only knows ids and names still gets a
/// usable picker, and the picker renders only the facts a model actually reports (an unreported context window
/// is an absent chip, never a zero).
/// </para>
/// <para>
/// Prices are quoted PER MILLION TOKENS in the host's currency, because that is how model catalogues are
/// published and compared. The picker formats them with the culture's currency (see
/// <c>TmModelPicker.Culture</c>) and scales the price bar against the most expensive model IN THE LIST, so the
/// bar answers "how expensive is this next to my alternatives" rather than "how many dollars is this".
/// </para>
/// </summary>
public sealed record AiModelInfo
{
    /// <summary>Creates a model.</summary>
    /// <param name="id">Stable identity of the model — what <see cref="AiModelSelection.ModelId"/> stores and
    /// what the host sends to its provider. Null or whitespace generates one, so two entries of an unnamed
    /// model are still two rows and neither can collide with the other.</param>
    /// <param name="name">The model as the reader should see it. Null or whitespace falls back to
    /// <see cref="Id"/>, so no row is ever blank.</param>
    public AiModelInfo(string id, string? name = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Name = string.IsNullOrWhiteSpace(name) ? Id : name!;
    }

    /// <summary>Stable identity of the model, sent to the provider. Never empty: a missing id is generated at
    /// construction.</summary>
    public string Id { get; init; }

    /// <summary>The model as the reader should see it. Never blank: it falls back to <see cref="Id"/>.</summary>
    public string Name { get; init; }

    /// <summary>
    /// The vendor or gateway the model is called through ("OpenAI", "Anthropic", "Azure"). Null means the
    /// catalogue does not distinguish providers; <see cref="AiModelGroupBy.Provider"/> then puts the model in
    /// the localized "Other" group, and the row renders no provider line.
    /// </summary>
    public string? Provider { get; init; }

    /// <summary>
    /// The model generation or line ("GPT-4", "Claude 3.5"). Null means the catalogue does not report one;
    /// <see cref="AiModelGroupBy.Family"/> then puts the model in the localized "Other" group.
    /// </summary>
    public string? Family { get; init; }

    /// <summary>An optional one-line explanation of what the model is good at. Null renders no description.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// How many tokens the model can hold in one conversation, in TOKENS. Null means "not reported" and
    /// renders no context-window chip; a reported value renders as a short form ("200k", "1M") captioned as
    /// a context window ("200k context"). The row carries no full-precision number.
    /// </summary>
    public int? ContextWindow { get; init; }

    /// <summary>
    /// The catalogue's relative speed score on a 0–5 scale (5 is the fastest model the host knows). Null
    /// means "not reported" and renders no speed bar. The picker clamps the value into 0–5 and draws the bar
    /// on that absolute 0–5 scale.
    /// </summary>
    public double? SpeedScore { get; init; }

    /// <summary>
    /// The catalogue's relative quality score on a 0–5 scale (5 is the strongest model the host knows). Null
    /// means "not reported" and renders no quality bar. The picker clamps the value into 0–5 and draws the
    /// bar on that absolute 0–5 scale.
    /// </summary>
    public double? QualityScore { get; init; }

    /// <summary>
    /// Price of one million INPUT tokens, in the host's currency. Null means "not reported" and renders no
    /// price bar or value. The picker never treats an unknown price as free.
    /// </summary>
    public decimal? InputPricePerMillion { get; init; }

    /// <summary>
    /// Price of one million OUTPUT tokens, in the host's currency. Informational: carried for the host's own
    /// use (persistence, the host's own UI); the built-in row does not render it — the price bar and its
    /// per-million value describe the INPUT price. Null means "not reported".
    /// </summary>
    public decimal? OutputPricePerMillion { get; init; }

    /// <summary>
    /// Price of one million CACHED INPUT tokens, in the host's currency. Informational: carried for the host's
    /// own use (persistence, the host's own UI); the built-in row does not render it. Null means
    /// "not reported".
    /// </summary>
    public decimal? CachedInputPricePerMillion { get; init; }

    /// <summary>
    /// What the model can do ("vision", "tools", "json"), as the reader should see it — the host supplies its
    /// own wording and the picker renders each entry as a text chip. Null or empty renders no chips; blank
    /// entries are dropped instead of painting an empty pill.
    /// </summary>
    public IReadOnlyList<string>? Capabilities { get; init; }

    /// <summary>
    /// The reasoning-effort levels the model offers, in the order they should be presented (usually cheapest
    /// first). Null or empty means the model has no effort control and the picker renders none; a selection
    /// that stores an effort level for such a model keeps the stored value untouched — the host owns the
    /// meaning.
    /// </summary>
    public IReadOnlyList<AiModelEffortLevel>? EffortLevels { get; init; }

    /// <summary>
    /// Whether the model accepts an "extended thinking" switch. Default <c>false</c>, so a host never gets a
    /// toggle its model does not support. The switch is rendered for the SELECTED model only.
    /// </summary>
    public bool SupportsExtendedThinking { get; init; }

    /// <summary>
    /// Whether the catalogue marks the model as deprecated. A deprecated model is still selectable (a host may
    /// have it pinned in an existing conversation) but its row and the trigger carry the localized
    /// "Deprecated" chip, so the choice is never silent.
    /// </summary>
    public bool IsDeprecated { get; init; }

    /// <summary>
    /// Whether the model offers the reasoning-effort control (it reports at least one level with a usable
    /// id).
    /// </summary>
    public bool HasEffortLevels => EffortLevels is { Count: > 0 } levels
        && levels.Any(level => !string.IsNullOrWhiteSpace(level.Id));
}
