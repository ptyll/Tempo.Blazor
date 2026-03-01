namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Represents a tag in TmTagPicker and TmTagEditorDialog.
/// </summary>
public interface ITag
{
    /// <summary>Unique identifier for the tag.</summary>
    string Id { get; }

    /// <summary>Display name of the tag.</summary>
    string Name { get; }

    /// <summary>CSS color value for the tag badge (e.g. "#3b82f6" or "var(--tm-color-primary)").</summary>
    string Color { get; }
}
