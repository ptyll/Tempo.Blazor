namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Which parts of a memory a reader may change in <see cref="TmMemoryPanel"/>'s inline editor.
/// <para>
/// The flag set is the host's permission, not a suggestion: a field whose flag is missing is neither rendered nor
/// reported, so a store that owns the category (a taxonomy an agent classifies into) can still let the reader fix the
/// text. <see cref="Text"/> is the default, which is exactly the pre-2.11.0 editor. <see cref="None"/> removes the Edit
/// action entirely, which is what a host with its own editor wants.
/// </para>
/// </summary>
[Flags]
public enum MemoryEditableFields
{
    /// <summary>Nothing is editable, so no Edit action is offered.</summary>
    None = 0,

    /// <summary>The fact itself. The default.</summary>
    Text = 1,

    /// <summary>The category the fact is grouped under.</summary>
    Category = 2,

    /// <summary>Where the fact came from ("From: …").</summary>
    Source = 4,

    /// <summary>Every field the editor can carry.</summary>
    All = Text | Category | Source,
}
