namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents an item displayed in TmMultiViewList (Table / Card / List views).
/// </summary>
public interface IMultiViewListItem
{
    /// <summary>Unique identifier.</summary>
    string Id { get; }

    /// <summary>Primary display title.</summary>
    string Title { get; }

    /// <summary>Optional secondary line of text.</summary>
    string? SubTitle { get; }

    /// <summary>Optional URL of an avatar or thumbnail image.</summary>
    string? AvatarUrl { get; }

    /// <summary>Tags associated with this item.</summary>
    IReadOnlyList<ITag>? Tags { get; }

    /// <summary>Short status label (e.g. "Active", "Pending").</summary>
    string? StatusLabel { get; }

    /// <summary>CSS color value for the status badge (e.g. "#22c55e").</summary>
    string? StatusColor { get; }

    /// <summary>Optional date/time associated with this item.</summary>
    DateTimeOffset? Date { get; }
}
