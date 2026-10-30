namespace Tempo.Blazor.Abstractions.Models;

/// <summary>How the Gantt presents its filter editor.</summary>
public enum GanttFilterPanelMode
{
    /// <summary>The default: the editor is a strip under the toolbar (today's look).</summary>
    Inline,

    /// <summary>The editor is a modal dialog (a bottom sheet on a mobile viewport) with a Clear all | Cancel | Apply footer.</summary>
    Dialog,

    /// <summary>
    /// Intended to pick the presentation from the available container width. Until the container layout lands
    /// (a later phase) it behaves exactly like <see cref="Inline"/>; this is subject to change, so a host that needs a
    /// specific presentation should choose <see cref="Inline"/> or <see cref="Dialog"/> explicitly.
    /// </summary>
    Auto,
}
