namespace Tempo.Blazor.Components.Actions;

/// <summary>
/// Whether an action surface may move a <see cref="TmActionItem"/> into its overflow ("More")
/// menu. The pin is independent of <see cref="TmActionItem.Priority"/>: priority ranks the
/// <see cref="Auto"/> items against each other, the pin removes an item from that contest.
/// </summary>
public enum ActionOverflow
{
    /// <summary>The default: the item stays visible while the budget allows and overflows by <see cref="TmActionItem.Priority"/> rank.</summary>
    Auto,

    /// <summary>The item never overflows. It counts against the visible budget but is never chosen to leave the surface.</summary>
    Never,

    /// <summary>The item is always in the overflow menu, whatever the budget — never rendered on the surface itself.</summary>
    Always,
}
