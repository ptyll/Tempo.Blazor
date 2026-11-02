using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>
/// Every user-visible string of a comment thread. One record replaces the ~15 string parameters
/// the panels used to pass into <see cref="TmNotionCommentThreadView"/>.
/// </summary>
public sealed record TmNotionCommentLabels
{
    /// <summary>Accessible name of the thread list.</summary>
    public string? ListLabel { get; init; }

    /// <summary>Accessible name of the actions group.</summary>
    public string? ActionsLabel { get; init; }

    /// <summary>Reply button label. Reply stays inline.</summary>
    public string Reply { get; init; } = string.Empty;

    /// <summary>⋯ menu label.</summary>
    public string More { get; init; } = string.Empty;

    /// <summary>Edit menu item label.</summary>
    public string Edit { get; init; } = string.Empty;

    /// <summary>Accessible name of the edit composer.</summary>
    public string EditLabel { get; init; } = string.Empty;

    /// <summary>Delete menu item label.</summary>
    public string Delete { get; init; } = string.Empty;

    /// <summary>Quote menu item label.</summary>
    public string Quote { get; init; } = string.Empty;

    /// <summary>Edited marker.</summary>
    public string Edited { get; init; } = string.Empty;

    /// <summary>Save button label.</summary>
    public string Save { get; init; } = string.Empty;

    /// <summary>Cancel button label.</summary>
    public string Cancel { get; init; } = string.Empty;

    /// <summary>Inline reply placeholder.</summary>
    public string? ReplyPlaceholder { get; init; }

    /// <summary>Inline reply accessible name.</summary>
    public string? ReplyLabel { get; init; }

    /// <summary>Inline reply submit label.</summary>
    public string ReplySubmit { get; init; } = string.Empty;

    /// <summary>Hint beside the inline reply submit button.</summary>
    public string? ReplyHint { get; init; }

    /// <summary>Builds the record from one panel's localization prefix (TmNotionBlockComment, …).</summary>
    public static TmNotionCommentLabels For(ITmLocalizer loc, string prefix) => new()
    {
        ListLabel = loc[$"{prefix}_Thread"],
        ActionsLabel = loc[$"{prefix}_EntryActions"],
        Reply = loc[$"{prefix}_ReplyToThis"],
        More = loc["Tm_More"],
        Edit = loc[$"{prefix}_Edit"],
        EditLabel = loc[$"{prefix}_EditLabel"],
        Delete = loc[$"{prefix}_Delete"],
        Quote = loc[$"{prefix}_Quote"],
        Edited = loc[$"{prefix}_Edited"],
        Save = loc[$"{prefix}_Save"],
        Cancel = loc[$"{prefix}_Cancel"],
        ReplyPlaceholder = loc[$"{prefix}_ReplyPlaceholder"],
        ReplyLabel = loc[$"{prefix}_ReplyLabel"],
        ReplySubmit = loc[$"{prefix}_Reply"],
        ReplyHint = loc[$"{prefix}_ReplyHint"]
    };
}
