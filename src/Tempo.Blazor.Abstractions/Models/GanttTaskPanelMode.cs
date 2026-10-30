namespace Tempo.Blazor.Abstractions.Models;

/// <summary>How the Gantt presents its task panel (the editor of the selected task).</summary>
public enum GanttTaskPanelMode
{
    /// <summary>The default: an in-flow panel at the right edge of the chart at every width (the pre-redesign look).</summary>
    Inline,

    /// <summary>
    /// The panel is a core <c>TmSidePanel</c> side sheet at every width: it slides over the chart, is dismissed by its
    /// close button, the backdrop or Escape, and presents its content as the tabs Detail | Attachments | Comments | History.
    /// </summary>
    Overlay,

    /// <summary>
    /// Picks by the Gantt's own container: the in-flow panel on a desktop-width container (1024 px and up), the
    /// tabbed side sheet on a tablet or narrower container.
    /// </summary>
    Auto,
}