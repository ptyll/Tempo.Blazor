namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>
/// How the block / text comment panels present (NO1).
/// <see cref="Popover"/> keeps the anchored bubble at every layout;
/// <see cref="Dialog"/> always renders the core <c>TmModal</c> (centred dialog on desktop, sheet
/// on mobile); <see cref="Auto"/> (the default) follows the layout — popover on a fine desktop pointer,
/// dialog / sheet on touch and narrow containers.
/// </summary>
public enum NotionCommentPresentation
{
    /// <summary>Anchored popover bubble at every layout (the pre-redesign behavior).</summary>
    Popover,

    /// <summary>The core modal at every layout.</summary>
    Dialog,

    /// <summary>Popover on a fine desktop pointer; modal / sheet on a coarse pointer or a narrow layout.</summary>
    Auto,
}

/// <summary>
/// How the page-level comment panel presents (phase 5, P4). <see cref="Inline"/> (the default)
/// keeps the collapsible panel at the bottom of the page; <see cref="Dialog"/> renders no inline
/// panel — the header badge and the Comments page action open a core <c>TmModal</c> hosting the
/// Embedded panel body.
/// </summary>
public enum NotionPageCommentsPresentation
{
    /// <summary>Collapsible panel inline at the bottom of the page (default).</summary>
    Inline,

    /// <summary>The core modal (sheet on a mobile layout) with the Embedded panel body.</summary>
    Dialog,
}
