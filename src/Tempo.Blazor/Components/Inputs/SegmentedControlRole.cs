namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// The ARIA pattern a <see cref="TmSegmentedControl{TValue}"/> exposes. The visual design is the same in
/// both cases — what the control MEANS decides.
/// </summary>
public enum SegmentedControlRole
{
    /// <summary>
    /// The segments pick one VALUE out of a set, so the control is a <c>radiogroup</c> of
    /// <c>radio</c> options with <c>aria-checked</c> and a roving tabindex (the APG radio-group
    /// pattern). This is the default, and the reason the control is not simply a row of buttons.
    /// </summary>
    RadioGroup,

    /// <summary>
    /// The segments switch which CONTENT is shown, so the control is a <c>tablist</c> of <c>tab</c>
    /// options with <c>aria-selected</c> and an <c>aria-controls</c> pointing at the tab's panel. A host
    /// that uses this mode owns the panels and their <c>role="tabpanel"</c> wrapper.
    /// </summary>
    Tabs
}
