namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How the model list of <c>TmModelPicker</c> is grouped.
/// <para>
/// Grouping is a READING aid, never a filter: every supplied model stays reachable in exactly one group, and
/// a model that does not report the property the list is grouped by lands in the localized "Other" group
/// instead of disappearing.
/// </para>
/// </summary>
public enum AiModelGroupBy
{
    /// <summary>One flat list, no group headers. The whole list is a single <c>role="listbox"</c>.</summary>
    None = 0,

    /// <summary>Grouped by <see cref="AiModelInfo.Provider"/> — the vendor or gateway the model is called
    /// through.</summary>
    Provider = 1,

    /// <summary>Grouped by <see cref="AiModelInfo.Family"/> — the model generation or line ("GPT-4",
    /// "Claude 3.5").</summary>
    Family = 2,
}
