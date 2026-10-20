namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>The fully rendered output of a template: substituted subject/preheader, HTML and text.</summary>
/// <param name="Html">
/// The rendered HTML body. Empty (<c>""</c>, never <see langword="null"/>) when the markup could not be
/// built or could not be checked safely — an unchecked or half-compiled body is never handed to a host.
/// </param>
/// <param name="Subject">The subject after variable substitution.</param>
/// <param name="Preheader">The preheader after variable substitution, if any.</param>
/// <param name="TextVersion">The plain-text alternative after variable substitution.</param>
/// <param name="Errors">Errors and warnings gathered across the pipeline phases, in order.</param>
public sealed record RenderResult(
    string Html,
    string Subject,
    string? Preheader,
    string TextVersion,
    IReadOnlyList<RenderError> Errors)
{
    /// <summary>
    /// Gets whether rendering completed without errors. A <see cref="RenderErrorSeverity.Warning"/>
    /// (a URL replaced with <c>#</c>, a removed event handler) does not fail the render — the message
    /// and its HTML are usable, so the host can still send it.
    /// </summary>
    public bool Success => Errors.All(error => error.Severity != RenderErrorSeverity.Error);

    /// <summary>Gets whether the render produced any <see cref="RenderErrorSeverity.Warning"/>.</summary>
    public bool HasWarnings => Errors.Any(error => error.Severity == RenderErrorSeverity.Warning);
}
