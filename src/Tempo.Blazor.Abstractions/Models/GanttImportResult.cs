using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>
/// The full outcome of an import: the tasks, the dependencies that reference them and the
/// non-fatal warnings. Import never throws for data-level problems — they surface as warnings.
/// </summary>
public sealed class GanttImportResult
{
    /// <summary>
    /// Imported tasks, in sheet order. Task ids are stable per row within one import
    /// (<c>{GanttExcelImportOptions.IdPrefix}{ordinal}</c>, e.g. <c>xlsx-row-3</c>); predecessor row
    /// references and WBS lookups resolve against the same ordinals.
    /// </summary>
    public IReadOnlyList<TmWorkItem> Tasks { get; init; } = [];

    /// <summary>Dependencies between the imported tasks (predecessor → task).</summary>
    public IReadOnlyList<GanttDependency> Dependencies { get; init; } = [];

    /// <summary>Non-fatal problems; the import itself always succeeded when this result exists.</summary>
    public IReadOnlyList<GanttImportWarning> Warnings { get; init; } = [];
}
