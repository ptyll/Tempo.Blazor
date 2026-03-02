namespace Tempo.Blazor.Models;

/// <summary>Represents a keyboard shortcut with key combination and description.</summary>
public record TmKeyboardShortcut(string Keys, string Description);

/// <summary>A named category of keyboard shortcuts.</summary>
public record TmShortcutCategory(string Title, IEnumerable<TmKeyboardShortcut> Shortcuts);
