namespace Tempo.Blazor.Helpers;

using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Toolbar;

/// <summary>
/// Internal. Splits toolbar buttons into the ones that stay on the bar and the ones in the "More"
/// menu. It is <see cref="ActionOverflowLayout.Partition{T}(IReadOnlyList{T}, int, Func{T, int}, Func{T, Tempo.Blazor.Components.Actions.ActionOverflow})"/>
/// with the toolbar's vocabulary: <see cref="ToolbarButtonPriority"/> is the rank, OverflowOnly is the
/// <c>Always</c> pin — one ordering rule for every action surface.
/// </summary>
internal static class ToolbarOverflowLayout
{
    /// <summary>The overflow rank of a priority: Secondary leaves before Primary.</summary>
    public static int Rank(ToolbarButtonPriority priority) => priority switch
    {
        ToolbarButtonPriority.OverflowOnly => 0,
        ToolbarButtonPriority.Secondary => 1,
        // Pinned never competes (it is the Never pin), the rank is only a stable placeholder.
        ToolbarButtonPriority.Pinned => 2,
        _ => 2,
    };

    /// <summary>
    /// Partitions <paramref name="items"/>. <paramref name="maxVisible"/> is how many non-OverflowOnly
    /// buttons the measured bar can hold; <see langword="null"/> (not measured yet, or
    /// <see cref="ToolbarOverflow.None"/> never measures) keeps every non-OverflowOnly button on the bar.
    /// </summary>
    public static (IReadOnlyList<T> Bar, IReadOnlyList<T> Menu) Resolve<T>(
        IReadOnlyList<T> items,
        int? maxVisible,
        Func<T, ToolbarButtonPriority> priority)
        => ActionOverflowLayout.Partition(
            items,
            // null = not measured / nothing to measure: the budget is every button there is. The measured count is
            // the COLLAPSIBLE buttons that fit - the shared partition spends its budget on Never pins first, so they
            // are added back (a pinned button is fixed width in the measurement, not part of the count).
            maxVisible is { } measured ? measured + items.Count(item => priority(item) == ToolbarButtonPriority.Pinned) : Math.Max(1, items.Count),
            item => Rank(priority(item)),
            item => priority(item) switch
            {
                ToolbarButtonPriority.OverflowOnly => ActionOverflow.Always,
                ToolbarButtonPriority.Pinned => ActionOverflow.Never,
                _ => ActionOverflow.Auto,
            });
}
