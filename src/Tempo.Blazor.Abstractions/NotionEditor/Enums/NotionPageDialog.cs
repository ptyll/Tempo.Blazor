namespace Tempo.Blazor.NotionEditor.Enums;

/// <summary>
/// The editor-owned page dialogs a page can ask the shell to open (<c>NotionEditorContext.OpenPageDialog</c>).
/// The page calls the context instead of rendering the dialog itself: the shell canvas remounts on a
/// band flip, and a dialog inside it would close mid-open.
/// </summary>
public enum NotionPageDialog
{
    /// <summary>The page share dialog.</summary>
    Share,

    /// <summary>The page access-restrictions dialog.</summary>
    Restrictions,

    /// <summary>The page version-history dialog.</summary>
    History
}
