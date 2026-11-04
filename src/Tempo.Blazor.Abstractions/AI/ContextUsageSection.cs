namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One part of a context window — the system prompt, the tool definitions, the memory, the attachments, the
/// conversation, the tool results — with how many tokens it occupies, whether it is required, whether the
/// provider has already summarised or dropped it, and the entries it is made of.
/// <para>
/// <see cref="Tokens"/> is the section's authoritative total; when it reports nothing the section's tokens are
/// the sum of what its <see cref="Items"/> report (see <see cref="ContextUsageMath.TokensOf"/>). The three state
/// words are independent facts, not one enum: a summarised section can still be required, and an omitted
/// section still keeps its row so the reader can see what is no longer being sent.
/// </para>
/// </summary>
/// <param name="Key">Stable identifier of the section (the DOM key, the expansion state and the removal report all use it).</param>
/// <param name="Label">The reader-facing name of the section, rendered as text and never localised by the library.</param>
public sealed record ContextUsageSection(string Key, string Label)
{
    /// <summary>
    /// The kind of content the section holds. Default <see cref="ContextUsageKind.Other"/>. It selects the
    /// section's tone only — the label stays the host's wording.
    /// </summary>
    public ContextUsageKind Kind { get; init; } = ContextUsageKind.Other;

    /// <summary>
    /// How many tokens the section occupies. Default <c>0</c>; a negative value counts as nothing. When this is
    /// <c>0</c> and <see cref="Items"/> report tokens, those are the section's measurement instead.
    /// </summary>
    public int Tokens { get; init; }

    /// <summary>
    /// Whether the section cannot be dropped from the prompt (the system prompt, the tool contract). Default
    /// <c>false</c>. A required section is never offered a Remove action and never counts as compactable.
    /// </summary>
    public bool IsRequired { get; init; }

    /// <summary>
    /// Whether this section stands for content the provider has already summarised. Default <c>false</c>. The
    /// component shows the localized "Summarized" word next to the section and treats it as no longer
    /// compactable.
    /// </summary>
    public bool IsSummarized { get; init; }

    /// <summary>
    /// Whether the content of this section was dropped from the request (omitted). Default <c>false</c>. The row
    /// and its tokens stay visible — an omitted section is exactly what a reader needs to see — and the
    /// localized "Omitted" word marks it.
    /// </summary>
    public bool IsOmitted { get; init; }

    /// <summary>
    /// Whether the whole section may be dropped by the reader. Default <c>false</c>. A section with items can be
    /// removable as a whole AND have removable items; the section-level action removes the section, the
    /// item-level action removes one entry.
    /// </summary>
    public bool CanRemove { get; init; }

    /// <summary>
    /// The entries the section is made of, in the host's order. Default <c>null</c> (the section is not broken
    /// down). A non-empty list renders a disclosure that expands the section into its items.
    /// </summary>
    public IReadOnlyList<ContextUsageItem>? Items { get; init; }
}
