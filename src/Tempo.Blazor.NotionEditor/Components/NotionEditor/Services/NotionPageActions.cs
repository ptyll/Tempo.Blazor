namespace Tempo.Blazor.Components.NotionEditor.Services;

/// <summary>
/// Page-level actions the <see cref="Page.TmNotionPage"/> exposes to chrome rendered above it
/// (the mobile action bar). Populated by the page and consumed through the cascaded
/// <see cref="NotionEditorContext"/>, so the bar invokes the page's own handlers.
/// </summary>
internal sealed class NotionPageActions
{
    /// <summary>Opens the slash menu at the focused (or last) block, creating one on an empty page.</summary>
    public Func<Task>? AddBlock { get; init; }

    /// <summary>Expands the page comments panel.</summary>
    public Func<Task>? OpenComments { get; init; }

    /// <summary>Opens the page settings (⋯) menu.</summary>
    public Func<Task>? OpenPageSettings { get; init; }
}
