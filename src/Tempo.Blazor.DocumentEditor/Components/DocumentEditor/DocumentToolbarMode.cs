namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>Visual density and visibility mode for the document editor toolbar.</summary>
public enum DocumentToolbarMode
{
    /// <summary>Single-row icon ribbon: tabs, groups divided by rules, icon buttons with a tooltip, and a visible label only on the primary calls to action.</summary>
    Ribbon,

    /// <summary>The same row, denser and icon-only (no visible labels), for smaller work areas.</summary>
    Compact,

    /// <summary>Hides the ribbon for focused writing while keeping floating editing surfaces available.</summary>
    DistractionFree
}
