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

    /// <summary>
    /// Short badge text shown at the trailing edge of the item (e.g. the entity type "UC",
    /// "Decision"). Default interface member: null (no badge). Renders as a subtle
    /// <c>TmBadge</c> in <c>TmCommandPalette</c>; the tone comes from <see cref="BadgeTone"/>.
    /// </summary>
    string? Badge => null;

    /// <summary>
    /// Code/identifier shown monospace at the trailing edge of the item (e.g. "UC-042"). Also
    /// matched by the palette's local filter. Default interface member: null (no code).
    /// </summary>
    string? Code => null;

    /// <summary>
    /// Tone of the trailing badge. Default interface member: <see cref="CommandPaletteBadgeTone.Neutral"/>.
    /// </summary>
    CommandPaletteBadgeTone BadgeTone => CommandPaletteBadgeTone.Neutral;

    /// <summary>
    /// Extra terms the palette's local filter matches besides the title, description and code (a command's internal name, its
    /// untranslated category). Not displayed. Default interface member: null.
    /// </summary>
    IReadOnlyList<string>? SearchTerms => null;

    /// <summary>
    /// Whether the action is currently unavailable. Default interface member: false. A disabled
    /// action stays in the list and reachable with the arrow keys (it carries
    /// <c>aria-disabled="true"</c>), but Enter and a click do not execute it and the palette stays open.
    /// </summary>
    bool IsDisabled => false;

    /// <summary>
    /// Why the action is unavailable. Default interface member: null. Shown instead of
    /// <see cref="Description"/> while <see cref="IsDisabled"/> is true, so the reason is part of the option's
    /// accessible description.
    /// </summary>
    string? DisabledReason => null;

    /// <summary>The async action to execute when selected.</summary>
    Func<Task> Execute { get; }
}

/// <summary>
/// Tone of the trailing <c>Badge</c> on a <see cref="ICommandPaletteAction"/>. Mapped to the
/// <c>TmBadge</c> variants by <c>TmCommandPalette</c>; declared in Abstractions (without a UI
/// dependency) so a backend can compose palette items.
/// </summary>
public enum CommandPaletteBadgeTone
{
    /// <summary>Neutral gray badge (the default).</summary>
    Neutral,

    /// <summary>Accent-colored badge.</summary>
    Primary,

    /// <summary>Green badge.</summary>
    Success,

    /// <summary>Amber badge.</summary>
    Warning,

    /// <summary>Red badge.</summary>
    Danger,

    /// <summary>Blue badge.</summary>
    Info
}
