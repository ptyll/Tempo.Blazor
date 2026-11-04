namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The result of assembling a <see cref="PromptTemplate"/>: the resolved variables, the runs the preview renders,
/// the substituted prompt, the effective values and every reason the prompt is incomplete.
/// <para>
/// The assembly is ALWAYS produced, even when it is invalid: the preview shows the prompt with its unfilled
/// placeholders left in, so the reader sees exactly what is missing. <see cref="ToUse"/> refuses such a result, so
/// an incomplete prompt can never reach a host by accident.
/// </para>
/// </summary>
public sealed record PromptTemplateBuildResult
{
    /// <summary>The variables the assembly used — the template's declared ones, or the ones derived from its
    /// body. In declaration (or first-appearance) order.</summary>
    public IReadOnlyList<PromptVariable> Variables { get; init; } = [];

    /// <summary>The assembled prompt as runs, in reading order. Concatenating <see cref="PromptTemplateSegment.Text"/>
    /// yields <see cref="Prompt"/> exactly.</summary>
    public IReadOnlyList<PromptTemplateSegment> Segments { get; init; } = [];

    /// <summary>The assembled prompt: literal text with every variable substituted. An unfilled variable is left
    /// as its literal placeholder <c>{{name}}</c> rather than replaced with an empty string.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>
    /// The effective values by variable name: the supplied value, or the template's default when the field was
    /// left empty. A variable with neither is absent from the dictionary. A value for a name the template does not
    /// declare is ignored.
    /// </summary>
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();

    /// <summary>Every reason the prompt is incomplete, in variable order. Empty when the prompt is valid.</summary>
    public IReadOnlyList<PromptTemplateValidationError> Errors { get; init; } = [];

    /// <summary>Whether the prompt is complete — no <see cref="Errors"/> and therefore safe to hand to a host.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>The first validation error of a variable, or null when that variable is fine.</summary>
    /// <param name="variableName">The variable name to look up, matched ordinally.</param>
    /// <returns>The error, or null.</returns>
    public PromptTemplateValidationError? ErrorFor(string variableName)
        => Errors.FirstOrDefault(error => string.Equals(error.VariableName, variableName, StringComparison.Ordinal));

    /// <summary>
    /// Turns a valid assembly into the payload of "Use". Invalid assemblies are refused: an incomplete prompt is
    /// a bug in the caller, never something a host should receive.
    /// </summary>
    /// <param name="template">The template that was assembled — it supplies the id and the title.</param>
    /// <returns>The payload for <c>TmPromptLibrary.OnUse</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="template"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The assembly has validation errors.</exception>
    public PromptTemplateUse ToUse(PromptTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (!IsValid)
        {
            throw new InvalidOperationException(
                "An incomplete prompt cannot be used: " +
                string.Join(", ", Errors.Select(error => $"{error.VariableName} ({error.Kind})")) + ".");
        }

        return new PromptTemplateUse
        {
            TemplateId = template.Id,
            Title = template.Title,
            Prompt = Prompt,
            Values = Values
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal),
        };
    }
}
