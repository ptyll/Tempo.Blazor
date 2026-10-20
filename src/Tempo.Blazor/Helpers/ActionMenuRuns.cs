namespace Tempo.Blazor.Helpers;

using Tempo.Blazor.Components.Actions;

/// <summary>
/// One contiguous run of "More"-menu items sharing a group key (see <see cref="ActionMenuRuns.Split"/>).
/// <see cref="Key"/> is null for an ungrouped run; <see cref="Label"/> is the accessible name and visible
/// header of the run, read from its first item (null renders the run without a group wrapper).
/// </summary>
internal readonly record struct ActionMenuRun(string? Key, string? Label, IReadOnlyList<TmActionItem> Items);

/// <summary>
/// Splits "More"-menu items into contiguous group runs for the overflow menus of the action surfaces
/// (the F4 <c>TmToolbar</c> "More" and the F5 <c>TmMobileActionBar</c>). Grouping NEVER reorders — the
/// render order is the <c>Items</c> order (the F5 rule), so two runs of the same key stay apart and
/// consecutive ungrouped items merge into one run (no separator is drawn between them).
/// </summary>
internal static class ActionMenuRuns
{
    /// <summary>
    /// Splits the items (order preserved) into runs of consecutive items sharing one non-empty
    /// <see cref="TmActionItem.Group"/> key; items whose key is null, empty or whitespace are ungrouped
    /// and form runs with a null key. The run's label is its FIRST item's
    /// <see cref="TmActionItem.GroupLabel"/>; an empty label keeps the run (separators apply) but renders
    /// it without a named group wrapper.
    /// </summary>
    public static IReadOnlyList<ActionMenuRun> Split(IReadOnlyList<TmActionItem> items)
    {
        if (items.Count == 0)
        {
            return [];
        }

        var runs = new List<ActionMenuRun>();
        List<TmActionItem> current = [];
        string? currentKey = null;
        string? currentLabel = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var key = Normalize(item.Group);
            // A run breaks when the key changes; consecutive ungrouped items (both keys null) merge,
            // so no separator is ever drawn between ungrouped neighbours.
            if (current.Count > 0 && !string.Equals(key, currentKey, StringComparison.Ordinal))
            {
                runs.Add(new ActionMenuRun(currentKey, currentLabel, current));
                current = [];
                currentLabel = null;
            }

            currentKey = key;
            if (current.Count == 0)
            {
                // The label is the FIRST item's GroupLabel, full stop — a later member's label never
                // retroactively names a run whose first item carries none.
                currentLabel = Normalize(item.GroupLabel);
            }

            current.Add(item);
        }

        runs.Add(new ActionMenuRun(currentKey, currentLabel, current));
        return runs;

        static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
