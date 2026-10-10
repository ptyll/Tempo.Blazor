namespace Tempo.Blazor.Helpers;

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
    public static int Rank(ToolbarButtonPriority priority) => throw new NotImplementedException();

    /// <summary>
    /// Partitions <paramref name="items"/>. <paramref name="maxVisible"/> is how many non-OverflowOnly
    /// buttons the measured bar can hold; <see langword="null"/> (not measured yet, or
    /// <see cref="ToolbarOverflow.None"/> never measures) keeps every non-OverflowOnly button on the bar.
    /// </summary>
    public static (IReadOnlyList<T> Bar, IReadOnlyList<T> Menu) Resolve<T>(
        IReadOnlyList<T> items,
        int? maxVisible,
        Func<T, ToolbarButtonPriority> priority) => throw new NotImplementedException();
}
