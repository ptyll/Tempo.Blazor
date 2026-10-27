using System.ComponentModel;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The task grid of <see cref="TmGantt"/>: header, rows (bulk checkbox, WBS, title with inline edit, status,
/// dates, progress, custom fields), the drag-and-drop reorder affordances and the bulk-action bar. The task
/// list, the selection and the inline-edit state live on <see cref="TmGanttPart.Owner"/>; this component only
/// draws them, and skips a render pass that changes nothing it draws.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttTree : TmGanttPart
{
    /// <summary>
    /// What the tree paints. <see cref="TmGantt.DrawVersion"/> moves on every data refresh; <c>Tasks</c> is the
    /// content fingerprint of the tasks, so a task edited in place is never skipped.
    /// </summary>
    private readonly record struct TreeState(
        int Version,
        int Tasks,
        IReadOnlyList<GanttColumnDefinition> Columns,
        object CustomFields,
        bool Bulk,
        int BulkSelected,
        string? SelectedTaskId,
        string? Dragged,
        string? DropTarget,
        GanttDropPosition DropPosition,
        string? EditTask,
        GanttColumnKey? EditColumn,
        string? EditField,
        string EditValue,
        bool OverdueHighlight,
        DateTime Today,
        string? Baseline,
        int Overloaded,
        string? Prefix,
        string Culture);

    /// <inheritdoc />
    protected override object? RenderState => new TreeState(
        Owner.DrawVersion, Owner.TaskFingerprint(), Owner.Columns, Owner.CustomFields, Owner.AllowBulkSelect, Owner._selectedTaskIds.Count,
        Owner.SelectedTask?.Id, Owner._draggedTaskId, Owner._dropTargetId, Owner._dropPosition,
        Owner._inlineEditTaskId, Owner._inlineEditColumn, Owner._inlineEditCustomFieldId, Owner._inlineEditValue,
        Owner.ShowOverdueHighlight, Owner._today, Owner.ActiveBaselineId, Owner._overloadedAssigneeIds.Count,
        Owner.TestIdPrefix, Loc.Culture.Name);

    private string GetRowClass(GanttTaskNode node)
    {
        var classes = new List<string>();
        if (Owner.SelectedTask?.Id == node.Task.Id)
            classes.Add("tm-gantt__tree-row--selected");
        if (Owner._draggedTaskId == node.Task.Id)
            classes.Add("tm-gantt__tree-row--dragging");
        if (Owner.ShowOverdueHighlight && Owner.IsOverdue(node.Task))
            classes.Add("tm-gantt__tree-row--overdue");
        return string.Join(" ", classes);
    }

    private string GetDragClass(GanttTaskNode node)
    {
        if (Owner._dropTargetId != node.Task.Id || Owner._dropPosition == GanttDropPosition.None)
            return "";
        return Owner._dropPosition switch
        {
            GanttDropPosition.Before => "tm-gantt__tree-row--drop-before",
            GanttDropPosition.After => "tm-gantt__tree-row--drop-after",
            GanttDropPosition.Child => "tm-gantt__tree-row--drop-child",
            _ => ""
        };
    }

    private static string GetPriorityIconClass(TmWorkItemPriority priority) => priority switch
    {
        TmWorkItemPriority.Highest => "tm-gantt__priority-icon--highest",
        TmWorkItemPriority.High => "tm-gantt__priority-icon--high",
        TmWorkItemPriority.Medium => "tm-gantt__priority-icon--medium",
        TmWorkItemPriority.Low => "tm-gantt__priority-icon--low",
        TmWorkItemPriority.Lowest => "tm-gantt__priority-icon--lowest",
        _ => ""
    };
}