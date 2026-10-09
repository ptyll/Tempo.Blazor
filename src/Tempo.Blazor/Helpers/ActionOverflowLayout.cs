namespace Tempo.Blazor.Helpers;

using Tempo.Blazor.Components.Actions;

/// <summary>
/// Pure overflow partition shared by action surfaces (the F5 <c>TmMobileActionBar</c>; F4's toolbar
/// overflow builds on the same helper). Rendering order is ALWAYS the order of
/// <paramref name="items"/> — <c>TmActionItem.Priority</c> is only an overflow rank: the
/// lowest-priority items leave the bar first, ties drop the last item first (user decision,
/// plan faf503eb F5 round 1). Disabled items still count towards the visible budget.
/// </summary>
internal static class ActionOverflowLayout
{
    /// <summary>
    /// Splits <paramref name="items"/> into what stays on the bar and what moves into the overflow
    /// menu. Both lists preserve the Items order; the overflow SET is chosen by ascending priority
    /// (ties: the later item in Items order drops first). <paramref name="maxVisible"/> below 1
    /// clamps to 1.
    /// </summary>
    public static (IReadOnlyList<TmActionItem> Visible, IReadOnlyList<TmActionItem> Overflow) Partition(
        IReadOnlyList<TmActionItem> items,
        int maxVisible)
        => throw new NotImplementedException();
}
