namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One reason an assembled prompt is incomplete: which variable and what is wrong with it. The parser reports the
/// facts; the prompt library turns them into a localized, visible field error and a single live-region sentence.
/// </summary>
public sealed record PromptTemplateValidationError
{
    /// <summary>Creates an error for a variable.</summary>
    /// <param name="variableName">The variable the error belongs to. Null is stored as an empty string.</param>
    /// <param name="kind">What is wrong with the variable.</param>
    public PromptTemplateValidationError(string variableName, PromptTemplateValidationErrorKind kind)
    {
        VariableName = variableName ?? string.Empty;
        Kind = kind;
    }

    /// <summary>The variable the error belongs to, matched against <see cref="PromptVariable.Name"/>.</summary>
    public string VariableName { get; init; }

    /// <summary>What is wrong with the variable.</summary>
    public PromptTemplateValidationErrorKind Kind { get; init; }
}
