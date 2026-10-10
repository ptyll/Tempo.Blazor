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
        => Partition(items, maxVisible, static item => item.Priority);

    /// <summary>
    /// The pin-aware partition (F4): <see cref="ActionOverflow.Always"/> items are always in the
    /// overflow list and never count against <paramref name="maxVisible"/>;
    /// <see cref="ActionOverflow.Never"/> items are always visible and count against it (the
    /// remaining budget — never below zero — is what the <see cref="ActionOverflow.Auto"/> items
    /// share, ranked like the plain overload). Both lists keep the Items order.
    /// </summary>
    public static (IReadOnlyList<T> Visible, IReadOnlyList<T> Overflow) Partition<T>(
        IReadOnlyList<T> items,
        int maxVisible,
        Func<T, int> rank,
        Func<T, ActionOverflow> pin)
        => throw new NotImplementedException();
    /// <summary>
    /// The generic partition (Y10, review round 2): the ordering rules — visible budget,
    /// ascending rank overflows first, ties drop the last item, both lists keep the Items order —
    /// in one implementation any action surface ranks its own item type through. The
    /// <c>TmActionItem</c> overload delegates here; F4's toolbar overflow builds on this.
    /// </summary>
    public static (IReadOnlyList<T> Visible, IReadOnlyList<T> Overflow) Partition<T>(
        IReadOnlyList<T> items,
        int maxVisible,
        Func<T, int> rank)
    {
        var budget = Math.Max(1, maxVisible);
        if (items.Count <= budget)
        {
            return (items, []);
        }

        var overflowCount = items.Count - budget;
        var overflowIndexes = items
            .Select((item, index) => (Item: item, Index: index))
            .OrderBy(entry => rank(entry.Item))
            .ThenByDescending(entry => entry.Index)
            .Take(overflowCount)
            .Select(entry => entry.Index)
            .ToHashSet();

        var visible = new List<T>(budget);
        var overflow = new List<T>(overflowCount);
        for (var i = 0; i < items.Count; i++)
        {
            (overflowIndexes.Contains(i) ? overflow : visible).Add(items[i]);
        }

        return (visible, overflow);
    }
}
