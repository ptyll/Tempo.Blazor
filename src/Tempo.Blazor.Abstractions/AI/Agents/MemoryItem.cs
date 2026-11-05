namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One fact an agent remembers: what it is, which category it belongs to, how sure the agent is, where it came from and
/// when it changed.
/// <para>
/// The record is a VALUE: the host recomposes it with <c>item with { … }</c> when the reader edits or pauses it, and no
/// component mutates the instance it was given. <see cref="Id"/> is the identity every surface keys on and the value an
/// action reports; <see cref="Category"/> is what groups the rows, so a memory the host left uncategorised is grouped
/// under the surface's own localized "Uncategorized" heading instead of a blank one.
/// </para>
/// <para>
/// The identity is the HOST's, never invented: a blank <see cref="Id"/> is rejected at construction, because an id the
/// model made up would change on every rebuild of a snapshot and silently drop the row's selection, edit and undo state.
/// A blank <see cref="Category"/> is fine (the surface names that group itself) and a blank text reads as empty.
/// </para>
/// <para>
/// Nothing is invented: an unmeasured <see cref="Confidence"/> or a missing <see cref="Source"/> prints nothing at all,
/// and a confidence outside <c>0…1</c> is not a ratio the surface can turn into a percentage.
/// </para>
/// </summary>
public sealed record MemoryItem
{
    /// <summary>Creates a memory.</summary>
    /// <param name="id">
    /// Stable identity in the host's store — the key every surface selects, edits and undoes by. Required: a blank value
    /// throws <see cref="ArgumentException"/> instead of inventing an id that changes on the next rebuild.
    /// </param>
    /// <param name="category">The group the memory belongs to ("Preferences", "People"). Blank is allowed.</param>
    /// <param name="text">The fact itself, in the host's own words.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace.</exception>
    public MemoryItem(string id, string category, string text)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            // A generated id looks stable but is not: a host that rebuilds the snapshot from data would hand the panel a
            // NEW identity on every render, so a row would lose its selection, its edit or its undo target without any
            // error. Failing loudly is the only way that bug cannot be silent.
            throw new ArgumentException(
                "A memory needs a stable, non-blank id: no component invents one, because an id that changes between " +
                "renders silently loses the row's state.",
                nameof(id));
        }

        Id = id;
        Category = category ?? string.Empty;
        Text = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    /// <summary>Stable identity of the memory, in the host's store. Never empty: construction rejects a blank value.</summary>
    public string Id { get; init; }

    /// <summary>
    /// The category the memory is grouped under. Kept verbatim (whitespace and all); blank means "no category" and the
    /// surface renders its own localized group name, so every uncategorised memory shares one group.
    /// </summary>
    public string Category { get; init; }

    /// <summary>The fact itself, verbatim. A whitespace-only text reads as empty.</summary>
    public string Text { get; init; }

    /// <summary>
    /// What the agent is allowed to do with the memory. Default <see cref="MemoryItemStatus.Active"/> — an unclassified
    /// memory is a normal stored fact, never "forgotten"; painted as a WORD plus an icon, never as a colour alone.
    /// </summary>
    public MemoryItemStatus Status { get; init; }

    /// <summary>
    /// How sure the agent is, as a RATIO in <c>0…1</c> (0.82 renders "82 %"). Null, negative, above one, or NaN means
    /// "not measured" and renders no confidence at all instead of a made-up zero.
    /// </summary>
    public double? Confidence { get; init; }

    /// <summary>
    /// Where the memory came from ("Support ticket #4211", "Conversation 4f2a", "Manual entry"), in the host's own
    /// words. Null or blank renders no source line.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>When the memory was first stored, in the host's timeline.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the memory last changed, or <c>null</c> when it never did. Null renders no "updated" line rather than a
    /// timestamp the host never recorded.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Whether the host gave the memory a category with actual text.</summary>
    public bool HasCategory => !string.IsNullOrWhiteSpace(Category);

    /// <summary>Whether the host reported a usable confidence (a value inside <c>0…1</c>).</summary>
    public bool HasConfidence => Confidence is { } confidence
        && !double.IsNaN(confidence)
        && confidence >= 0
        && confidence <= 1;

    /// <summary>Whether the memory came from a source worth printing.</summary>
    public bool HasSource => !string.IsNullOrWhiteSpace(Source);

    /// <summary>Whether the memory was edited after it was stored.</summary>
    public bool HasBeenUpdated => UpdatedAt is not null;

    /// <summary>Whether the memory is stored and in use by the agent.</summary>
    public bool IsActive => Status == MemoryItemStatus.Active;

    /// <summary>Whether the memory is kept but not used.</summary>
    public bool IsPaused => Status == MemoryItemStatus.Paused;

    /// <summary>Whether the memory has been forgotten (it is kept in the list only while an undo is still possible).</summary>
    public bool IsForgotten => Status == MemoryItemStatus.Forgotten;

    /// <summary>Whether the agent proposed the memory and is waiting for a human decision.</summary>
    public bool IsPendingReview => Status == MemoryItemStatus.PendingReview;

    /// <summary>Whether <see cref="Status"/> is one of the declared states (a cast integer may not be).</summary>
    public bool HasKnownStatus => Status is >= MemoryItemStatus.Active and <= MemoryItemStatus.PendingReview;
}
