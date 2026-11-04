namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One entry inside a <see cref="ContextUsageSection"/> — a single tool definition, an attached file, one tool
/// result — so a reader can see what the tokens of a section actually are and (when the host allows it) drop one
/// entry without dropping the whole section.
/// <para>
/// The item is the host's data: <see cref="Label"/> is rendered as text and never localised, and
/// <see cref="Tokens"/> is counted as reported (a negative value counts as nothing). An item is a BREAKDOWN of
/// its section: when the section reports its own total the items are the detail of that total, never a second
/// measurement.
/// </para>
/// </summary>
/// <param name="Id">Stable identifier of the entry inside its section, used as the DOM key and by the removal report.</param>
/// <param name="Label">The reader-facing name of the entry (the tool name, the file name, the call id).</param>
public sealed record ContextUsageItem(string Id, string Label)
{
    /// <summary>
    /// How many tokens the entry occupies. Default <c>0</c>; a negative value counts as nothing. It is only the
    /// section's total when the section itself reports none.
    /// </summary>
    public int Tokens { get; init; }

    /// <summary>
    /// Whether the reader may drop this entry on its own. Default <c>false</c> — a component offers the Remove
    /// action for an item only when the host says the item is removable (and a removal handler is bound), so an
    /// entry whose loss would break the conversation is never offered as removable by accident.
    /// </summary>
    public bool CanRemove { get; init; }
}
