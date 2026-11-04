namespace Tempo.Blazor.AI.Models;

/// <summary>
/// A removal the reader asked for, reported by <c>TmContextUsage.OnRemoveItem</c>. The component never edits the
/// snapshot it renders: it says WHAT the reader wants gone and the host decides whether and how to apply it (drop
/// the section, summarise it, re-request the prompt with the entry removed).
/// <para>
/// <see cref="Item"/> tells the two shapes apart: a <c>null</c> item is a SECTION removal (the whole
/// <see cref="ContextUsageSection"/> named by <see cref="SectionKey"/>), a non-null item is a single ENTRY of that
/// section. <see cref="IsSection"/> answers the same question in one word.
/// </para>
/// </summary>
public sealed record ContextUsageRemoval
{
    /// <summary>The <see cref="ContextUsageSection.Key"/> of the section the reader acted on.</summary>
    public required string SectionKey { get; init; }

    /// <summary>The label of that section as it was rendered, so a host can name it without a lookup.</summary>
    public required string SectionLabel { get; init; }

    /// <summary>
    /// The single entry to drop, or <c>null</c> when the whole section is the target.
    /// </summary>
    public ContextUsageItem? Item { get; init; }

    /// <summary>Whether the removal targets the whole section (no single entry).</summary>
    public bool IsSection => Item is null;
}
