using Tempo.Blazor.EmailTemplates.Abstractions.Model;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Renders an <see cref="EmailTemplateDocument"/> with a data model into HTML and text.
/// </summary>
/// <remarks>
/// The escaping mode per pass (§3.7) is a property of the render pipeline, so it only exists when the
/// engine implements the internal <c>IEscapingTemplateEngine</c> contract. The Tempo
/// <c>ScribanTemplateEngine</c> does; a host-supplied <see cref="Templating.ITemplateEngine"/> cannot be
/// asked for a raw pass, and the default renderer logs a warning at construction — such a host is
/// responsible for escaping its own output.
/// </remarks>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders the document with the given model. The pipeline substitutes Scriban variables in the
    /// subject, preheader, generated MJML (incl. visibility conditions) and text version, HTML-escapes
    /// every expression result in the body, checks every URL against the scheme allow-list and then
    /// compiles the MJML to HTML. Failures are collected into <see cref="RenderResult.Errors"/>.
    /// </summary>
    /// <param name="document">The document to render.</param>
    /// <param name="model">The data the template reads.</param>
    /// <param name="options">Per-render options such as the formatter culture.</param>
    /// <param name="cancellationToken">Cancels the render; the only exception this method throws.</param>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    Task<RenderResult> RenderAsync(
        EmailTemplateDocument document,
        object? model = null,
        TemplateRenderOptions? options = null,
        CancellationToken cancellationToken = default);
}
