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
        => Partition(items, maxVisible, static item => item.Priority, static item => item.Overflow);

    /// <summary>
    /// The pin-aware partition (F4): <see cref="ActionOverflow.Always"/> items are always in the
    /// overflow list and never count against <paramref name="maxVisible"/>;
    /// <see cref="ActionOverflow.Never"/> items are always visible and count against it. The
    /// <see cref="ActionOverflow.Auto"/> items share what is left of the budget (never below zero
    /// once a Never item exists; <paramref name="maxVisible"/> below 1 clamps to 1 first), ranked
    /// exactly like the plain overload: the lowest rank overflows first, ties drop the last item.
    /// Both lists keep the Items order.
    /// </summary>
    public static (IReadOnlyList<T> Visible, IReadOnlyList<T> Overflow) Partition<T>(
        IReadOnlyList<T> items,
        int maxVisible,
        Func<T, int> rank,
        Func<T, ActionOverflow> pin)
    {
        var pins = new ActionOverflow[items.Count];
        var neverCount = 0;
        var autoCount = 0;
        for (var i = 0; i < items.Count; i++)
        {
            pins[i] = pin(items[i]);
            if (pins[i] == ActionOverflow.Never) neverCount++;
            else if (pins[i] == ActionOverflow.Auto) autoCount++;
        }

        // The classic clamp keeps a surface with only Auto items showing at least one action; once a
        // Never item holds its place the Auto items may legitimately get nothing.
        var budget = Math.Max(1, maxVisible);
        var autoBudget = neverCount == 0 ? budget : Math.Max(0, budget - neverCount);
        var autoOverflow = Math.Max(0, autoCount - autoBudget);

        var overflowIndexes = new HashSet<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (pins[i] == ActionOverflow.Always) overflowIndexes.Add(i);
        }

        if (autoOverflow > 0)
        {
            var dropped = Enumerable.Range(0, items.Count)
                .Where(i => pins[i] == ActionOverflow.Auto)
                .OrderBy(i => rank(items[i]))
                .ThenByDescending(i => i)
                .Take(autoOverflow);
            foreach (var index in dropped) overflowIndexes.Add(index);
        }

        if (overflowIndexes.Count == 0)
        {
            return (items, []);
        }

        var visible = new List<T>(items.Count - overflowIndexes.Count);
        var overflow = new List<T>(overflowIndexes.Count);
        for (var i = 0; i < items.Count; i++)
        {
            (overflowIndexes.Contains(i) ? overflow : visible).Add(items[i]);
        }

        return (visible, overflow);
    }

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
        => Partition(items, maxVisible, rank, static _ => ActionOverflow.Auto);
}
