namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>
/// How the block / text comment panels present (NO1).
/// <see cref="Popover"/> keeps the anchored bubble at every layout;
/// <see cref="Dialog"/> always renders the core <c>TmModal</c> (centred dialog on desktop, sheet
/// on mobile); <see cref="Auto"/> (the default) follows the layout — popover on a desktop layout,
/// dialog / sheet on touch and narrow containers.
/// </summary>
public enum NotionCommentPresentation
{
    /// <summary>Anchored popover bubble at every layout (the pre-redesign behavior).</summary>
    Popover,

    /// <summary>The core modal at every layout.</summary>
    Dialog,

    /// <summary>Popover on a desktop layout, modal / sheet on a narrow (mobile) layout.</summary>
    Auto,
}
