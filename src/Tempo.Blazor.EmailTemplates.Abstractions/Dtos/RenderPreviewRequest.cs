namespace Tempo.Blazor.EmailTemplates.Abstractions.Dtos;

/// <summary>Request to render a preview of a template body with optional variable data.</summary>
public sealed record RenderPreviewRequest
{
    /// <summary>Gets the serialized document content (JSON).</summary>
    public string ContentJson { get; init; } = string.Empty;

    /// <summary>Gets the optional variable data as JSON.</summary>
    public string? VariablesJson { get; init; }

    /// <summary>
    /// Gets whether variables are substituted. <see langword="false"/> keeps the <c>{{ … }}</c> tokens in
    /// the rendered markup, which is what an HTML export with unresolved placeholders needs.
    /// </summary>
    public bool SubstituteVariables { get; init; } = true;

    /// <summary>
    /// Gets the culture name (for example <c>cs-CZ</c>) the server should format values in;
    /// <see langword="null"/> = the renderer default (the document language, otherwise invariant).
    /// </summary>
    public string? CultureName { get; init; }
}
