namespace Tempo.Blazor.Abstractions.Models;

/// <summary>An action offered by the Actions section of the Gantt task panel (the same commands as the task context menu).</summary>
public enum GanttTaskAction
{
    /// <summary>Marks the task done: status Done and 100 % complete.</summary>
    MarkDone,

    /// <summary>Inserts a new task above the task.</summary>
    AddAbove,

    /// <summary>Inserts a new task below the task.</summary>
    AddBelow,

    /// <summary>Inserts a new child task under the task.</summary>
    AddChild,

    /// <summary>Creates a copy of the task below it.</summary>
    Duplicate,

    /// <summary>Removes the task.</summary>
    Delete,

    /// <summary>Moves the task one place up among its siblings (the non-drag alternative of the tree reorder).</summary>
    MoveUp,

    /// <summary>Moves the task one place down among its siblings (the non-drag alternative of the tree reorder).</summary>
    MoveDown,
}