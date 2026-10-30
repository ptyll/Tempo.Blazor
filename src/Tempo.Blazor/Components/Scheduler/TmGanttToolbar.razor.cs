using System.ComponentModel;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The command row of <see cref="TmGantt"/>: add / expand / collapse, the Day | Week | Month segment, cascade
/// sort, the zoom controls, panel toggles and the view-settings menu. Stateless: every value and handler lives
/// on <see cref="TmGanttPart.Owner"/>. It re-renders with the owner (its aria-pressed / expanded state follows
/// the owner's), which is cheap - the expensive surfaces are the tree and the timeline.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttToolbar : TmGanttPart
{
}