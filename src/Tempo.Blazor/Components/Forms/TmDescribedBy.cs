namespace Tempo.Blazor.Components.Forms;

/// <summary>
/// Internal merge for <c>aria-describedby</c> id groups. Every control that both owns a described-by
/// chain (its error, its help text) and receives references from outside (a surrounding
/// <see cref="TmFormField"/> cascade, the host's own ids) joins the groups through this helper: the
/// first occurrence of every id is kept, empty and whitespace-only groups are dropped, and ids are
/// split on ANY whitespace (the <c>Split</c> overload with a <c>null</c> separator array and
/// <c>RemoveEmptyEntries</c>), so a host may pass "a b" with a regular space, a non-breaking space
/// or a newline interchangeably. An empty result yields <c>null</c> — the attribute stays off the
/// element instead of rendering empty.
/// </summary>
internal static class TmDescribedBy
{
    /// <summary>Joins described-by id groups into one whitespace-separated value, keeping the first
    /// occurrence of every id and dropping empty groups.</summary>
    /// <param name="groups">The groups in order: the control's own chain first, then the
    /// <c>TmFormField</c> cascade, then the host's extra ids.</param>
    internal static string? Join(params string?[] groups)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ids = new List<string>();
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group))
            {
                continue;
            }

            foreach (var id in group.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
        }

        return ids.Count == 0 ? null : string.Join(' ', ids);
    }
}
