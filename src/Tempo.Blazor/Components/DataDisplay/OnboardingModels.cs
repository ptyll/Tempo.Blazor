namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// Presentation of a <see cref="TmOnboardingChecklist"/>.
/// </summary>
public enum OnboardingChecklistLayout
{
    /// <summary>
    /// A plain vertical list without card chrome — the default, suitable for a sidebar or a page
    /// section that already provides its own surface.
    /// </summary>
    List,

    /// <summary>
    /// One card surface: the title header, the progress block and the list inside a bordered,
    /// rounded panel. The setup-guide presentation.
    /// </summary>
    Card,

    /// <summary>
    /// A responsive tile grid — each step becomes a tile. The tips-panel presentation; prefer it
    /// for short titles, because a tile has much less room for a description than a list row.
    /// </summary>
    Grid,
}

/// <summary>
/// One step of a <see cref="TmOnboardingChecklist"/>: what to do, how it is presented and — once
/// the user worked through it — whether it is done.
/// </summary>
/// <remarks>
/// <para>
/// The checklist writes <see cref="IsDone"/> on the instance it was given before it raises
/// <c>TmOnboardingChecklist.OnTaskToggle</c>, so the component works without a bound handler (the
/// checkbox, the progress and the celebration all follow the model). A host that persists progress
/// handles the callback and stores the value; a host that reloads the steps from storage gives the
/// checklist NEW instances, which is why the checklist treats a new <c>Tasks</c> list identity as a
/// new run (a dismissed checklist comes back).
/// </para>
/// <para>
/// <see cref="Id"/> must be unique within one checklist: it is the suffix of the step's
/// <c>data-testid</c> and part of its checkbox/description element ids, so two steps that share one
/// render fine but make a label point at the first checkbox and make host tests ambiguous.
/// </para>
/// </remarks>
public sealed class OnboardingTask
{
    /// <summary>Stable identifier of the step, unique within the checklist (see the type remarks).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The step's text — the visible label AND the accessible name of its checkbox.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional explanation under the title. Wired to the checkbox through <c>aria-describedby</c>.</summary>
    public string? Description { get; set; }

    /// <summary>Whether the step is done. The checklist writes this value when the user toggles it.</summary>
    public bool IsDone { get; set; }

    /// <summary>
    /// Marks the step as optional: it renders a localized "Optional" badge, it still counts in the
    /// progress sentence, but it never blocks the celebration — the checklist is complete once every
    /// required step is done.
    /// </summary>
    public bool IsOptional { get; set; }

    /// <summary>
    /// Group the step belongs to. Steps that share a group render under one collapsible header with
    /// their own progress count; steps with no group render as a plain leading list. The value is
    /// used as the group's visible title and as its stable key, so its casing and spacing matter.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Visible text of the step's call-to-action. An action renders only when it has a target —
    /// <see cref="ActionHref"/> or <see cref="OnAction"/>; a label alone renders nothing.
    /// </summary>
    public string? ActionLabel { get; set; }

    /// <summary>
    /// Navigation target of the call-to-action; renders a link. <see cref="OnAction"/> wins when
    /// both are set (the handler decides what to do, including navigating).
    /// </summary>
    public string? ActionHref { get; set; }

    /// <summary>
    /// Handler of the call-to-action when it is a command rather than a link. Receives the step; the
    /// checklist awaits it, so a handler that loads data keeps its busy state. Setting it does NOT
    /// check the step — completing a step stays the user's decision.
    /// </summary>
    public Func<OnboardingTask, Task>? OnAction { get; set; }
}
