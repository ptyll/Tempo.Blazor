using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Base of the internal pieces <see cref="TmGantt"/> is composed of (<see cref="TmGanttToolbar"/>,
/// <see cref="TmGanttTree"/>, <see cref="TmGanttTimeline"/>, <see cref="TmGanttSidebar"/>). A host never
/// places one: the Razor compiler can only emit <c>public</c> components, so they are hidden from the
/// editor (<see cref="EditorBrowsableState.Never"/>) and take nothing but their owner.
/// <para>
/// All Gantt state - parameters, callbacks, selection, inline edit, drag, zoom - stays in
/// <see cref="TmGantt"/>; a part reads it through <see cref="Owner"/> (one source of truth, no second copy
/// to keep in sync) and draws its slice of the DOM.
/// </para>
/// <para>
/// A part's event handlers mutate the owner's state, and the owner's own markup (filter panel, task panel,
/// context menu, dialogs) depends on it. Blazor re-renders only the component that RECEIVES an event, which
/// would be the part, so the part forwards every event to the owner (<see cref="IHandleEvent"/>) and the
/// owner re-renders as it did when the markup was its own.
/// </para>
/// <para>
/// A part with a <see cref="RenderState"/> (the tree and the timeline: one element per row, bar, header cell
/// and dependency) skips a render pass in which that state is unchanged, so a toolbar action that only opens
/// a menu, a dialog or a panel no longer repaints every bar. The state is compared by value (scalars) and by
/// identity (lists the owner rebuilds on every data change).
/// </para>
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class TmGanttPart : ComponentBase, IHandleEvent
{
    private object? _renderedState;
    private bool _hasRendered;

    /// <summary>The Gantt this part belongs to.</summary>
    [Parameter, EditorBrowsable(EditorBrowsableState.Never)] public TmGantt Owner { get; set; } = default!;

    /// <summary>
    /// Everything this part draws, as a value with structural equality; <c>null</c> (default) opts out of the
    /// render gate. Every owner field the markup reads MUST be part of it.
    /// </summary>
    protected virtual object? RenderState => null;

    /// <summary>Resolves an internal test id through the owner, so a <c>TestIdPrefix</c> reaches every part.</summary>
    protected string TestId(string name) => Owner.TestIdFor(name);

    /// <summary>Invariant-culture number, the way every pixel value reaches an inline style.</summary>
    protected static string F(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Avatar background (image or a name-derived hue); shared with the owner's People view.</summary>
    protected static string GetAvatarStyle(TmWorkItemAssignee assignee) => TmGantt.GetAvatarStyle(assignee);

    /// <summary>Avatar initial; shared with the owner's People view.</summary>
    protected static string GetAvatarInitial(TmWorkItemAssignee assignee) => TmGantt.GetAvatarInitial(assignee);

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // The first render never asks ShouldRender, so the state it paints is recorded here (nothing
        // changes between this point and the first render pass).
        var state = RenderState;
        if (state is null) return;
        _renderedState = state;
        _hasRendered = true;
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var state = RenderState;
        if (state is null) return true;
        if (_hasRendered && state.Equals(_renderedState)) return false;

        // Recorded at decision time, not after the render: a handler that changes the owner's state after an
        // await re-renders this part before any OnAfterRender, and the comparison must use what was last painted.
        _renderedState = state;
        _hasRendered = true;
        return true;
    }

    /// <inheritdoc />
    Task IHandleEvent.HandleEventAsync(EventCallbackWorkItem callback, object? arg)
        => Owner.HandlePartEventAsync(callback, arg);
}