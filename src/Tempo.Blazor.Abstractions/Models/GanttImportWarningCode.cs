namespace Tempo.Blazor.Abstractions.Models;

/// <summary>Why an import produced a warning. The UI localizes the code — a warning never carries ready-made text.</summary>
public enum GanttImportWarningCode
{
    /// <summary>A predecessor token referenced a row or WBS code that does not exist.</summary>
    UnknownPredecessor,

    /// <summary>A predecessor references the task it is declared on.</summary>
    SelfDependency,

    /// <summary>A dependency edge would close a cycle; the edge was skipped.</summary>
    CircularDependency,

    /// <summary>A WBS code's parent code (the code without its last segment) has no row.</summary>
    UnknownWbsParent,

    /// <summary>Two rows declare the same WBS code; the first occurrence wins.</summary>
    DuplicateWbs,

    /// <summary>A cell value could not be parsed and was ignored.</summary>
    UnparsedValue,

    /// <summary>Several columns share the same header; only the first of them is imported.</summary>
    DuplicateHeader,
}
