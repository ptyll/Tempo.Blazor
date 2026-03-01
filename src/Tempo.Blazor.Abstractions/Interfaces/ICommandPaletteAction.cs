namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents an action available in TmCommandPalette (Ctrl+K).
/// </summary>
public interface ICommandPaletteAction
{
    /// <summary>Unique identifier for the action.</summary>
    string Id { get; }

    /// <summary>Primary display title shown in the command palette.</summary>
    string Title { get; }

    /// <summary>Optional secondary description.</summary>
    string? Description { get; }

    /// <summary>Optional icon name from IconNames constants.</summary>
    string? Icon { get; }

    /// <summary>Optional keyboard shortcut hint to display (e.g. "⌘K", "Ctrl+N").</summary>
    string? Shortcut { get; }

    /// <summary>Optional category for grouping actions.</summary>
    string? Category { get; }

    /// <summary>The async action to execute when selected.</summary>
    Func<Task> Execute { get; }
}
