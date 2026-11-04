namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One reasoning-effort level a model offers ("low", "medium", "high" — whatever the host's vocabulary is).
/// <para>
/// The level is a VALUE the host hands over, not an enum: effort vocabularies differ per provider and change
/// with every model generation, so the library must not own a closed list. <see cref="Label"/> exists so a
/// host can show its own wording (already localized) while the stable <see cref="Id"/> is what
/// <see cref="AiModelSelection.EffortLevel"/> stores and what the host persists.
/// </para>
/// </summary>
public sealed record AiModelEffortLevel
{
    /// <summary>Creates an effort level.</summary>
    /// <param name="id">Stable identity of the level, stored in <see cref="AiModelSelection.EffortLevel"/>.
    /// Null or whitespace is stored as an empty string — a level without an id can never be selected, so the
    /// picker skips it.</param>
    /// <param name="label">The level as the reader should see it. Null or whitespace falls back to
    /// <see cref="Id"/>, so a host that only has ids still gets a readable control.</param>
    public AiModelEffortLevel(string id, string? label = null)
    {
        Id = id ?? string.Empty;
        Label = string.IsNullOrWhiteSpace(label) ? Id : label!;
    }

    /// <summary>Stable identity of the level (e.g. <c>"high"</c>). Never null; may be empty, and an empty
    /// id means the level cannot be selected.</summary>
    public string Id { get; init; }

    /// <summary>The level as the reader should see it. Never null or blank: it falls back to
    /// <see cref="Id"/>.</summary>
    public string Label { get; init; }
}
