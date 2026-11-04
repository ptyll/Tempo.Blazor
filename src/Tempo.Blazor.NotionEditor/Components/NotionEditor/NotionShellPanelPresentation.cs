namespace Tempo.Blazor.Components.NotionEditor;

/// <summary>The one open shell panel (Tasks / Blog / Analytics / Audit). At most one is open.</summary>
internal enum NotionShellPanel
{
    None = 0,
    Tasks = 1,
    Blog = 2,
    Analytics = 3,
    Audit = 4,
}

/// <summary>
/// How the Tasks / Blog / Analytics / Audit shell panels present themselves
/// (see <see cref="TmNotionEditor.ShellPanelPresentation"/>).
/// </summary>
public enum NotionShellPanelPresentation
{
    /// <summary>
    /// The panel rides the responsive core surfaces: docked next to the page on a desktop layout,
    /// a <c>TmSidePanel</c> side sheet on a tablet layout and a full-height sheet on a mobile
    /// layout. At most one shell panel is open at a time.
    /// </summary>
    Overlay = 0,

    /// <summary>
    /// The panel replaces the page in the main region (the pre-phase-3 hosting). Only one panel can
    /// be open; opening another closes the first.
    /// </summary>
    Main = 1,
}
