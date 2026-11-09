namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>How much chrome <see cref="TmNotionEditor"/> renders around the page.</summary>
public enum NotionEditorChrome
{
    /// <summary>The full editor: sidebar, top bar, notification bell and the mobile action bar.</summary>
    Full = 0,

    /// <summary>
    /// A public, read-only document. The nested editor renders no shortcuts, notification bell,
    /// mode toggles or badges; the host page supplies the only header.
    /// </summary>
    Minimal = 1,

    /// <summary>The page only. No top bar, no sidebar chrome and no mobile action bar.</summary>
    None = 2,
}
