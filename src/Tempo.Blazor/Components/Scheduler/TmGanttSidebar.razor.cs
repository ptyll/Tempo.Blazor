using System.ComponentModel;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The icon rail of <see cref="TmGantt"/> (<c>ShowSidebar</c>) and the Tasks / Workload / History /
/// Notifications / Reports drawer it opens. Stateless: the active panel lives on
/// <see cref="TmGanttPart.Owner"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttSidebar : TmGanttPart
{
    private bool TabsMode => Owner.SidebarMode == GanttSidebarMode.Tabs;

    /// <summary>The drawer is the tabpanel of its tab only in tab mode; the icon rail has no tab semantics.</summary>
    private string? PanelRole => TabsMode ? "tabpanel" : null;

    private string? PanelId(GanttSidebarPanel panel) => TabsMode ? Owner.PanelDomId(panel) : null;

    private string? PanelLabelledBy(GanttSidebarPanel panel) => TabsMode ? Owner.TabDomId(panel) : null;

}