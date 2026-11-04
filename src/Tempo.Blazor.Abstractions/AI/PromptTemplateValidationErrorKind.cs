namespace Tempo.Blazor.AI.Models;

/// <summary>
/// Why an assembled prompt is not valid. The prompt library maps the kind to its own localized message, so the
/// parser (which owns no localization) can report the fact without owning the wording.
/// </summary>
public enum PromptTemplateValidationErrorKind
{
    /// <summary>A required variable has neither a value nor a default, so its placeholder could not be replaced.
    /// </summary>
    MissingRequired = 0,

    /// <summary>A <see cref="PromptVariableKind.Select"/> variable was given a value that is not one of its
    /// <see cref="PromptVariable.Options"/>. The value is still substituted; the host is told it is not an
    /// offered option.</summary>
    NotAnOption = 1,
}
