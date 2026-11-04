namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What the user chose in <c>TmModelPicker</c>: which model, whether extended thinking is on and which
/// reasoning-effort level is set. This is the two-way bound value (<c>@bind-Selection</c>), so it is the whole
/// answer a host needs — never a model id plus two loose parameters.
/// <para>
/// The record is a VALUE: the picker raises a new instance on every change (a model pick resets the reasoning
/// options to the chosen model's defaults), so a host can store it as-is and compare it with <c>==</c>. Null
/// means "nothing chosen yet" and renders the placeholder trigger.
/// </para>
/// <para>
/// <see cref="ExtendedThinking"/> and <see cref="EffortLevel"/> are stored even for a model that does not
/// report them: the picker only STOPS OFFERING the controls, it never rewrites a selection the host supplied.
/// Whether the combination is meaningful is the host's (and its provider's) business. A pick in the picker
/// starts a DIFFERENT model from that model's defaults, while a programmatic switch through
/// <see cref="ForModel"/> keeps the reasoning options verbatim — see its remarks.
/// </para>
/// </summary>
public sealed record AiModelSelection
{
    /// <summary>Creates a selection of one model with the reasoning options at their defaults.</summary>
    /// <param name="modelId">The <see cref="AiModelInfo.Id"/> of the chosen model. Null or whitespace is
    /// stored as an empty string — an empty selection should be expressed as a null
    /// <c>Selection</c>, but a half-built one must not make the picker throw.</param>
    public AiModelSelection(string modelId)
    {
        ModelId = modelId ?? string.Empty;
    }

    /// <summary>The <see cref="AiModelInfo.Id"/> of the chosen model. Never null; empty when the selection
    /// was constructed without one.</summary>
    public string ModelId { get; init; }

    /// <summary>
    /// Whether the user turned extended thinking on. Default <c>false</c> — a model that supports it starts
    /// with the cheaper setting and the reader opts in.
    /// </summary>
    public bool ExtendedThinking { get; init; }

    /// <summary>
    /// The <see cref="AiModelEffortLevel.Id"/> of the chosen effort level, or null for the model's default
    /// (the provider's own default, which the host does not need to name). The picker never invents a level:
    /// an id the selected model does not offer is kept as-is and shown as unselected.
    /// </summary>
    public string? EffortLevel { get; init; }

    /// <summary>Creates a copy of this selection for another model, keeping the reasoning options.</summary>
    /// <param name="modelId">The model to switch to.</param>
    /// <returns>A new selection with the same <see cref="ExtendedThinking"/> and
    /// <see cref="EffortLevel"/>.</returns>
    /// <remarks>
    /// Unlike a pick in <c>TmModelPicker</c>, which resets the reasoning options for a different model, this
    /// keeps them verbatim — the new model may not offer the stored effort id. Use
    /// <c>new AiModelSelection(modelId)</c> for the picker's reset semantics.
    /// </remarks>
    public AiModelSelection ForModel(string modelId) => this with { ModelId = modelId ?? string.Empty };
}
