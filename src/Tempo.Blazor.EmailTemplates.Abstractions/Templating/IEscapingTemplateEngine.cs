using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The renderer-side view of a <see cref="ITemplateEngine"/> that can render one pass with an explicit
/// escaping mode. The mode is <c>internal</c> on purpose (D14): it is chosen by the render pipeline per
/// pass, never by a host. An engine that does not implement this interface still renders through
/// <see cref="ITemplateEngine.Render(string, object?, TemplateRenderOptions?)"/> (which escapes), but
/// the pipeline cannot ask it for a raw pass — so it is implemented by the Tempo engine itself.
/// </summary>
internal interface IEscapingTemplateEngine : ITemplateEngine
{
    /// <summary>Renders a template with the given escaping mode.</summary>
    Result<string> Render(string template, object? model, TemplateRenderOptions? options, TemplateEscapeMode escapeMode);
}
