using System.ComponentModel;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The filter strip of <see cref="TmGantt"/>: the chips of the active filters under the toolbar and the filter editor
/// (inline, or a dialog for <c>FilterPanelMode.Dialog</c>). Stateless: the filters and the open flag live on
/// <see cref="TmGanttPart.Owner"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttFilterBar : TmGanttPart
{
}