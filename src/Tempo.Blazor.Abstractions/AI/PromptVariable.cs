namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One variable of a <see cref="PromptTemplate"/>: the placeholder the body spells as <c>{{name}}</c>, and the
/// form the prompt library renders for it.
/// <para>
/// The declaration is OPTIONAL. A template that declares no <see cref="PromptTemplate.Variables"/> gets one
/// derived variable per placeholder found in the body (<see cref="PromptTemplateParser.ExtractVariables"/>): its
/// name as the label, required, and a single line of text. A template that declares them takes over entirely —
/// the declared label, description, default, required flag, kind and options are what the reader sees, and a
/// placeholder the declaration does not name stays literal text.
/// </para>
/// <para>
/// A value is TEXT and is never evaluated. When the reader leaves a variable empty the template's
/// <see cref="DefaultValue"/> is used; a required variable with neither a value nor a default makes the assembly
/// invalid (<see cref="PromptTemplateValidationErrorKind.MissingRequired"/>), so a prompt is never handed to a
/// host with a hole in it.
/// </para>
/// </summary>
public sealed record PromptVariable
{
    /// <summary>Creates a required single-line variable.</summary>
    /// <param name="name">The placeholder name, as spelled between the braces. Null or whitespace is stored as an
    /// empty string, so nothing renders the word "null". Matching is ordinal (case-sensitive), because the
    /// placeholder in the body is.</param>
    /// <param name="label">The visible label; null falls back to <see cref="Name"/>.</param>
    public PromptVariable(string name, string? label = null)
    {
        Name = name ?? string.Empty;
        Label = label;
    }

    /// <summary>The placeholder name, without the braces. Matched against the body ordinally (case-sensitive).</summary>
    public string Name { get; init; }

    /// <summary>The visible label of the field. Null (or whitespace) falls back to <see cref="Name"/>, so a
    /// derived variable is still labelled by something the reader recognises.</summary>
    public string? Label { get; init; }

    /// <summary>Optional help text rendered under the field, in the host's own words.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The value used when the reader leaves the field empty. Null (the default) means there is no default; a
    /// whitespace-only default counts as no default, because a prompt made of spaces is not a filled prompt.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Whether the variable has to be filled in. Default <c>true</c>: a placeholder the author wrote has to be
    /// replaced. An optional variable that stays empty is left in the prompt as its literal placeholder and does
    /// not make the assembly invalid.
    /// </summary>
    public bool IsRequired { get; init; } = true;

    /// <summary>
    /// Which input the prompt library renders. Default <see cref="PromptVariableKind.Text"/>. With
    /// <see cref="PromptVariableKind.Select"/> the <see cref="Options"/> list is the offered set and a value
    /// outside it is a validation error.
    /// </summary>
    public PromptVariableKind Kind { get; init; } = PromptVariableKind.Text;

    /// <summary>
    /// The values a <see cref="PromptVariableKind.Select"/> variable offers. Null or empty means the select has
    /// nothing to offer and no value can satisfy it, so the prompt library renders a text field instead. The
    /// values are shown verbatim (they are the host's data), and matching is ordinal.
    /// </summary>
    public IReadOnlyList<string>? Options { get; init; }

    /// <summary>The label the reader sees: <see cref="Label"/> when it has one, otherwise <see cref="Name"/>.</summary>
    public string DisplayLabel => string.IsNullOrWhiteSpace(Label) ? Name : Label!;
}
