namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Concrete, ready-to-use <see cref="ICommandPaletteAction"/> implementation. Demos and hosts
/// previously wrote their own private copy of this class; the library now ships one so a palette
/// action list can be built without boilerplate.
/// </summary>
/// <param name="Id">Unique identifier for the action.</param>
/// <param name="Title">Primary display title shown in the command palette.</param>
/// <param name="Description">Optional secondary description.</param>
/// <param name="Icon">Optional icon name from IconNames constants.</param>
/// <param name="Shortcut">Optional keyboard shortcut hint to display.</param>
/// <param name="Category">Optional category for grouping actions.</param>
/// <param name="Badge">Short badge text shown at the trailing edge of the item.</param>
/// <param name="Code">Code/identifier shown monospace at the trailing edge; also matched by the local filter.</param>
/// <param name="BadgeTone">Tone of the trailing badge.</param>
/// <param name="Execute">The async action to execute when selected; defaults to a no-op.</param>
public sealed record CommandPaletteAction(
    string Id,
    string Title,
    string? Description = null,
    string? Icon = null,
    string? Shortcut = null,
    string? Category = null,
    string? Badge = null,
    string? Code = null,
    CommandPaletteBadgeTone BadgeTone = CommandPaletteBadgeTone.Neutral,
    Func<Task>? Execute = null) : ICommandPaletteAction
{
    /// <inheritdoc />
    Func<Task> ICommandPaletteAction.Execute => Execute ?? (() => Task.CompletedTask);
}
