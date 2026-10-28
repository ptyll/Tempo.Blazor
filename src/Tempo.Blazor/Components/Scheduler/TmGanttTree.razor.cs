using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
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
        string Culture,
        string? FocusRow,
        int FocusSeq,
        bool WbsInTitle,
        bool Narrow);

    /// <inheritdoc />
    protected override object? RenderState => new TreeState(
        Owner.DrawVersion, Owner.TaskFingerprint(), Owner.Columns, Owner.CustomFields, Owner.AllowBulkSelect, Owner._selectedTaskIds.Count,
        Owner.SelectedTask?.Id, Owner._draggedTaskId, Owner._dropTargetId, Owner._dropPosition,
        Owner._inlineEditTaskId, Owner._inlineEditColumn, Owner._inlineEditCustomFieldId, Owner._inlineEditValue,
        Owner.ShowOverdueHighlight, Owner._today, Owner.ActiveBaselineId, Owner._overloadedAssigneeIds.Count,
        Owner.TestIdPrefix, Loc.Culture.Name,
        Owner.EffectiveFocusRowId(), Owner.FocusRequest?.Seq ?? 0, Owner.ViewSettings.WbsInTitle, Owner.IsNarrowLayout);

    /// <summary>
    /// Duration and Priority do not fit the 420px default, so the grid sizes to the columns it shows (the header and
    /// the cells share one flex row; a narrower grid would clip the last column and misalign them). With neither
    /// column - today's grid - nothing is written and the stylesheet width stands.
    /// </summary>
    /// <summary>
    /// The columns the grid shows. A tablet or narrower container keeps the four that identify and schedule a task - Task, Status, Start,
    /// End - so the timeline keeps its room (the other columns and the custom fields are one desktop width away).
    /// </summary>
    private IEnumerable<GanttColumnDefinition> ShownColumns => Owner.IsNarrowLayout
        ? Owner.EffectiveColumns.Where(c => c.Visible && c.Key is GanttColumnKey.Title or GanttColumnKey.Status or GanttColumnKey.Start or GanttColumnKey.End)
        : Owner.EffectiveColumns.Where(c => c.Visible);

    /// <summary>The custom-field columns; none on a narrow container.</summary>
    private IReadOnlyList<TmCustomFieldDefinition> ShownCustomFields => Owner.IsNarrowLayout ? [] : Owner.CustomFields;

    private string? TreeStyle => Owner.IsNarrowLayout ? null : TreeWidth is { } width ? $"width: {width}px; max-width: 70%; flex-shrink: 0" : null;

    /// <summary>The grid's full width in pixels, or null when the stylesheet width stands.</summary>
    private int? TreeWidth
    {
        get
        {
            // A tablet or narrower container shows only the identifying columns (ShownColumns) in the width it has: no computed minimum.
            if (Owner.IsNarrowLayout) return null;

            var visible = Owner.EffectiveColumns.Where(c => c.Visible).Select(c => c.Key).ToList();
            if (!visible.Contains(GanttColumnKey.Duration) && !visible.Contains(GanttColumnKey.Priority)) return null;

            // Custom fields are real cells of 100px each; the tree caps at 70% and scrolls horizontally past that.
            var width = 24 + (Owner.AllowBulkSelect ? 32 : 0) + Owner.CustomFields.Count * 100;
            foreach (var key in visible)
            {
                width += key switch
                {
                    GanttColumnKey.Title => 220,
                    GanttColumnKey.WBS => 50,
                    GanttColumnKey.Status or GanttColumnKey.Priority => 90,
                    GanttColumnKey.Start or GanttColumnKey.End or GanttColumnKey.Progress or GanttColumnKey.Duration => 80,
                    _ => 0,
                };
            }

            return width;
        }
    }

    private readonly Dictionary<string, ElementReference> _rowRefs = [];
    private int _handledFocusSeq;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Element references of rows that left the visible set (collapse, filter) would otherwise pile up.
        var visibleIds = Owner._visibleNodes.Select(n => n.Task.Id).ToHashSet();
        foreach (var id in _rowRefs.Keys.Where(id => !visibleIds.Contains(id)).ToList()) _rowRefs.Remove(id);

        // A key on a row asked the owner to move DOM focus (an arrow key, Escape from a bar): the new tab stop is
        // rendered now, so the element exists and carries tabindex 0. One request is acted on once.
        var request = Owner.FocusRequest;
        if (request is not { Target: GanttFocusTarget.Row } row || row.Seq == _handledFocusSeq) return;
        _handledFocusSeq = row.Seq;
        if (_rowRefs.TryGetValue(row.TaskId, out var element))
        {
            try
            {
                await element.FocusAsync(preventScroll: false);
            }
            catch (InvalidOperationException)
            {
                // The row left the DOM with the render (a collapse); nothing to focus.
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }

    /// <summary>"3 days" - the inclusive-exclusive span of the task in whole days, as a localized plural family.</summary>
    private string DurationText(TmWorkItem task)
    {
        var days = (int)Math.Max(0, Math.Round((task.End.Date - task.Start.Date).TotalDays));
        return Loc.Plural("TmGantt_DurationDays", days);
    }

    private static string GetPriorityGlyphClass(TmWorkItemPriority priority) => priority switch
    {
        TmWorkItemPriority.Highest => "tm-gantt__priority-glyph--highest",
        TmWorkItemPriority.High => "tm-gantt__priority-glyph--high",
        TmWorkItemPriority.Medium => "tm-gantt__priority-glyph--medium",
        TmWorkItemPriority.Low => "tm-gantt__priority-glyph--low",
        TmWorkItemPriority.Lowest => "tm-gantt__priority-glyph--lowest",
        _ => string.Empty,
    };

    private static string GetPriorityGlyph(TmWorkItemPriority priority) => priority switch
    {
        TmWorkItemPriority.Highest => "chevrons-up",
        TmWorkItemPriority.High => "chevron-up",
        TmWorkItemPriority.Low => "chevron-down",
        TmWorkItemPriority.Lowest => "chevrons-down",
        _ => "minus",
    };

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