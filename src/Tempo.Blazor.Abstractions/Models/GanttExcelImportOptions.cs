using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>Options for an Excel Gantt import.</summary>
public sealed class GanttExcelImportOptions
{
    /// <summary>Default <see cref="IdPrefix"/>.</summary>
    public const string DefaultIdPrefix = "xlsx-row-";

    /// <summary>
    /// Resolves an assignee name from the sheet into a real person. Return <c>null</c> to skip the
    /// name (the host does not know it). When null, every name becomes a plain
    /// <see cref="TmWorkItemAssignee"/> with the normalized name as its id.
    /// </summary>
    public Func<string, TmWorkItemAssignee?>? AssigneeResolver { get; init; }

    /// <summary>
    /// Prefix of every imported task id: the id is <c>{IdPrefix}{n}</c>, where <c>n</c> is the
    /// 1-based data-row ordinal. The default keeps ids stable across imports of the same sheet
    /// (<c>xlsx-row-3</c>); pass a fresh value per import (e.g. <c>xlsx-{8 random hex}-row-</c>)
    /// when one stream can be imported more than once, so the ids of separate imports never collide.
    /// </summary>
    public string IdPrefix { get; init; } = DefaultIdPrefix;
}
