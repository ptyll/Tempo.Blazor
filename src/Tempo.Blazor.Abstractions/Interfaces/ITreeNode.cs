namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a node in TmTreeView. Supports lazy loading via IsLoading flag.
/// </summary>
/// <typeparam name="TKey">The type of the node identifier (typically string or Guid).</typeparam>
public interface ITreeNode<TKey>
{
    /// <summary>Unique identifier for this node.</summary>
    TKey Id { get; }

    /// <summary>Display label for the node.</summary>
    string Label { get; }

    /// <summary>Optional icon name from IconNames constants.</summary>
    string? Icon { get; }

    /// <summary>True if this node has no children and cannot be expanded.</summary>
    bool IsLeaf { get; }

    /// <summary>True if children are currently being loaded (shows spinner).</summary>
    bool IsLoading { get; }

    /// <summary>Child nodes. Empty list when IsLeaf is true or children not yet loaded.</summary>
    IReadOnlyList<ITreeNode<TKey>> Children { get; }
}
