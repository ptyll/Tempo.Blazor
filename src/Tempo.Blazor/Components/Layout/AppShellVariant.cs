namespace Tempo.Blazor.Components.Layout;

/// <summary>
/// The visual treatment of an <see cref="TmAppShell"/>: how the page content sits on the shell's
/// workspace background.
/// </summary>
public enum AppShellVariant
{
    /// <summary>
    /// The content is a rounded card floating on the workspace background (the default): the frame
    /// reads as one surface with a gutter around it, like a desktop application window.
    /// </summary>
    Inset,

    /// <summary>
    /// The content fills the frame edge to edge, separated from the navigation by a hairline border
    /// only. The denser treatment for data-heavy applications.
    /// </summary>
    Flat,

    /// <summary>
    /// No content panel at all: the <c>main</c> area is transparent over the workspace background —
    /// no border, no radius, no shadow — so the page content lies directly on the workspace and its
    /// own cards read as white surfaces (the PromptHelper redesign look, plan 92e96b25 / TG-008).
    /// The navigation column and the work area keep their panels.
    /// </summary>
    Workspace,
}
