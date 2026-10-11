namespace Tempo.Blazor.Models;

/// <summary>Represents a keyboard shortcut with key combination and description.</summary>
/// <param name="Keys">The key combination, split on <paramref name="KeySeparator"/> into one <c>kbd</c> per part.</param>
/// <param name="Description">What the shortcut does.</param>
/// <param name="KeySeparator">
/// What joins the parts of <paramref name="Keys"/>. Defaults to <c>+</c>, so "Ctrl+N" renders as a
/// chord. Pass <c> / </c> for alternatives ("Arrow Up / Arrow Down") so the help does not present
/// two alternative keys as one chord.
/// </param>
public record TmKeyboardShortcut(string Keys, string Description, string KeySeparator = "+");

/// <summary>A named category of keyboard shortcuts.</summary>
public record TmShortcutCategory(string Title, IEnumerable<TmKeyboardShortcut> Shortcuts);
