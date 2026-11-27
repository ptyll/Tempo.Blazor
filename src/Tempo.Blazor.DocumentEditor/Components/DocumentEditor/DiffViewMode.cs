namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>How <see cref="TmDocumentDiffViewer"/> lays the two sides of a comparison out.</summary>
public enum DiffViewMode
{
    /// <summary>Two columns, one row per block: the old text on the left, the new text on the right, both highlighted.</summary>
    SideBySide,

    /// <summary>One column: a changed block shows its removed and added words in the same line.</summary>
    Unified,
}
