using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Dashboard;

/// <summary>
/// Partial of <see cref="TmDashboard"/> (F4): reordering and resizing without drag — the
/// compact move controls (tablet portrait, mobile) and the drag handle's keyboard grab mode
/// (Enter/Space grabs and drops, arrows move one cell, Shift+arrows resize one step, Escape
/// restores the pre-grab geometry), every step announced through the polite live region.
/// Kept beside <c>TmDashboard.razor.cs</c> so the code-behind stays under the N270/F13
/// 1200-line source ceiling.
/// </summary>
public partial class TmDashboard
{
    // ── F4: reordering and resizing without drag ─────────────────────

    /// <summary>
    /// Whether the widget renders its compact move controls: edit mode on a layout without the
    /// desktop grid (tablet portrait, mobile), where drag and resize handles do not exist.
    /// </summary>
    private bool IsCompactEditMode(TmLayoutContext layout) => _isEditMode && !layout.IsDesktop;

    /// <summary>Whether another widget precedes this one in the linear (Y, X) order (Move up enabled).</summary>
    private bool CanMoveUp(WidgetInstance widget)
    {
        var ordered = OrderedWidgets();
        return ordered.IndexOf(widget) > 0;
    }

    /// <summary>Whether another widget follows this one in the linear (Y, X) order (Move down enabled).</summary>
    private bool CanMoveDown(WidgetInstance widget)
    {
        var ordered = OrderedWidgets();
        return ordered.IndexOf(widget) < ordered.Count - 1;
    }

    private List<WidgetInstance> OrderedWidgets() => _widgets
        .OrderBy(w => w.Y).ThenBy(w => w.X).ThenBy(w => w.InstanceId, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// DA2 compact-mode move: swaps the widget's <c>(X, Y)</c> with the previous neighbour in the
    /// linear (Y, X) order and pushes overlaps — the widget model (instance identity) is unchanged.
    /// </summary>
    private void MoveWidgetUp(WidgetInstance widget) => SwapWithNeighbour(widget, -1);

    /// <summary>DA2 compact-mode move: swaps with the next neighbour in the linear (Y, X) order.</summary>
    private void MoveWidgetDown(WidgetInstance widget) => SwapWithNeighbour(widget, +1);

    private void SwapWithNeighbour(WidgetInstance widget, int direction)
    {
        if (!_isEditMode) return;

        var ordered = OrderedWidgets();
        var index = ordered.IndexOf(widget);
        var neighbourIndex = index + direction;
        if (index < 0 || neighbourIndex < 0 || neighbourIndex >= ordered.Count) return;

        var neighbour = ordered[neighbourIndex];
        (widget.X, neighbour.X) = (neighbour.X, widget.X);
        (widget.Y, neighbour.Y) = (neighbour.Y, widget.Y);

        if (_currentDashboard?.Grid.AllowOverlap == false)
        {
            PushOverlappingWidgets(widget);
            PushOverlappingWidgets(neighbour);
        }

        AnnounceMove(PositionText(widget));
        StateHasChanged();
    }

    /// <summary>Native button activation (Enter/Space, docs/keyboard-activation-convention.md): first press grabs, second press drops.</summary>
    private void ToggleGrab(WidgetInstance widget)
    {
        if (!_isEditMode || !IsDraggable(widget)) return;

        if (_grabbedWidget == widget)
        {
            DropGrabbed();
            return;
        }

        ResetGrabState();
        _grabbedWidget = widget;
        _grabOriginal = (widget.X, widget.Y, widget.Width, widget.Height);
        AnnounceMove(Loc["TmDashboard_Grabbed"] + " " + PositionText(widget));
        StateHasChanged();
    }

    /// <summary>Arrow keys move the grabbed widget one grid cell; Shift+arrows resize one step; Escape cancels.</summary>
    internal void HandleHandleKeyDown(KeyboardEventArgs e, WidgetInstance widget)
    {
        if (!_isEditMode || _grabbedWidget != widget) return;

        // Shift+arrow resizes one step, a bare arrow moves one cell (spec F4 / WCAG 2.5.7).
        if (e.ShiftKey)
        {
            switch (e.Key)
            {
                case "ArrowLeft":
                    ResizeGrabbedBy(-1, 0);
                    break;
                case "ArrowRight":
                    ResizeGrabbedBy(1, 0);
                    break;
                case "ArrowUp":
                    ResizeGrabbedBy(0, -1);
                    break;
                case "ArrowDown":
                    ResizeGrabbedBy(0, 1);
                    break;
                default:
                    return;
            }

            StateHasChanged();
            return;
        }

        switch (e.Key)
        {
            case "ArrowLeft":
                MoveGrabbedBy(-1, 0);
                break;
            case "ArrowRight":
                MoveGrabbedBy(1, 0);
                break;
            case "ArrowUp":
                MoveGrabbedBy(0, -1);
                break;
            case "ArrowDown":
                MoveGrabbedBy(0, 1);
                break;
            case "Escape":
                CancelGrab();
                break;
            default:
                return;
        }

        StateHasChanged();
    }

    private void MoveGrabbedBy(int deltaX, int deltaY)
    {
        if (_grabbedWidget is not { } widget) return;

        widget.X = Math.Clamp(widget.X + deltaX, 0, Math.Max(0, Columns - widget.Width));
        widget.Y = Math.Max(0, widget.Y + deltaY);
        PushAfterGrab(widget);
        AnnounceMove(PositionText(widget));
    }

    private void ResizeGrabbedBy(int deltaWidth, int deltaHeight)
    {
        if (_grabbedWidget is not { } widget) return;

        var definition = WidgetRegistry?.GetWidget(widget.WidgetId);
        var minWidth = Math.Max(1, definition?.MinWidth ?? 1);
        var minHeight = Math.Max(1, definition?.MinHeight ?? 1);
        // MaxWidth/MaxHeight of 0 means unlimited — the grid itself is the ceiling for the width.
        var maxWidth = definition is { MaxWidth: > 0 } ? definition.MaxWidth : Columns;
        var maxHeight = definition is { MaxHeight: > 0 } ? definition.MaxHeight : int.MaxValue;

        widget.Width = Math.Clamp(widget.Width + deltaWidth, minWidth, Math.Min(maxWidth, Columns));
        widget.Height = Math.Clamp(widget.Height + deltaHeight, minHeight, maxHeight);
        // A growth at the right edge would leave the grid: pull the position back inside.
        widget.X = Math.Clamp(widget.X, 0, Math.Max(0, Columns - widget.Width));
        PushAfterGrab(widget);
        AnnounceMove(PositionText(widget));
    }

    private void PushAfterGrab(WidgetInstance widget)
    {
        if (_currentDashboard?.Grid.AllowOverlap == false)
        {
            PushOverlappingWidgets(widget);
        }
    }

    private void DropGrabbed()
    {
        if (_grabbedWidget is not { } widget) return;

        PushAfterGrab(widget);
        ResetGrabState();
        AnnounceMove(PositionText(widget));
        StateHasChanged();
    }

    private void CancelGrab()
    {
        if (_grabbedWidget is not { } widget || _grabOriginal is not { } original)
        {
            ResetGrabState();
            StateHasChanged();
            return;
        }

        widget.X = original.X;
        widget.Y = original.Y;
        widget.Width = original.Width;
        widget.Height = original.Height;
        PushAfterGrab(widget);
        ResetGrabState();
        AnnounceMove(Loc["TmDashboard_MoveCancelled"] + " " + PositionText(widget));
        StateHasChanged();
    }

    private void ResetGrabState()
    {
        _grabbedWidget = null;
        _grabOriginal = null;
    }

    /// <summary>Human-readable geometry for the live region: "column 3, row 2, 4 × 3" (1-based).</summary>
    private string PositionText(WidgetInstance widget) => string.Format(
        Loc["TmDashboard_PositionAnnouncement"],
        GetWidgetName(widget), widget.X + 1, widget.Y + 1, widget.Width, widget.Height);

    private void AnnounceMove(string message) => _moveAnnouncement = message;
}
