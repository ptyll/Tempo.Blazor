namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>
/// Presentation mode of <see cref="TmNotionPageCommentPanel"/>. <see cref="Inline"/> (the default)
/// renders the collapsible toggle + expandable body; <see cref="Embedded"/> renders the body
/// directly with no toggle — used inside the page-comments dialog (<c>PageCommentsPresentation=Dialog</c>)
/// and any host-owned container.
/// </summary>
public enum TmNotionPageCommentPanelMode
{
    /// <summary>Collapsible panel with the toggle header (default).</summary>
    Inline,

    /// <summary>Always-expanded body without the toggle; the host owns the surrounding chrome.</summary>
    Embedded,
}
