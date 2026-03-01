namespace Tempo.Blazor.Components.Activity;

/// <summary>
/// Wrapper for document.execCommand JavaScript commands used by rich text editors.
/// </summary>
public static class EditorCommands
{
    #region Command Constants

    /// <summary>Command for bold formatting.</summary>
    public const string Bold = "bold";

    /// <summary>Command for italic formatting.</summary>
    public const string Italic = "italic";

    /// <summary>Command for underline formatting.</summary>
    public const string Underline = "underline";

    /// <summary>Command for strikethrough formatting.</summary>
    public const string Strikethrough = "strikeThrough";

    /// <summary>Command for creating a link.</summary>
    public const string InsertLink = "createLink";

    /// <summary>Command for removing a link.</summary>
    public const string RemoveLink = "unlink";

    /// <summary>Command for inserting an unordered list.</summary>
    public const string InsertUnorderedList = "insertUnorderedList";

    /// <summary>Command for inserting an ordered list.</summary>
    public const string InsertOrderedList = "insertOrderedList";

    /// <summary>Command for removing formatting.</summary>
    public const string RemoveFormat = "removeFormat";

    /// <summary>Command for inserting text.</summary>
    public const string InsertText = "insertText";

    /// <summary>Command for inserting HTML.</summary>
    public const string InsertHtml = "insertHTML";

    /// <summary>Command for selecting all content.</summary>
    public const string SelectAll = "selectAll";

    /// <summary>Command for deleting selection.</summary>
    public const string Delete = "delete";

    /// <summary>Command for formatting as heading.</summary>
    public const string FormatBlock = "formatBlock";

    #endregion

    #region Format Commands

    /// <summary>
    /// Returns the JavaScript command string for bold formatting.
    /// </summary>
    public static string FormatBold() => GetExecCommandString(Bold);

    /// <summary>
    /// Returns the JavaScript command string for italic formatting.
    /// </summary>
    public static string FormatItalic() => GetExecCommandString(Italic);

    /// <summary>
    /// Returns the JavaScript command string for underline formatting.
    /// </summary>
    public static string FormatUnderline() => GetExecCommandString(Underline);

    /// <summary>
    /// Returns the JavaScript command string for strikethrough formatting.
    /// </summary>
    public static string FormatStrikethrough() => GetExecCommandString(Strikethrough);

    /// <summary>
    /// Returns the JavaScript command string for inserting a link.
    /// </summary>
    public static string InsertLinkCommand(string url) => GetExecCommandString(InsertLink, url);

    /// <summary>
    /// Returns the JavaScript command string for inserting an unordered list.
    /// </summary>
    public static string InsertUnorderedListCommand() => GetExecCommandString(InsertUnorderedList);

    /// <summary>
    /// Returns the JavaScript command string for inserting an ordered list.
    /// </summary>
    public static string InsertOrderedListCommand() => GetExecCommandString(InsertOrderedList);

    /// <summary>
    /// Returns the JavaScript command string for formatting as heading.
    /// </summary>
    public static string FormatHeadingCommand(int level) => GetExecCommandString(FormatBlock, $"H{level}");

    #endregion

    #region Query Commands

    /// <summary>
    /// Returns the JavaScript query string for checking bold state.
    /// </summary>
    public static string QueryBoldState() => GetQueryCommandStateString(Bold);

    /// <summary>
    /// Returns the JavaScript query string for checking italic state.
    /// </summary>
    public static string QueryItalicState() => GetQueryCommandStateString(Italic);

    /// <summary>
    /// Returns the JavaScript query string for checking underline state.
    /// </summary>
    public static string QueryUnderlineState() => GetQueryCommandStateString(Underline);

    #endregion

    #region Utility Methods

    private static string GetExecCommandString(string command, string? value = null)
    {
        var valueParam = value != null ? $"'{EscapeJavaScriptString(value)}'" : "null";
        return $"document.execCommand('{command}', false, {valueParam});";
    }

    private static string GetQueryCommandStateString(string command)
    {
        return $"document.queryCommandState('{command}')";
    }

    private static string EscapeJavaScriptString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    #endregion
}
