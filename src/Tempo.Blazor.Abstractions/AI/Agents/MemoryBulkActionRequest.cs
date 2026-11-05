namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One bulk request the reader made about a selection of memories: WHICH memories and WHAT to do with them.
/// <para>
/// The request holds a SNAPSHOT of the selection (the list is copied at construction), so a host that mutates its own
/// collection while the request is in flight still receives exactly what the reader acted on. The surface reports the
/// request and never applies it to the host's data.
/// </para>
/// </summary>
public sealed record MemoryBulkActionRequest
{
    /// <summary>Creates a bulk request.</summary>
    /// <param name="items">The selected memories, in the order they are shown. Null entries are dropped.</param>
    /// <param name="action">What the reader asked to do with them.</param>
    public MemoryBulkActionRequest(IEnumerable<MemoryItem>? items, MemoryBulkAction action)
    {
        Items = items is null ? [] : [.. items.Where(item => item is not null)];
        Action = action;
    }

    /// <summary>The selected memories, copied at construction.</summary>
    public IReadOnlyList<MemoryItem> Items { get; init; }

    /// <summary>What the reader asked to do with the selection.</summary>
    public MemoryBulkAction Action { get; init; }

    /// <summary>How many memories the request covers.</summary>
    public int Count => Items.Count;
}
