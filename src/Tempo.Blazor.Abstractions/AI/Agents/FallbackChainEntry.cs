namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One model in the ORDERED chain a host falls back through: the model's identity plus the name the reader should see.
/// <para>
/// The record is a VALUE and the chain is an ordered list of these, so a reorder is a new list rather than a mutation.
/// It deliberately carries no score, price or capability: a chain is a sequence of choices, and the facts that drive
/// the choice live in the host's <see cref="AiModelInfo"/> catalogue.
/// </para>
/// </summary>
public sealed record FallbackChainEntry
{
    /// <summary>Creates a chain entry.</summary>
    /// <param name="modelId">
    /// The identity of the model in the host's catalogue. Null or whitespace generates one, so a list built in a loop
    /// still renders; a host that matches the entry to its own record supplies its id.
    /// </param>
    /// <param name="label">
    /// The model as the reader should see it. Null or whitespace falls back to <see cref="ModelId"/>, so no row is ever
    /// blank.
    /// </param>
    public FallbackChainEntry(string modelId, string? label = null)
    {
        ModelId = string.IsNullOrWhiteSpace(modelId) ? Guid.NewGuid().ToString("n") : modelId!;
        Label = label ?? string.Empty;
    }

    /// <summary>Stable identity of the model in the chain. Never empty: a missing id is generated at construction.</summary>
    public string ModelId { get; init; }

    /// <summary>The model as the reader should see it. Empty falls back to <see cref="ModelId"/>.</summary>
    public string Label { get; init; }

    /// <summary>The name a surface prints: <see cref="Label"/> when the host supplied one, else <see cref="ModelId"/>.</summary>
    public string DisplayName => HasLabel ? Label : ModelId;

    /// <summary>Whether the host supplied a label for the model.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>
    /// Builds an entry from a catalogue model, so a host that offers <c>AvailableModels</c> can add one to its chain
    /// without copying the identity by hand.
    /// </summary>
    /// <param name="model">The catalogue model to add. Null returns <c>null</c>.</param>
    /// <returns>An entry naming <paramref name="model"/>; <c>null</c> for a null model.</returns>
    public static FallbackChainEntry? FromModel(AiModelInfo? model)
        => model is null ? null : new FallbackChainEntry(model.Id, model.Name);
}
