using Tempo.Blazor.EmailTemplates.Abstractions.Model;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// One preview request (§3.4). It travels outside the process untouched by design: the document is
/// serialized with <c>EmailTemplateSerializer</c> (canonical JSON — polymorphic blocks are its job, not
/// <c>System.Text.Json</c>'s) while <see cref="VariablesJson"/> and <see cref="CultureName"/> are plain
/// strings.
/// </summary>
public sealed record EmailPreviewRequest
{
    /// <summary>Gets the document to render.</summary>
    public required EmailTemplateDocument Document { get; init; }

    /// <summary>Gets the variable data as JSON; <see langword="null"/> = renderer default sample data.</summary>
    public string? VariablesJson { get; init; }

    /// <summary>
    /// Gets whether variables are substituted. <see langword="false"/> keeps the <c>{{ … }}</c> tokens
    /// in the output, which is what an HTML export with unresolved placeholders needs.
    /// </summary>
    public bool SubstituteVariables { get; init; } = true;

    /// <summary>
    /// Gets the culture name (for example <c>cs-CZ</c>); <see langword="null"/> = the renderer default
    /// (the document language, otherwise the invariant culture).
    /// </summary>
    public string? CultureName { get; init; }
}
