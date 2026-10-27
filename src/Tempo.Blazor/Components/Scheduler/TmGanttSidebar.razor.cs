using System.ComponentModel;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The icon rail of <see cref="TmGantt"/> (<c>ShowSidebar</c>) and the Tasks / Workload / History /
/// Notifications / Reports drawer it opens. Stateless: the active panel lives on
/// <see cref="TmGanttPart.Owner"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttSidebar : TmGanttPart
{
}